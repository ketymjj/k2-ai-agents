using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Automations;
using K2Agents.Application.Common;
using K2Agents.Application.Conversations;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Contacts;

public interface IContactService
{
    Task<PagedResult<ContactDto>> ListAsync(ContactQuery query, CancellationToken ct);
    Task<FunnelDto> FunnelAsync(Guid? companyId, CancellationToken ct);
    Task<ContactDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<ContactDto> CreateAsync(SaveContactRequest request, CancellationToken ct);
    Task<ContactDto> UpdateAsync(Guid id, SaveContactRequest request, CancellationToken ct);
    Task<ContactDto> SetStatusAsync(Guid id, ContactStatus status, CancellationToken ct);
    Task<ContactDto> AnonymizeAsync(Guid id, CancellationToken ct);
}

/// <summary>Contatos e funil de leads. Contact é ITenantEntity (filtro global por empresa).</summary>
public class ContactService(
    IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IValidator<SaveContactRequest> validator,
    IAutomationEngine automations) : IContactService
{
    public const string AnonymizedName = "Contato anonimizado";
    public const string RemovedMessage = "[mensagem removida — dados do cliente anonimizados]";

    public async Task<PagedResult<ContactDto>> ListAsync(ContactQuery query, CancellationToken ct)
    {
        var contacts = db.Contacts.AsNoTracking();
        if (query.CompanyId.HasValue) contacts = contacts.Where(c => c.CompanyId == query.CompanyId);
        if (query.Status.HasValue) contacts = contacts.Where(c => c.Status == query.Status);
        if (query.IsLead.HasValue) contacts = contacts.Where(c => c.IsLead == query.IsLead);
        if (query.Interest.HasValue) contacts = contacts.Where(c => c.Interest == query.Interest);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            var digits = ValidationRules.DigitsOnly(query.Search);
            contacts = contacts.Where(c => c.Name.ToLower().Contains(term)
                                           || (c.Email != null && c.Email.Contains(term))
                                           || (digits != null && digits.Length >= 3 && c.Phone != null && c.Phone.Contains(digits)));
        }
        return await Project(contacts.OrderByDescending(c => c.UpdatedAt)).ToPagedAsync(query, ct);
    }

    public async Task<FunnelDto> FunnelAsync(Guid? companyId, CancellationToken ct)
    {
        var contacts = db.Contacts.AsNoTracking().Where(c => !c.IsAnonymized);
        if (companyId.HasValue) contacts = contacts.Where(c => c.CompanyId == companyId);
        var counts = await contacts.GroupBy(c => c.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var stages = Enum.GetValues<ContactStatus>()
            .Select(s => new FunnelStageDto(s, counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0)).ToList();
        return new FunnelDto(stages, stages.Sum(s => s.Count), await contacts.CountAsync(c => c.IsLead, ct));
    }

    public async Task<ContactDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var contact = await Project(db.Contacts.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct)
                      ?? throw AppException.NotFound("Contato");
        var conversations = await ConversationService.ProjectSummaries(db,
                db.Conversations.AsNoTracking().Where(c => c.ContactId == id).OrderByDescending(c => c.LastMessageAt))
            .Take(50).ToListAsync(ct);
        return new ContactDetailDto(contact, conversations);
    }

    public async Task<ContactDto> CreateAsync(SaveContactRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var companyId = ResolveCompany(request.CompanyId);
        var contact = new Contact { CompanyId = companyId };
        await ApplyAsync(contact, request, ct);
        db.Contacts.Add(contact);
        audit.Record("contact.created", nameof(Contact), contact.Id.ToString(), companyId: companyId);
        await db.SaveChangesAsync(ct);
        if (contact.IsLead)
            await automations.PublishAsync(new AutomationEvent(companyId, AutomationTrigger.LeadCreated,
                new Dictionary<string, string> { ["contato"] = contact.Name, ["canal"] = contact.Source ?? "Manual" },
                $"/contacts/{contact.Id}"), ct);
        return await SummaryAsync(contact.Id, ct);
    }

    public async Task<ContactDto> UpdateAsync(Guid id, SaveContactRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var contact = await LoadAsync(id, ct);
        if (contact.IsAnonymized) throw new AppException("Contatos anonimizados não podem ser editados.", 409, "contact_anonymized");
        await ApplyAsync(contact, request, ct);
        contact.UpdatedAt = DateTime.UtcNow;
        audit.Record("contact.updated", nameof(Contact), id.ToString(), companyId: contact.CompanyId);
        await db.SaveChangesAsync(ct);
        return await SummaryAsync(id, ct);
    }

    public async Task<ContactDto> SetStatusAsync(Guid id, ContactStatus status, CancellationToken ct)
    {
        if (!Enum.IsDefined(status)) throw new AppException("Status inválido.", 400, "invalid_status");
        var contact = await LoadAsync(id, ct);
        if (contact.Status != status)
        {
            audit.Record("contact.status_changed", nameof(Contact), id.ToString(), $"{contact.Status} -> {status}", contact.CompanyId);
            contact.Status = status;
            // Avançar no funil a partir de "Qualificado" implica tratar o contato como lead.
            if (status is ContactStatus.Qualified or ContactStatus.Converted) contact.IsLead = true;
            contact.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return await SummaryAsync(id, ct);
    }

    /// <summary>
    /// LGPD: remove dados pessoais do contato e o conteúdo das mensagens escritas por ele.
    /// Mantém registros estatísticos (contagens) sem identificar a pessoa. Irreversível.
    /// </summary>
    public async Task<ContactDto> AnonymizeAsync(Guid id, CancellationToken ct)
    {
        var contact = await LoadAsync(id, ct);
        if (contact.IsAnonymized) return await SummaryAsync(id, ct);

        contact.Name = AnonymizedName;
        contact.Email = null;
        contact.Phone = null;
        contact.Notes = null;
        contact.IsAnonymized = true;
        contact.UpdatedAt = DateTime.UtcNow;

        var conversations = await db.Conversations.Where(c => c.ContactId == id).ToListAsync(ct);
        var conversationIds = conversations.Select(c => c.Id).ToList();
        // WhatsApp guarda o número do cliente para poder responder: removido e a conversa encerrada.
        foreach (var c in conversations.Where(c => c.Channel == ChannelType.WhatsApp))
        {
            c.ExternalSessionId = null;
            if (c.Status != ConversationStatus.Closed)
            {
                c.Status = ConversationStatus.Closed;
                c.ClosedAt = DateTime.UtcNow;
            }
        }
        var messages = await db.ConversationMessages
            .Where(m => conversationIds.Contains(m.ConversationId) && m.SenderType == SenderType.Contact).ToListAsync(ct);
        foreach (var m in messages) m.Content = RemovedMessage;

        audit.Record("contact.anonymized", nameof(Contact), id.ToString(), $"{messages.Count} mensagens anonimizadas", contact.CompanyId);
        await db.SaveChangesAsync(ct);
        return await SummaryAsync(id, ct);
    }

    private Guid ResolveCompany(Guid? requested)
    {
        if (currentUser.IsSuperAdmin) return requested ?? throw new AppException("Informe a empresa.", 400, "company_required");
        if (requested.HasValue && requested != currentUser.CompanyId) throw AppException.Forbidden();
        return currentUser.CompanyId ?? throw AppException.Forbidden();
    }

    private async Task<Contact> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("Contato");

    private async Task ApplyAsync(Contact contact, SaveContactRequest r, CancellationToken ct)
    {
        var email = ValidationRules.TrimToNull(r.Email)?.ToLowerInvariant();
        var phone = ValidationRules.DigitsOnly(r.Phone);
        if (email is not null && await db.Contacts.AnyAsync(c => c.CompanyId == contact.CompanyId && c.Email == email && c.Id != contact.Id, ct))
            throw AppException.Conflict("Já existe um contato com este e-mail.");
        if (phone is not null && await db.Contacts.AnyAsync(c => c.CompanyId == contact.CompanyId && c.Phone == phone && c.Id != contact.Id, ct))
            throw AppException.Conflict("Já existe um contato com este telefone.");

        contact.Name = r.Name.Trim();
        contact.Email = email;
        contact.Phone = phone;
        contact.Source = ValidationRules.TrimToNull(r.Source) ?? contact.Source ?? "Manual";
        contact.Status = r.Status;
        contact.IsLead = r.IsLead || r.Status is ContactStatus.Qualified or ContactStatus.Converted;
        contact.Interest = r.Interest;
        contact.Notes = ValidationRules.TrimToNull(r.Notes);
    }

    private async Task<ContactDto> SummaryAsync(Guid id, CancellationToken ct) =>
        await Project(db.Contacts.AsNoTracking().Where(c => c.Id == id)).FirstAsync(ct);

    private IQueryable<ContactDto> Project(IQueryable<Contact> query) =>
        query.Select(c => new ContactDto(
            c.Id, c.CompanyId,
            db.Companies.Where(co => co.Id == c.CompanyId).Select(co => co.Name).FirstOrDefault(),
            c.Name, c.Email, c.Phone, c.Source, c.Status, c.IsLead, c.Interest, c.Notes, c.IsAnonymized,
            db.Conversations.Count(cv => cv.ContactId == c.Id),
            db.Conversations.Where(cv => cv.ContactId == c.Id).Max(cv => (DateTime?)cv.LastMessageAt),
            c.CreatedAt, c.UpdatedAt));
}
