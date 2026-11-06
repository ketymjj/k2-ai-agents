using FluentValidation;
using K2Agents.Application.Common;
using K2Agents.Application.Conversations;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Contacts;

public record ContactDto(
    Guid Id, Guid CompanyId, string? CompanyName, string Name, string? Email, string? Phone, string? Source,
    ContactStatus Status, bool IsLead, InterestLevel Interest, string? Notes, bool IsAnonymized,
    int ConversationCount, DateTime? LastConversationAt, DateTime CreatedAt, DateTime UpdatedAt);

public record ContactDetailDto(ContactDto Contact, IReadOnlyList<ConversationSummaryDto> Conversations);

public record SaveContactRequest(
    Guid? CompanyId, string Name, string? Email, string? Phone, string? Source,
    ContactStatus Status = ContactStatus.New, bool IsLead = false, InterestLevel Interest = InterestLevel.Unknown, string? Notes = null);

public record SetContactStatusRequest(ContactStatus Status);

public record FunnelStageDto(ContactStatus Status, int Count);

public record FunnelDto(IReadOnlyList<FunnelStageDto> Stages, int Total, int Leads);

public record ContactQuery : PagedQuery
{
    public Guid? CompanyId { get; init; }
    public ContactStatus? Status { get; init; }
    public bool? IsLead { get; init; }
    public InterestLevel? Interest { get; init; }
    public string? Search { get; init; }
}

public class SaveContactRequestValidator : AbstractValidator<SaveContactRequest>
{
    public SaveContactRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome.").MaximumLength(120);
        RuleFor(x => x.Email).EmailAddress().WithMessage("E-mail inválido.").MaximumLength(160).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone)
            .Must(p => ValidationRules.DigitsOnly(p) is { Length: >= 8 and <= 15 })
            .WithMessage("Telefone deve ter entre 8 e 15 dígitos.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Source).MaximumLength(40);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Interest).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
