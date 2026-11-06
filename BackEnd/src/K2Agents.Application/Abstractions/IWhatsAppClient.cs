namespace K2Agents.Application.Abstractions;

public record WhatsAppPhoneInfo(string? DisplayPhoneNumber, string? VerifiedName);

/// <summary>Erro da WhatsApp Cloud API (Graph API). A mensagem nunca contém o access token.</summary>
public class WhatsAppApiException(string code, string message, int? statusCode = null) : Exception(message)
{
    public string Code { get; } = code;
    public int? StatusCode { get; } = statusCode;
}

/// <summary>Cliente da API oficial WhatsApp Business Platform (Cloud API). Nada de automação do WhatsApp Web.</summary>
public interface IWhatsAppClient
{
    /// <summary>Envia texto; retorna o id da mensagem (wamid).</summary>
    Task<string> SendTextAsync(string phoneNumberId, string accessToken, string to, string text, CancellationToken ct);

    /// <summary>Consulta o número (usado no teste de conexão).</summary>
    Task<WhatsAppPhoneInfo> GetPhoneNumberAsync(string phoneNumberId, string accessToken, CancellationToken ct);
}
