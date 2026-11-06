using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using K2Agents.Application.Abstractions;

namespace K2Agents.Tests.Support;

/// <summary>Cliente da Cloud API simulado — nenhum teste chama a Meta de verdade.</summary>
public class FakeWhatsAppClient : IWhatsAppClient
{
    public record Sent(string PhoneNumberId, string AccessToken, string To, string Text);

    private int _counter;
    public ConcurrentQueue<Sent> Messages { get; } = new();

    /// <summary>Quando definido, toda chamada falha com este código (ex.: "invalid_access_token").</summary>
    public string? FailWith { get; set; }

    public Task<string> SendTextAsync(string phoneNumberId, string accessToken, string to, string text, CancellationToken ct)
    {
        if (FailWith is not null) throw new WhatsAppApiException(FailWith, "falha simulada");
        Messages.Enqueue(new Sent(phoneNumberId, accessToken, to, text));
        return Task.FromResult($"wamid.TEST{Interlocked.Increment(ref _counter)}");
    }

    public Task<WhatsAppPhoneInfo> GetPhoneNumberAsync(string phoneNumberId, string accessToken, CancellationToken ct)
    {
        if (FailWith is not null) throw new WhatsAppApiException(FailWith, "falha simulada");
        return Task.FromResult(new WhatsAppPhoneInfo("+55 11 4000-0000", "Empresa Teste"));
    }
}

/// <summary>Monta webhooks no formato da WhatsApp Cloud API, com assinatura HMAC como a Meta envia.</summary>
public static class WhatsAppWebhook
{
    public static string TextMessage(string phoneNumberId, string from, string messageId, string text, string? profileName = "Cliente Teste", DateTime? sentAt = null) =>
        JsonSerializer.Serialize(new
        {
            @object = "whatsapp_business_account",
            entry = new[]
            {
                new
                {
                    id = "WABA_ID",
                    changes = new[]
                    {
                        new
                        {
                            field = "messages",
                            value = new
                            {
                                messaging_product = "whatsapp",
                                metadata = new { display_phone_number = "551140000000", phone_number_id = phoneNumberId },
                                contacts = new[] { new { profile = new { name = profileName }, wa_id = from } },
                                messages = new[]
                                {
                                    new
                                    {
                                        from,
                                        id = messageId,
                                        timestamp = new DateTimeOffset(sentAt ?? DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
                                        type = "text",
                                        text = new { body = text }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

    public static string Sign(string appSecret, string body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static HttpRequestMessage Request(string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/whatsapp") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (signature is not null) request.Headers.Add("X-Hub-Signature-256", signature);
        return request;
    }
}
