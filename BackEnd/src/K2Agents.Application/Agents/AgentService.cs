using System.Text.Json;
using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Agents;

public interface IAgentService
{
    Task<PagedResult<AgentListItemDto>> ListAsync(AgentQuery query, CancellationToken ct);
    Task<AgentDto> GetAsync(Guid id, CancellationToken ct);
    Task<AgentDto> CreateAsync(SaveAgentRequest request, CancellationToken ct);
    Task<AgentDto> UpdateAsync(Guid id, SaveAgentRequest request, CancellationToken ct);
    Task<AgentDto> DuplicateAsync(Guid id, CancellationToken ct);
    Task<AgentDto> SetStatusAsync(Guid id, AgentStatus status, CancellationToken ct);
    Task<PreviewPromptResponse> PreviewPromptAsync(PreviewPromptRequest request, CancellationToken ct);
    Task<IReadOnlyList<AgentVersionDto>> VersionsAsync(Guid id, CancellationToken ct);
    Task<AgentDto> RestoreVersionAsync(Guid id, int versionNumber, CancellationToken ct);
}

/// <summary>
/// AIAgent é ITenantEntity: o filtro global já limita usuários de empresa aos próprios agentes.
/// Toda alteração de configuração gera um snapshot imutável em AgentVersions.
/// </summary>
public class AgentService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAuditService audit,
    IPromptBuilder promptBuilder,
    IModelCatalog models,
    IValidator<SaveAgentRequest> saveValidator,
    IValidator<PreviewPromptRequest> previewValidator) : IAgentService
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AgentBehavior ParseBehavior(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AgentBehavior>(json, Json) ?? new AgentBehavior();
        }
        catch (JsonException)
        {
            return new AgentBehavior();
        }
    }

    public static string SerializeBehavior(AgentBehavior behavior) => JsonSerializer.Serialize(behavior, Json);

    public async Task<PagedResult<AgentListItemDto>> ListAsync(AgentQuery query, CancellationToken ct)
    {
        var agents = db.AIAgents.AsNoTracking();
        if (query.CompanyId.HasValue) agents = agents.Where(a => a.CompanyId == query.CompanyId);
        if (query.Status.HasValue) agents = agents.Where(a => a.Status == query.Status);
        if (query.BusinessType.HasValue) agents = agents.Where(a => a.BusinessType == query.BusinessType);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            agents = agents.Where(a => a.Name.ToLower().Contains(term) || (a.Description != null && a.Description.ToLower().Contains(term)));
        }

        return await agents
            .OrderBy(a => a.Status == AgentStatus.Active ? 0 : a.Status == AgentStatus.Draft ? 1 : 2)
            .ThenBy(a => a.Name)
            .Select(a => new AgentListItemDto(
                a.Id, a.CompanyId, a.Company != null ? a.Company.Name : null, a.Name, a.Description, a.BusinessType,
                a.Model, a.Status, a.ResponseLanguage, a.CurrentVersion, a.UpdatedAt))
            .ToPagedAsync(query, ct);
    }

    public async Task<AgentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var agent = await db.AIAgents.AsNoTracking().Include(a => a.Company).FirstOrDefaultAsync(a => a.Id == id, ct)
                    ?? throw AppException.NotFound("Agente");
        return ToDto(agent);
    }

    public async Task<AgentDto> CreateAsync(SaveAgentRequest request, CancellationToken ct)
    {
        await saveValidator.ValidateAndThrowAsync(request, ct);
        var company = await ResolveCompanyAsync(request.CompanyId, ct);

        var agent = new AIAgent { CompanyId = company.Id };
        Apply(agent, request, company.Name);
        agent.Status = AgentStatus.Draft;
        db.AIAgents.Add(agent);
        AddVersion(agent, request.ChangeNote ?? "Versão inicial");
        audit.Record("agent.created", nameof(AIAgent), agent.Id.ToString(), agent.Name, company.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(agent.Id, ct);
    }

    public async Task<AgentDto> UpdateAsync(Guid id, SaveAgentRequest request, CancellationToken ct)
    {
        await saveValidator.ValidateAndThrowAsync(request, ct);
        var agent = await db.AIAgents.Include(a => a.Company).FirstOrDefaultAsync(a => a.Id == id, ct)
                    ?? throw AppException.NotFound("Agente");

        var before = Fingerprint(agent);
        Apply(agent, request, agent.Company!.Name);
        agent.UpdatedAt = DateTime.UtcNow;

        if (Fingerprint(agent) != before)
        {
            agent.CurrentVersion++;
            AddVersion(agent, request.ChangeNote);
        }
        audit.Record("agent.updated", nameof(AIAgent), agent.Id.ToString(), $"v{agent.CurrentVersion}", agent.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AgentDto> DuplicateAsync(Guid id, CancellationToken ct)
    {
        var source = await db.AIAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound("Agente");
        var copy = new AIAgent
        {
            CompanyId = source.CompanyId,
            Name = source.Name.Length > 72 ? source.Name[..72] + " (cópia)" : source.Name + " (cópia)",
            Description = source.Description,
            BusinessType = source.BusinessType,
            SystemPrompt = source.SystemPrompt,
            BehaviorJson = source.BehaviorJson,
            WelcomeMessage = source.WelcomeMessage,
            Model = source.Model,
            Temperature = source.Temperature,
            MaxOutputTokens = source.MaxOutputTokens,
            ResponseLanguage = source.ResponseLanguage,
            Status = AgentStatus.Draft
        };
        db.AIAgents.Add(copy);
        AddVersion(copy, $"Duplicado de {source.Name} (v{source.CurrentVersion})");
        audit.Record("agent.duplicated", nameof(AIAgent), copy.Id.ToString(), $"origem {source.Id}", copy.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(copy.Id, ct);
    }

    public async Task<AgentDto> SetStatusAsync(Guid id, AgentStatus status, CancellationToken ct)
    {
        if (!Enum.IsDefined(status)) throw new AppException("Status inválido.");
        var agent = await db.AIAgents.Include(a => a.Company).FirstOrDefaultAsync(a => a.Id == id, ct)
                    ?? throw AppException.NotFound("Agente");

        if (status == AgentStatus.Active)
        {
            if (string.IsNullOrWhiteSpace(agent.SystemPrompt))
                throw new AppException("Configure o prompt do agente antes de ativá-lo.", 400, "prompt_required");
            if (agent.Company!.Status != CompanyStatus.Active)
                throw new AppException("A empresa está desativada.", 400, "company_inactive");
        }

        if (agent.Status != status)
        {
            audit.Record("agent.status_changed", nameof(AIAgent), agent.Id.ToString(), $"{agent.Status} -> {status}", agent.CompanyId);
            agent.Status = status;
            agent.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return ToDto(agent);
    }

    public async Task<PreviewPromptResponse> PreviewPromptAsync(PreviewPromptRequest request, CancellationToken ct)
    {
        await previewValidator.ValidateAndThrowAsync(request, ct);
        var company = await ResolveCompanyAsync(request.CompanyId, ct, requireActive: false);
        var prompt = promptBuilder.Build(request.Name, company.Name, request.BusinessType, request.ResponseLanguage, request.Behavior);
        return new PreviewPromptResponse(prompt, PlatformRules.For(request.ResponseLanguage));
    }

    public async Task<IReadOnlyList<AgentVersionDto>> VersionsAsync(Guid id, CancellationToken ct)
    {
        var agent = await db.AIAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound("Agente");
        var versions = await db.AgentVersions.AsNoTracking()
            .Where(v => v.AgentId == id)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);

        var authorIds = versions.Where(v => v.CreatedByUserId.HasValue).Select(v => v.CreatedByUserId!.Value).Distinct().ToList();
        var authors = await db.Users.AsNoTracking().Where(u => authorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return versions.Select(v => new AgentVersionDto(
            v.Id, v.VersionNumber, v.SystemPrompt, ParseBehavior(v.BehaviorJson), v.Model, v.Temperature, v.MaxOutputTokens,
            v.WelcomeMessage, v.ChangeNote,
            v.CreatedByUserId.HasValue && authors.TryGetValue(v.CreatedByUserId.Value, out var name) ? name : null,
            v.CreatedAt, v.VersionNumber == agent.CurrentVersion)).ToList();
    }

    public async Task<AgentDto> RestoreVersionAsync(Guid id, int versionNumber, CancellationToken ct)
    {
        var agent = await db.AIAgents.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound("Agente");
        var version = await db.AgentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.AgentId == id && v.VersionNumber == versionNumber, ct)
                      ?? throw AppException.NotFound("Versão");
        if (versionNumber == agent.CurrentVersion) return await GetAsync(id, ct);

        // Restaurar cria uma nova versão: o histórico nunca é reescrito.
        agent.SystemPrompt = version.SystemPrompt;
        agent.BehaviorJson = version.BehaviorJson;
        agent.Model = models.Find(version.Model)?.Id ?? models.Default.Id;
        agent.Temperature = version.Temperature;
        agent.MaxOutputTokens = version.MaxOutputTokens;
        agent.WelcomeMessage = version.WelcomeMessage;
        agent.UpdatedAt = DateTime.UtcNow;
        agent.CurrentVersion++;
        AddVersion(agent, $"Restaurado da v{versionNumber}");
        audit.Record("agent.version_restored", nameof(AIAgent), agent.Id.ToString(), $"v{versionNumber} -> v{agent.CurrentVersion}", agent.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task<Company> ResolveCompanyAsync(Guid? requested, CancellationToken ct, bool requireActive = true)
    {
        Guid companyId;
        if (currentUser.IsSuperAdmin)
            companyId = requested ?? throw new AppException("Informe a empresa do agente.", 400, "company_required");
        else
        {
            if (requested.HasValue && requested != currentUser.CompanyId) throw AppException.Forbidden();
            companyId = currentUser.CompanyId ?? throw AppException.Forbidden();
        }

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct) ?? throw AppException.NotFound("Empresa");
        if (requireActive && company.Status != CompanyStatus.Active)
            throw new AppException("A empresa está desativada.", 400, "company_inactive");
        return company;
    }

    private void Apply(AIAgent agent, SaveAgentRequest r, string companyName)
    {
        agent.Name = r.Name.Trim();
        agent.Description = ValidationRules.TrimToNull(r.Description);
        agent.BusinessType = r.BusinessType;
        agent.ResponseLanguage = r.ResponseLanguage;
        agent.BehaviorJson = SerializeBehavior(r.Behavior);
        agent.SystemPrompt = string.IsNullOrWhiteSpace(r.SystemPrompt)
            ? promptBuilder.Build(agent.Name, companyName, r.BusinessType, r.ResponseLanguage, r.Behavior)
            : r.SystemPrompt.Trim();
        agent.WelcomeMessage = ValidationRules.TrimToNull(r.WelcomeMessage);
        agent.Model = r.Model;
        agent.Temperature = r.Temperature;
        agent.MaxOutputTokens = r.MaxOutputTokens;
    }

    private void AddVersion(AIAgent agent, string? note)
    {
        db.AgentVersions.Add(new AgentVersion
        {
            CompanyId = agent.CompanyId,
            AgentId = agent.Id,
            VersionNumber = agent.CurrentVersion,
            SystemPrompt = agent.SystemPrompt,
            BehaviorJson = agent.BehaviorJson,
            Model = agent.Model,
            Temperature = agent.Temperature,
            MaxOutputTokens = agent.MaxOutputTokens,
            WelcomeMessage = agent.WelcomeMessage,
            CreatedByUserId = currentUser.UserId,
            ChangeNote = ValidationRules.TrimToNull(note)
        });
    }

    /// <summary>Campos que definem o comportamento; mudanças só de nome/descrição não geram versão.</summary>
    private static string Fingerprint(AIAgent a) =>
        string.Join('\u001f', a.SystemPrompt, a.BehaviorJson, a.Model, a.Temperature, a.MaxOutputTokens, a.WelcomeMessage,
            a.ResponseLanguage, a.BusinessType);

    private static AgentDto ToDto(AIAgent a) => new(
        a.Id, a.CompanyId, a.Company?.Name, a.Name, a.Description, a.BusinessType, a.SystemPrompt, a.WelcomeMessage,
        a.Model, a.Temperature, a.MaxOutputTokens, a.Status, a.ResponseLanguage, ParseBehavior(a.BehaviorJson),
        a.CurrentVersion, a.CreatedAt, a.UpdatedAt);
}
