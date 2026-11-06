using System.Text.Json;
using FluentValidation;

namespace K2Agents.Application.WhatsApp;

/// <summary>Configuração não sensível do canal (SettingsJson).</summary>
public record WhatsAppSettings
{
    public string PhoneNumberId { get; init; } = string.Empty;
    public string? BusinessAccountId { get; init; }
    public string? DisplayPhoneNumber { get; init; }

    /// <summary>Token que a Meta envia na verificação do webhook. Gerado pela plataforma.</summary>
    public string VerifyToken { get; init; } = string.Empty;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static WhatsAppSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new WhatsAppSettings();
        try { return JsonSerializer.Deserialize<WhatsAppSettings>(json, Json) ?? new WhatsAppSettings(); }
        catch (JsonException) { return new WhatsAppSettings(); }
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);
}

/// <summary>Credenciais do canal: guardadas apenas cifradas (ChannelConfiguration.EncryptedSecrets).</summary>
public record WhatsAppSecrets(string? AccessToken, string? AppSecret)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(AppSecret);
}

public static class WhatsAppCredentials
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Protect(Abstractions.ISecretProtector protector, WhatsAppSecrets secrets) =>
        protector.Protect(JsonSerializer.Serialize(secrets, Json));

    /// <summary>Credenciais decifradas. Falha de decifragem (chave trocada, dado alterado) equivale a "sem credenciais".</summary>
    public static WhatsAppSecrets Read(Abstractions.ISecretProtector protector, Domain.Entities.ChannelConfiguration channel)
    {
        if (string.IsNullOrWhiteSpace(channel.EncryptedSecrets)) return new WhatsAppSecrets(null, null);
        try
        {
            return JsonSerializer.Deserialize<WhatsAppSecrets>(protector.Unprotect(channel.EncryptedSecrets), Json) ?? new WhatsAppSecrets(null, null);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
        {
            return new WhatsAppSecrets(null, null);
        }
    }
}

/// <summary>Credenciais nulas ou vazias mantêm as já salvas (a API nunca as devolve para o navegador).</summary>
public record SaveWhatsAppRequest(
    Guid? CompanyId, Guid AgentId, string Name, string PhoneNumberId, string? BusinessAccountId, string? DisplayPhoneNumber,
    string? AccessToken, string? AppSecret);

/// <summary>Visão segura do canal: indica se há credenciais, nunca o valor delas.</summary>
public record WhatsAppChannelInfo(
    string PhoneNumberId, string? BusinessAccountId, string? DisplayPhoneNumber, string VerifyToken, bool HasAccessToken, bool HasAppSecret);

public record WhatsAppConnectionResult(bool Ok, string? DisplayPhoneNumber, string? VerifiedName, string? ErrorCode);

/// <summary>Mensagem recebida pelo webhook, já com assinatura conferida, aguardando processamento.</summary>
public record WhatsAppInbound(
    Guid ChannelId, string From, string? ProfileName, string MessageId, DateTime SentAt, string Type, string? Text);

public interface IWhatsAppInbox
{
    ValueTask EnqueueAsync(WhatsAppInbound message, CancellationToken ct);
}

public class SaveWhatsAppRequestValidator : AbstractValidator<SaveWhatsAppRequest>
{
    public SaveWhatsAppRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome do canal.").MaximumLength(80);
        RuleFor(x => x.AgentId).NotEmpty().WithMessage("Escolha o agente.");
        RuleFor(x => x.PhoneNumberId).NotEmpty().WithMessage("Informe o Phone Number ID do WhatsApp.")
            .Matches("^[0-9]{5,30}$").WithMessage("O Phone Number ID deve conter apenas números.");
        RuleFor(x => x.BusinessAccountId).Matches("^[0-9]{5,30}$").WithMessage("O WhatsApp Business Account ID deve conter apenas números.")
            .When(x => !string.IsNullOrWhiteSpace(x.BusinessAccountId));
        RuleFor(x => x.DisplayPhoneNumber).MaximumLength(30);
        RuleFor(x => x.AccessToken).MaximumLength(2048).Must(t => t is null || !t.Any(char.IsWhiteSpace))
            .WithMessage("O access token não pode conter espaços.");
        RuleFor(x => x.AppSecret).Matches("^[A-Za-z0-9]{16,128}$").WithMessage("O App Secret deve ter de 16 a 128 letras e números.")
            .When(x => !string.IsNullOrWhiteSpace(x.AppSecret));
    }
}
