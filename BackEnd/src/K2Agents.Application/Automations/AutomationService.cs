using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Automations;

public record AutomationRuleDto(
    Guid Id, Guid CompanyId, string? CompanyName, string Name, AutomationTrigger TriggerType, AutomationAction ActionType,
    AutomationConfig Configuration, bool Enabled, int ExecutionCount, DateTime? LastExecutedAt, DateTime CreatedAt);

public record SaveAutomationRequest(Guid? CompanyId, string Name, AutomationTrigger TriggerType, AutomationAction ActionType, AutomationConfig? Configuration, bool Enabled = true);

public record SetAutomationEnabledRequest(bool Enabled);

public record AutomationCatalogDto(IReadOnlyDictionary<AutomationTrigger, AutomationAction[]> Allowed, IReadOnlyList<string> Placeholders);

public class SaveAutomationRequestValidator : AbstractValidator<SaveAutomationRequest>
{
    public SaveAutomationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome da automação.").MaximumLength(120);
        RuleFor(x => x.TriggerType).IsInEnum();
        RuleFor(x => x.ActionType).IsInEnum()
            .Must((r, action) => AutomationEngine.Allowed.TryGetValue(r.TriggerType, out var allowed) && allowed.Contains(action))
            .WithMessage("Esta ação não está disponível para o gatilho escolhido.");
        RuleFor(x => x.Configuration!.Title).MaximumLength(160).When(x => x.Configuration is not null);
        RuleFor(x => x.Configuration!.Message).MaximumLength(500).When(x => x.Configuration is not null);
    }
}

public interface IAutomationService
{
    AutomationCatalogDto Catalog();
    Task<IReadOnlyList<AutomationRuleDto>> ListAsync(Guid? companyId, CancellationToken ct);
    Task<AutomationRuleDto> CreateAsync(SaveAutomationRequest request, CancellationToken ct);
    Task<AutomationRuleDto> UpdateAsync(Guid id, SaveAutomationRequest request, CancellationToken ct);
    Task<AutomationRuleDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>Regras de automação. AutomationRule é ITenantEntity (filtro global por empresa).</summary>
public class AutomationService(
    IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IValidator<SaveAutomationRequest> validator) : IAutomationService
{
    public AutomationCatalogDto Catalog() => new(AutomationEngine.Allowed, AutomationEngine.Placeholders);

    public async Task<IReadOnlyList<AutomationRuleDto>> ListAsync(Guid? companyId, CancellationToken ct)
    {
        var rules = db.AutomationRules.AsNoTracking();
        if (companyId.HasValue) rules = rules.Where(r => r.CompanyId == companyId);
        var list = await rules.OrderBy(r => r.TriggerType).ThenBy(r => r.Name).ToListAsync(ct);
        var companyNames = await db.Companies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return list.Select(r => ToDto(r, companyNames.GetValueOrDefault(r.CompanyId))).ToList();
    }

    public async Task<AutomationRuleDto> CreateAsync(SaveAutomationRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var rule = new AutomationRule { CompanyId = ResolveCompany(request.CompanyId) };
        Apply(rule, request);
        db.AutomationRules.Add(rule);
        audit.Record("automation.created", nameof(AutomationRule), rule.Id.ToString(), rule.Name, rule.CompanyId);
        await db.SaveChangesAsync(ct);
        return ToDto(rule, null);
    }

    public async Task<AutomationRuleDto> UpdateAsync(Guid id, SaveAutomationRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var rule = await LoadAsync(id, ct);
        Apply(rule, request);
        audit.Record("automation.updated", nameof(AutomationRule), id.ToString(), rule.Name, rule.CompanyId);
        await db.SaveChangesAsync(ct);
        return ToDto(rule, null);
    }

    public async Task<AutomationRuleDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var rule = await LoadAsync(id, ct);
        if (rule.Enabled != enabled)
        {
            rule.Enabled = enabled;
            audit.Record(enabled ? "automation.enabled" : "automation.disabled", nameof(AutomationRule), id.ToString(), rule.Name, rule.CompanyId);
            await db.SaveChangesAsync(ct);
        }
        return ToDto(rule, null);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var rule = await LoadAsync(id, ct);
        db.AutomationRules.Remove(rule);
        audit.Record("automation.deleted", nameof(AutomationRule), id.ToString(), rule.Name, rule.CompanyId);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AutomationRule> LoadAsync(Guid id, CancellationToken ct) =>
        await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw AppException.NotFound("Automação");

    private Guid ResolveCompany(Guid? requested)
    {
        if (currentUser.IsSuperAdmin) return requested ?? throw new AppException("Informe a empresa.", 400, "company_required");
        if (requested.HasValue && requested != currentUser.CompanyId) throw AppException.Forbidden();
        return currentUser.CompanyId ?? throw AppException.Forbidden();
    }

    private static void Apply(AutomationRule rule, SaveAutomationRequest r)
    {
        rule.Name = r.Name.Trim();
        rule.TriggerType = r.TriggerType;
        rule.ActionType = r.ActionType;
        rule.Enabled = r.Enabled;
        var config = r.Configuration ?? new AutomationConfig(null, null);
        rule.Configuration = AutomationEngine.SerializeConfig(new AutomationConfig(
            ValidationRules.TrimToNull(config.Title), ValidationRules.TrimToNull(config.Message)));
    }

    private static AutomationRuleDto ToDto(AutomationRule r, string? companyName) => new(
        r.Id, r.CompanyId, companyName, r.Name, r.TriggerType, r.ActionType, AutomationEngine.ParseConfig(r.Configuration),
        r.Enabled, r.ExecutionCount, r.LastExecutedAt, r.CreatedAt);
}
