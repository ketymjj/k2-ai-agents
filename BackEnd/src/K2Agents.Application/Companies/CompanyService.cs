using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Companies;

public interface ICompanyService
{
    Task<PagedResult<CompanyDto>> ListAsync(CompanyQuery query, CancellationToken ct);
    Task<CompanyDto> GetAsync(Guid id, CancellationToken ct);
    Task<CompanyDto> CreateAsync(SaveCompanyRequest request, CancellationToken ct);
    Task<CompanyDto> UpdateAsync(Guid id, SaveCompanyRequest request, CancellationToken ct);
    Task<CompanyDto> SetStatusAsync(Guid id, CompanyStatus status, CancellationToken ct);
    Task<CompanySummaryDto> SummaryAsync(Guid id, CancellationToken ct);
}

/// <summary>Companies não é ITenantEntity: o escopo por empresa é aplicado explicitamente aqui.</summary>
public class CompanyService(
    IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IValidator<SaveCompanyRequest> validator) : ICompanyService
{
    private IQueryable<Company> Scoped()
    {
        var query = db.Companies.AsNoTracking();
        return currentUser.IsSuperAdmin ? query : query.Where(c => c.Id == currentUser.CompanyId);
    }

    private static IQueryable<CompanyDto> Project(IQueryable<Company> query) =>
        query.Select(c => new CompanyDto(
            c.Id, c.Name, c.Document, c.Email, c.Phone, c.Industry, c.Website, c.Status,
            c.ConversationRetentionDays, c.CreatedAt, c.Agents.Count, c.Users.Count));

    public async Task<PagedResult<CompanyDto>> ListAsync(CompanyQuery query, CancellationToken ct)
    {
        var companies = Scoped();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            var digits = ValidationRules.DigitsOnly(query.Search);
            companies = companies.Where(c => c.Name.ToLower().Contains(term)
                                             || (c.Email != null && c.Email.Contains(term))
                                             || (digits != null && digits != "" && c.Document != null && c.Document.Contains(digits)));
        }
        if (query.Status.HasValue) companies = companies.Where(c => c.Status == query.Status);

        return await Project(companies.OrderBy(c => c.Name)).ToPagedAsync(query, ct);
    }

    public async Task<CompanyDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(Scoped().Where(c => c.Id == id)).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("Empresa");

    public async Task<CompanyDto> CreateAsync(SaveCompanyRequest request, CancellationToken ct)
    {
        if (!currentUser.IsSuperAdmin) throw AppException.Forbidden();
        await validator.ValidateAndThrowAsync(request, ct);

        var company = new Company();
        await ApplyAsync(company, request, ct);
        db.Companies.Add(company);
        // Toda empresa começa com os avisos padrão (novo lead, transferência, agendamento), editáveis em Automações.
        db.AutomationRules.AddRange(Automations.AutomationDefaults.For(company.Id));
        audit.Record("company.created", nameof(Company), company.Id.ToString(), company.Name, company.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(company.Id, ct);
    }

    public async Task<CompanyDto> UpdateAsync(Guid id, SaveCompanyRequest request, CancellationToken ct)
    {
        EnsureCanManage(id);
        await validator.ValidateAndThrowAsync(request, ct);

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("Empresa");
        await ApplyAsync(company, request, ct);
        audit.Record("company.updated", nameof(Company), company.Id.ToString(), companyId: company.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<CompanyDto> SetStatusAsync(Guid id, CompanyStatus status, CancellationToken ct)
    {
        if (!currentUser.IsSuperAdmin) throw AppException.Forbidden();
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("Empresa");
        if (company.Status == status) return await GetAsync(id, ct);

        company.Status = status;
        if (status == CompanyStatus.Inactive)
        {
            // Encerra as sessões ativas dos usuários da empresa desativada.
            var tokens = await db.RefreshTokens
                .Where(t => t.RevokedAt == null && t.User!.CompanyId == id).ToListAsync(ct);
            foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        }
        audit.Record(status == CompanyStatus.Active ? "company.activated" : "company.deactivated",
            nameof(Company), id.ToString(), companyId: id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<CompanySummaryDto> SummaryAsync(Guid id, CancellationToken ct)
    {
        if (!await Scoped().AnyAsync(c => c.Id == id, ct)) throw AppException.NotFound("Empresa");

        return new CompanySummaryDto(
            id,
            Agents: await db.AIAgents.CountAsync(a => a.CompanyId == id, ct),
            ActiveAgents: await db.AIAgents.CountAsync(a => a.CompanyId == id && a.Status == AgentStatus.Active, ct),
            Users: await db.Users.CountAsync(u => u.CompanyId == id, ct),
            Contacts: await db.Contacts.CountAsync(c => c.CompanyId == id, ct),
            Leads: await db.Contacts.CountAsync(c => c.CompanyId == id && c.IsLead, ct),
            Conversations: await db.Conversations.CountAsync(c => c.CompanyId == id, ct),
            OpenConversations: await db.Conversations.CountAsync(c => c.CompanyId == id && c.Status != ConversationStatus.Closed, ct),
            WaitingHumanConversations: await db.Conversations.CountAsync(c => c.CompanyId == id && c.Status == ConversationStatus.WaitingHuman, ct),
            Appointments: await db.Appointments.CountAsync(a => a.CompanyId == id, ct),
            KnowledgeDocuments: await db.KnowledgeDocuments.CountAsync(k => k.CompanyId == id, ct));
    }

    private void EnsureCanManage(Guid companyId)
    {
        if (currentUser.IsSuperAdmin) return;
        if (currentUser.Role == UserRole.CompanyAdmin && currentUser.CompanyId == companyId) return;
        throw AppException.Forbidden();
    }

    private async Task ApplyAsync(Company company, SaveCompanyRequest r, CancellationToken ct)
    {
        var document = ValidationRules.DigitsOnly(r.Document);
        if (document is not null && await db.Companies.AnyAsync(c => c.Document == document && c.Id != company.Id, ct))
            throw AppException.Conflict("Já existe uma empresa com este documento.");

        company.Name = r.Name.Trim();
        company.Document = document;
        company.Email = ValidationRules.TrimToNull(r.Email)?.ToLowerInvariant();
        company.Phone = ValidationRules.TrimToNull(r.Phone);
        company.Industry = ValidationRules.TrimToNull(r.Industry);
        company.Website = ValidationRules.TrimToNull(r.Website);
        company.ConversationRetentionDays = r.ConversationRetentionDays;
    }
}
