using FluentValidation;
using K2Agents.Application.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Companies;

public record CompanyDto(
    Guid Id, string Name, string? Document, string? Email, string? Phone, string? Industry, string? Website,
    CompanyStatus Status, int? ConversationRetentionDays, DateTime CreatedAt, int AgentCount, int UserCount);

public record SaveCompanyRequest(
    string Name, string? Document, string? Email, string? Phone, string? Industry, string? Website, int? ConversationRetentionDays);

public record SetCompanyStatusRequest(CompanyStatus Status);

public record CompanyQuery : PagedQuery
{
    public string? Search { get; init; }
    public CompanyStatus? Status { get; init; }
}

public record CompanySummaryDto(
    Guid CompanyId,
    int Agents, int ActiveAgents, int Users,
    int Contacts, int Leads,
    int Conversations, int OpenConversations, int WaitingHumanConversations,
    int Appointments, int KnowledgeDocuments);

public class SaveCompanyRequestValidator : AbstractValidator<SaveCompanyRequest>
{
    public SaveCompanyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome da empresa.").MaximumLength(160);
        RuleFor(x => x.Document)
            .Must(d => ValidationRules.DigitsOnly(d) is null or { Length: 11 or 14 })
            .WithMessage("Documento deve ser um CPF (11 dígitos) ou CNPJ (14 dígitos).");
        RuleFor(x => x.Email).EmailAddress().WithMessage("E-mail inválido.").MaximumLength(160)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.Industry).MaximumLength(80);
        RuleFor(x => x.Website).MaximumLength(255)
            .Must(w => Uri.TryCreate(w, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Site deve ser uma URL http(s) válida.")
            .When(x => !string.IsNullOrWhiteSpace(x.Website));
        RuleFor(x => x.ConversationRetentionDays).InclusiveBetween(1, 3650)
            .WithMessage("Retenção deve estar entre 1 e 3650 dias.")
            .When(x => x.ConversationRetentionDays.HasValue);
    }
}
