using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Agents;

public record FaqItem(string Question, string Answer);

/// <summary>Configuração do assistente visual. Persistida em AIAgent.BehaviorJson e origem do prompt gerado.</summary>
public record AgentBehavior
{
    public string? CompanyDisplayName { get; init; }
    public string Objective { get; init; } = string.Empty;
    public string? Personality { get; init; }
    public string? Tone { get; init; }
    public string? BusinessHours { get; init; }
    public IReadOnlyList<FaqItem> Faqs { get; init; } = [];
    public IReadOnlyList<string> Restrictions { get; init; } = [];
    public IReadOnlyList<string> HandoffRules { get; init; } = [];
}

public record AgentDto(
    Guid Id, Guid CompanyId, string? CompanyName, string Name, string? Description, BusinessType BusinessType,
    string SystemPrompt, string? WelcomeMessage, string Model, decimal Temperature, int MaxOutputTokens,
    AgentStatus Status, string ResponseLanguage, AgentBehavior Behavior, int CurrentVersion, DateTime CreatedAt, DateTime UpdatedAt);

public record AgentListItemDto(
    Guid Id, Guid CompanyId, string? CompanyName, string Name, string? Description, BusinessType BusinessType,
    string Model, AgentStatus Status, string ResponseLanguage, int CurrentVersion, DateTime UpdatedAt);

public record SaveAgentRequest
{
    /// <summary>Obrigatório apenas para Super Admin; usuários de empresa sempre usam a própria.</summary>
    public Guid? CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public BusinessType BusinessType { get; init; } = BusinessType.GeneralInfo;
    public string ResponseLanguage { get; init; } = "pt-BR";
    public AgentBehavior Behavior { get; init; } = new();

    /// <summary>Prompt revisado pelo usuário. Vazio = gerar a partir do comportamento.</summary>
    public string? SystemPrompt { get; init; }
    public string? WelcomeMessage { get; init; }
    public string Model { get; init; } = string.Empty;
    public decimal Temperature { get; init; } = 0.4m;
    public int MaxOutputTokens { get; init; } = 600;
    public string? ChangeNote { get; init; }
}

public record PreviewPromptRequest(
    string Name, BusinessType BusinessType, string ResponseLanguage, AgentBehavior Behavior, Guid? CompanyId);

public record PreviewPromptResponse(string Prompt, string PlatformRules);

public record SetAgentStatusRequest(AgentStatus Status);

public record AgentVersionDto(
    Guid Id, int VersionNumber, string SystemPrompt, AgentBehavior Behavior, string Model, decimal Temperature,
    int MaxOutputTokens, string? WelcomeMessage, string? ChangeNote, string? CreatedByName, DateTime CreatedAt, bool IsCurrent);

public record AgentQuery : PagedQuery
{
    public Guid? CompanyId { get; init; }
    public string? Search { get; init; }
    public AgentStatus? Status { get; init; }
    public BusinessType? BusinessType { get; init; }
}

public class AgentBehaviorValidator : AbstractValidator<AgentBehavior>
{
    public AgentBehaviorValidator()
    {
        RuleFor(x => x.CompanyDisplayName).MaximumLength(160);
        RuleFor(x => x.Objective).NotEmpty().WithMessage("Descreva o objetivo do agente.").MaximumLength(1000);
        RuleFor(x => x.Personality).MaximumLength(300);
        RuleFor(x => x.Tone).MaximumLength(200);
        RuleFor(x => x.BusinessHours).MaximumLength(500);
        RuleFor(x => x.Faqs).Must(f => f.Count <= 50).WithMessage("Máximo de 50 perguntas frequentes.");
        RuleForEach(x => x.Faqs).ChildRules(faq =>
        {
            faq.RuleFor(f => f.Question).NotEmpty().WithMessage("Pergunta vazia.").MaximumLength(300);
            faq.RuleFor(f => f.Answer).NotEmpty().WithMessage("Resposta vazia.").MaximumLength(2000);
        });
        RuleFor(x => x.Restrictions).Must(r => r.Count <= 30).WithMessage("Máximo de 30 regras.");
        RuleForEach(x => x.Restrictions).NotEmpty().MaximumLength(300);
        RuleFor(x => x.HandoffRules).Must(r => r.Count <= 20).WithMessage("Máximo de 20 regras de transferência.");
        RuleForEach(x => x.HandoffRules).NotEmpty().MaximumLength(300);
    }
}

public class SaveAgentRequestValidator : AbstractValidator<SaveAgentRequest>
{
    public SaveAgentRequestValidator(IModelCatalog models)
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome do agente.").MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.BusinessType).IsInEnum();
        RuleFor(x => x.ResponseLanguage).Must(l => ValidationRules.SupportedLanguages.Contains(l)).WithMessage("Idioma não suportado.");
        RuleFor(x => x.Behavior).NotNull().SetValidator(new AgentBehaviorValidator());
        RuleFor(x => x.SystemPrompt).MaximumLength(20000);
        RuleFor(x => x.WelcomeMessage).MaximumLength(1000);
        RuleFor(x => x.Model).Must(m => models.Find(m) is not null).WithMessage("Modelo não disponível.");
        RuleFor(x => x.Temperature).InclusiveBetween(0m, 1m).WithMessage("Temperatura deve estar entre 0 e 1.");
        RuleFor(x => x.MaxOutputTokens).InclusiveBetween(50, 4000).WithMessage("Limite de resposta deve estar entre 50 e 4000 tokens.");
        RuleFor(x => x.ChangeNote).MaximumLength(300);
    }
}

public class PreviewPromptRequestValidator : AbstractValidator<PreviewPromptRequest>
{
    public PreviewPromptRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome do agente.").MaximumLength(80);
        RuleFor(x => x.ResponseLanguage).Must(l => ValidationRules.SupportedLanguages.Contains(l)).WithMessage("Idioma não suportado.");
        RuleFor(x => x.Behavior).NotNull().SetValidator(new AgentBehaviorValidator());
    }
}
