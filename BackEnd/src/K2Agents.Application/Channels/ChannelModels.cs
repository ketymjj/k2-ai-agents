using FluentValidation;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Channels;

/// <summary>Configuração visual/comportamental do Web Chat. Nada aqui é sensível: é entregue ao widget público.</summary>
public record WebChatSettings
{
    public string Title { get; init; } = "Atendimento";
    public string? Subtitle { get; init; }
    public string PrimaryColor { get; init; } = "#6941C6";
    public string Position { get; init; } = "right";
    /// <summary>Vazio = mensagem de boas-vindas do agente.</summary>
    public string? WelcomeMessage { get; init; }
    /// <summary>Sem chave de IA: responder no modo demonstrativo (respostas marcadas como simuladas no widget).</summary>
    public bool AllowSimulatedReplies { get; init; } = true;
    /// <summary>Exibida quando a IA não pode responder (sem chave e sem modo demonstrativo, ou falha do provedor).</summary>
    public string? OfflineMessage { get; init; }
}

public record ChannelDto(
    Guid Id, Guid CompanyId, string? CompanyName, Guid AgentId, string AgentName, ChannelType ChannelType, string Name,
    ChannelStatus Status, string? PublicToken, WebChatSettings Settings, IReadOnlyList<string> AllowedOrigins,
    int ConversationCount, DateTime CreatedAt, DateTime UpdatedAt, WhatsApp.WhatsAppChannelInfo? WhatsApp = null);

public record SaveWebChatRequest(Guid? CompanyId, Guid AgentId, string Name, WebChatSettings Settings, IReadOnlyList<string>? AllowedOrigins);

public record SetChannelStatusRequest(ChannelStatus Status);

public class WebChatSettingsValidator : AbstractValidator<WebChatSettings>
{
    public WebChatSettingsValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Informe o título do chat.").MaximumLength(60);
        RuleFor(x => x.Subtitle).MaximumLength(100);
        RuleFor(x => x.PrimaryColor).Matches("^#[0-9A-Fa-f]{6}$").WithMessage("Cor deve estar no formato #RRGGBB.");
        RuleFor(x => x.Position).Must(p => p is "right" or "left").WithMessage("Posição deve ser 'right' ou 'left'.");
        RuleFor(x => x.WelcomeMessage).MaximumLength(500);
        RuleFor(x => x.OfflineMessage).MaximumLength(300);
    }
}

public class SaveWebChatRequestValidator : AbstractValidator<SaveWebChatRequest>
{
    public SaveWebChatRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome do canal.").MaximumLength(120);
        RuleFor(x => x.AgentId).NotEmpty().WithMessage("Escolha o agente.");
        RuleFor(x => x.Settings).NotNull().SetValidator(new WebChatSettingsValidator());
        RuleFor(x => x.AllowedOrigins).Must(o => o is null || o.Count <= 20).WithMessage("Máximo de 20 domínios.");
        RuleForEach(x => x.AllowedOrigins).Must(o => ChannelOrigins.Normalize(o) is not null)
            .WithMessage("Domínio inválido. Use o formato https://www.seusite.com.br");
    }
}

public static class ChannelOrigins
{
    /// <summary>Normaliza para "esquema://host[:porta]" em minúsculas; null se inválido.</summary>
    public static string? Normalize(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return null;
        if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    public static IReadOnlyList<string> Parse(string? stored) =>
        string.IsNullOrWhiteSpace(stored) ? [] : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
