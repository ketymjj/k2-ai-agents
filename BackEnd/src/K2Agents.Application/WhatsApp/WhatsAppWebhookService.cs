using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using K2Agents.Application.Abstractions;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K2Agents.Application.WhatsApp;

public enum WebhookOutcome { Accepted, Rejected }

public interface IWhatsAppWebhookService
{
    /// <summary>Verificação do webhook pela Meta: devolve o desafio se o verify token pertencer a um canal; senão, null.</summary>
    Task<string?> VerifyAsync(string? mode, string? verifyToken, string? challenge, CancellationToken ct);

    /// <summary>Confere a assinatura X-Hub-Signature-256 e enfileira as mensagens. Nada é processado sem assinatura válida.</summary>
    Task<WebhookOutcome> ReceiveAsync(byte[] body, string? signatureHeader, CancellationToken ct);
}

/// <summary>Endpoint público chamado pela Meta. Requisições anônimas: todo acesso é escopado pelo canal do número.</summary>
public class WhatsAppWebhookService(
    IAppDbContext db, ISecretProtector secrets, IWhatsAppInbox inbox, ILogger<WhatsAppWebhookService> logger) : IWhatsAppWebhookService
{
    public async Task<string?> VerifyAsync(string? mode, string? verifyToken, string? challenge, CancellationToken ct)
    {
        if (mode != "subscribe" || string.IsNullOrEmpty(verifyToken) || verifyToken.Length > 200 || string.IsNullOrEmpty(challenge)) return null;
        var channels = await db.ChannelConfigurations.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.ChannelType == ChannelType.WhatsApp).Select(c => c.SettingsJson).ToListAsync(ct);
        var expected = Encoding.UTF8.GetBytes(verifyToken);
        var match = channels.Any(json =>
        {
            var token = WhatsAppSettings.Parse(json).VerifyToken;
            return token.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), expected);
        });
        return match ? challenge : null;
    }

    public async Task<WebhookOutcome> ReceiveAsync(byte[] body, string? signatureHeader, CancellationToken ct)
    {
        if (!TryParseSignature(signatureHeader, out var signature)) return WebhookOutcome.Rejected;

        JsonDocument document;
        try { document = JsonDocument.Parse(body); }
        catch (JsonException) { return WebhookOutcome.Rejected; }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("object", out var obj) || obj.GetString() != "whatsapp_business_account")
                return WebhookOutcome.Rejected;

            var changes = Values(root).ToList();
            var phoneIds = changes.Select(v => v.PhoneNumberId).Where(id => id is not null).Distinct().ToList();
            if (phoneIds.Count == 0) return WebhookOutcome.Rejected;

            var channels = await db.ChannelConfigurations.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.ChannelType == ChannelType.WhatsApp && phoneIds.Contains(c.ExternalAccountId!)).ToListAsync(ct);

            // Só canais cujo App Secret confere com a assinatura do corpo bruto.
            var trusted = channels.Where(c =>
            {
                var appSecret = WhatsAppCredentials.Read(secrets, c).AppSecret;
                return appSecret is not null && SignatureMatches(appSecret, body, signature);
            }).ToDictionary(c => c.ExternalAccountId!, c => c.Id);
            if (trusted.Count == 0)
            {
                logger.LogWarning("Webhook do WhatsApp rejeitado: assinatura inválida ou número não cadastrado.");
                return WebhookOutcome.Rejected;
            }

            foreach (var value in changes)
            {
                if (value.PhoneNumberId is null || !trusted.TryGetValue(value.PhoneNumberId, out var channelId)) continue;
                foreach (var message in value.Messages)
                    await inbox.EnqueueAsync(message with { ChannelId = channelId }, ct);
            }
            return WebhookOutcome.Accepted;
        }
    }

    public static bool SignatureMatches(string appSecret, byte[] body, byte[] signature)
    {
        var computed = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return CryptographicOperations.FixedTimeEquals(computed, signature);
    }

    private static bool TryParseSignature(string? header, out byte[] signature)
    {
        signature = [];
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase) || header.Length != 7 + 64) return false;
        try
        {
            signature = Convert.FromHexString(header[7..]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record ChangeValue(string? PhoneNumberId, IReadOnlyList<WhatsAppInbound> Messages);

    /// <summary>Lê entry[].changes[].value do payload da Cloud API (statuses de entrega são ignorados).</summary>
    private static IEnumerable<ChangeValue> Values(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) yield break;
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;
                var phoneId = value.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("phone_number_id", out var pid) ? pid.GetString() : null;

                var names = new Dictionary<string, string>();
                if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
                    foreach (var c in contacts.EnumerateArray())
                        if (c.TryGetProperty("wa_id", out var waId) && c.TryGetProperty("profile", out var profile) && profile.TryGetProperty("name", out var name)
                            && waId.GetString() is { } id && name.GetString() is { } n)
                            names[id] = n;

                var messages = new List<WhatsAppInbound>();
                if (value.TryGetProperty("messages", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var m in list.EnumerateArray())
                    {
                        var from = m.TryGetProperty("from", out var f) ? f.GetString() : null;
                        var id = m.TryGetProperty("id", out var i) ? i.GetString() : null;
                        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(id) || from.Length > 20 || id.Length > 200) continue;
                        var type = m.TryGetProperty("type", out var t) ? t.GetString() ?? "unknown" : "unknown";
                        var text = type == "text" && m.TryGetProperty("text", out var tx) && tx.TryGetProperty("body", out var b) ? b.GetString() : null;
                        var sentAt = m.TryGetProperty("timestamp", out var ts) && long.TryParse(ts.GetString(), out var unix)
                            ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : DateTime.UtcNow;
                        messages.Add(new WhatsAppInbound(Guid.Empty, from, names.GetValueOrDefault(from), id, sentAt, type, text));
                    }

                yield return new ChangeValue(phoneId, messages);
            }
        }
    }
}
