using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using K2Agents.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace K2Agents.Infrastructure.WhatsApp;

/// <summary>
/// WhatsApp Business Platform — Cloud API (Graph API da Meta). O access token vai só no cabeçalho Authorization
/// de cada requisição e nunca aparece em logs ou mensagens de erro.
/// </summary>
public class WhatsAppCloudClient(HttpClient http, IConfiguration configuration, ILogger<WhatsAppCloudClient> logger) : IWhatsAppClient
{
    private string Base => $"https://graph.facebook.com/{configuration["WhatsApp:GraphApiVersion"] ?? "v21.0"}/";

    public async Task<string> SendTextAsync(string phoneNumberId, string accessToken, string to, string text, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}{Uri.EscapeDataString(phoneNumberId)}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to,
                type = "text",
                text = new { preview_url = false, body = text.Length > 4096 ? text[..4096] : text }
            })
        };
        using var response = await SendAsync(request, accessToken, ct);
        using var json = await ReadJsonAsync(response, ct);
        return json.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
               && messages[0].TryGetProperty("id", out var id) && id.GetString() is { } wamid
            ? wamid
            : throw new WhatsAppApiException("empty_response", "A Cloud API não retornou o id da mensagem.");
    }

    public async Task<WhatsAppPhoneInfo> GetPhoneNumberAsync(string phoneNumberId, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Base}{Uri.EscapeDataString(phoneNumberId)}?fields=display_phone_number,verified_name");
        using var response = await SendAsync(request, accessToken, ct);
        using var json = await ReadJsonAsync(response, ct);
        string? Field(string name) => json.RootElement.TryGetProperty(name, out var v) ? v.GetString() : null;
        return new WhatsAppPhoneInfo(Field("display_phone_number"), Field("verified_name"));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string accessToken, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        HttpResponseMessage response;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            response = await http.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new WhatsAppApiException("timeout", "A Cloud API não respondeu a tempo.");
        }
        catch (HttpRequestException)
        {
            throw new WhatsAppApiException("connection_failed", "Não foi possível conectar à Cloud API.");
        }

        if (response.IsSuccessStatusCode) return response;

        // Classifica o erro sem repassar o corpo (pode ecoar dados da requisição).
        var code = "provider_error";
        try
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (error.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) && c.TryGetInt32(out var metaCode))
                code = metaCode switch
                {
                    190 => "invalid_access_token",
                    100 => "invalid_parameter",
                    131030 => "recipient_not_allowed",
                    131047 => "outside_customer_service_window",
                    4 or 80007 or 130429 => "rate_limited",
                    _ => $"meta_{metaCode}"
                };
        }
        catch (JsonException) { }
        if (response.StatusCode == HttpStatusCode.Unauthorized) code = "invalid_access_token";
        var status = (int)response.StatusCode;
        response.Dispose();
        logger.LogWarning("Cloud API do WhatsApp respondeu {Status} ({Code}).", status, code);
        throw new WhatsAppApiException(code, "A Cloud API do WhatsApp recusou a requisição.", status);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new WhatsAppApiException("invalid_response", "Resposta inválida da Cloud API."); }
    }
}
