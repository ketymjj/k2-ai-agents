using K2Agents.Application.Abstractions;
using K2Agents.Application.Automations;
using K2Agents.Application.Common;
using K2Agents.Application.Contacts;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Conversations;

public record ConversationSummaryDto(
    Guid Id, Guid CompanyId, string? CompanyName, Guid AgentId, string AgentName, ChannelType Channel, ConversationStatus Status,
    Guid? ContactId, string DisplayName, string? LastMessagePreview, SenderType? LastMessageSender, DateTime LastMessageAt,
    DateTime StartedAt, DateTime? ClosedAt, int MessageCount, bool IsDemo, Guid? AssignedUserId, string? AssignedUserName);

public record ConversationContactDto(Guid Id, string Name, string? Email, string? Phone, ContactStatus Status, bool IsLead, InterestLevel Interest);

public record ConversationDetailDto(ConversationSummaryDto Summary, IReadOnlyList<MessageDto> Messages, ConversationContactDto? Contact);

public record ConversationEventDto(string Action, string? Details, string? UserName, DateTime CreatedAt);

public record ReplyRequest(string Content);

/// <summary>Vincula um contato existente (ContactId) ou cria um novo com os dados informados.</summary>
public record LinkContactRequest(Guid? ContactId, string? Name, string? Email, string? Phone);

public record ConversationQuery : PagedQuery
{
    public Guid? CompanyId { get; init; }
    public Guid? AgentId { get; init; }
    public Guid? ContactId { get; init; }
    public ConversationStatus? Status { get; init; }
    public ChannelType? Channel { get; init; }
    /// <summary>true = apenas conversas atribuídas ao usuário atual.</summary>
    public bool? Mine { get; init; }
    /// <summary>Por padrão as sessões do Playground (testes internos) não aparecem na central.</summary>
    public bool IncludePlayground { get; init; }
    public string? Search { get; init; }
}

public interface IConversationService
{
    Task<PagedResult<ConversationSummaryDto>> ListAsync(ConversationQuery query, CancellationToken ct);
    Task<ConversationDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<ConversationSummaryDto> SummaryAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ConversationEventDto>> EventsAsync(Guid id, CancellationToken ct);
    Task<ConversationSummaryDto> CloseAsync(Guid id, CancellationToken ct);
    Task<ConversationSummaryDto> ReopenAsync(Guid id, CancellationToken ct);
    Task<ConversationSummaryDto> TransferAsync(Guid id, CancellationToken ct);
    Task<ConversationSummaryDto> ClaimAsync(Guid id, CancellationToken ct);
    Task<MessageDto> ReplyAsync(Guid id, ReplyRequest request, CancellationToken ct);
    Task<ConversationSummaryDto> ReturnToAiAsync(Guid id, CancellationToken ct);
    Task<ConversationDetailDto> LinkContactAsync(Guid id, LinkContactRequest request, CancellationToken ct);
}

/// <summary>Central de atendimento. Conversation é ITenantEntity: o filtro global isola as empresas.</summary>
public class ConversationService(
    IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IRealtimeNotifier realtime, IHandoffService handoff,
    IContactService contacts, IAutomationEngine automations) : IConversationService
{
    public async Task<PagedResult<ConversationSummaryDto>> ListAsync(ConversationQuery query, CancellationToken ct)
    {
        var conversations = db.Conversations.AsNoTracking();
        if (!query.IncludePlayground && query.Channel != ChannelType.Playground)
            conversations = conversations.Where(c => c.Channel != ChannelType.Playground);
        if (query.CompanyId.HasValue) conversations = conversations.Where(c => c.CompanyId == query.CompanyId);
        if (query.AgentId.HasValue) conversations = conversations.Where(c => c.AgentId == query.AgentId);
        if (query.ContactId.HasValue) conversations = conversations.Where(c => c.ContactId == query.ContactId);
        if (query.Status.HasValue) conversations = conversations.Where(c => c.Status == query.Status);
        if (query.Channel.HasValue) conversations = conversations.Where(c => c.Channel == query.Channel);
        if (query.Mine == true) conversations = conversations.Where(c => c.AssignedUserId == currentUser.UserId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            conversations = conversations.Where(c =>
                (c.Contact != null && (c.Contact.Name.ToLower().Contains(term) || (c.Contact.Email != null && c.Contact.Email.Contains(term))
                                       || (c.Contact.Phone != null && c.Contact.Phone.Contains(term))))
                || c.Messages.Any(m => m.Content.ToLower().Contains(term)));
        }

        return await Project(conversations.OrderByDescending(c => c.LastMessageAt)).ToPagedAsync(query, ct);
    }

    public async Task<ConversationDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var summary = await SummaryAsync(id, ct);
        var messages = await db.ConversationMessages.AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
        ConversationContactDto? contact = null;
        if (summary.ContactId is { } contactId)
            contact = await db.Contacts.AsNoTracking().Where(c => c.Id == contactId)
                .Select(c => new ConversationContactDto(c.Id, c.Name, c.Email, c.Phone, c.Status, c.IsLead, c.Interest))
                .FirstOrDefaultAsync(ct);
        return new ConversationDetailDto(summary, messages.Select(MessageMapping.ToDto).ToList(), contact);
    }

    public async Task<ConversationSummaryDto> SummaryAsync(Guid id, CancellationToken ct) =>
        await Project(db.Conversations.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw AppException.NotFound("Conversa");

    /// <summary>Trilha de auditoria da conversa (transferências, atribuições, encerramentos).</summary>
    public async Task<IReadOnlyList<ConversationEventDto>> EventsAsync(Guid id, CancellationToken ct)
    {
        await SummaryAsync(id, ct); // garante acesso pela empresa
        var key = id.ToString();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Conversation) && a.EntityId == key)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ConversationEventDto(a.Action, a.Details,
                db.Users.Where(u => u.Id == a.UserId).Select(u => u.Name).FirstOrDefault(), a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<ConversationSummaryDto> CloseAsync(Guid id, CancellationToken ct)
    {
        var conversation = await LoadAsync(id, ct);
        if (conversation.Status != ConversationStatus.Closed)
        {
            conversation.Status = ConversationStatus.Closed;
            conversation.ClosedAt = DateTime.UtcNow;
            var message = AddSystemMessage(conversation, "Conversa encerrada.");
            audit.Record("conversation.closed", nameof(Conversation), id.ToString(), companyId: conversation.CompanyId);
            await db.SaveChangesAsync(ct);
            await realtime.MessageCreatedAsync(conversation.CompanyId, conversation.Id, MessageMapping.ToDto(message), ct);
            await automations.PublishAsync(new AutomationEvent(conversation.CompanyId, AutomationTrigger.ConversationClosed,
                new Dictionary<string, string> { ["conversa"] = id.ToString()[..8], ["canal"] = conversation.Channel.ToString() },
                $"/conversations?id={id}"), ct);
        }
        return await NotifyAsync(conversation, ct);
    }

    public async Task<ConversationSummaryDto> ReopenAsync(Guid id, CancellationToken ct)
    {
        var conversation = await LoadAsync(id, ct);
        if (conversation.Status == ConversationStatus.Closed)
        {
            conversation.Status = ConversationStatus.Open;
            conversation.ClosedAt = null;
            conversation.AssignedUserId = null;
            var message = AddSystemMessage(conversation, "Conversa reaberta.");
            audit.Record("conversation.reopened", nameof(Conversation), id.ToString(), companyId: conversation.CompanyId);
            await db.SaveChangesAsync(ct);
            await realtime.MessageCreatedAsync(conversation.CompanyId, conversation.Id, MessageMapping.ToDto(message), ct);
        }
        return await NotifyAsync(conversation, ct);
    }

    public async Task<ConversationSummaryDto> TransferAsync(Guid id, CancellationToken ct)
    {
        var conversation = await LoadAsync(id, ct);
        if (conversation.Status != ConversationStatus.Open)
            throw new AppException("Só é possível transferir conversas que estão com a IA.", 409, "invalid_transition");
        await handoff.RequestAsync(conversation, HandoffOrigin.Manual, ct);
        return await SummaryAsync(id, ct);
    }

    public async Task<ConversationSummaryDto> ClaimAsync(Guid id, CancellationToken ct)
    {
        await handoff.ClaimAsync(await LoadAsync(id, ct), ct);
        return await SummaryAsync(id, ct);
    }

    public async Task<MessageDto> ReplyAsync(Guid id, ReplyRequest request, CancellationToken ct) =>
        await handoff.ReplyAsync(await LoadAsync(id, ct), request.Content, ct);

    public async Task<ConversationSummaryDto> ReturnToAiAsync(Guid id, CancellationToken ct)
    {
        var conversation = await LoadAsync(id, ct);
        if (conversation.Status is not (ConversationStatus.WaitingHuman or ConversationStatus.HumanActive))
            throw new AppException("A conversa não está com a equipe.", 409, "invalid_transition");
        await handoff.ReturnToAiAsync(conversation, ct);
        return await SummaryAsync(id, ct);
    }

    public async Task<ConversationDetailDto> LinkContactAsync(Guid id, LinkContactRequest request, CancellationToken ct)
    {
        var conversation = await LoadAsync(id, ct);
        Guid contactId;
        if (request.ContactId is { } existing)
        {
            var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == existing, ct) ?? throw AppException.NotFound("Contato");
            if (contact.CompanyId != conversation.CompanyId) throw AppException.Forbidden();
            contactId = contact.Id;
        }
        else
        {
            var created = await contacts.CreateAsync(new SaveContactRequest(
                conversation.CompanyId, request.Name ?? string.Empty, request.Email, request.Phone, conversation.Channel.ToString(),
                ContactStatus.InService, IsLead: !string.IsNullOrWhiteSpace(request.Email) || !string.IsNullOrWhiteSpace(request.Phone)), ct);
            contactId = created.Id;
        }

        conversation.ContactId = contactId;
        audit.Record("conversation.contact_linked", nameof(Conversation), id.ToString(), contactId.ToString(), conversation.CompanyId);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(conversation, ct);
        return await GetAsync(id, ct);
    }

    private async Task<Conversation> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.Channel != ChannelType.Playground, ct)
        ?? throw AppException.NotFound("Conversa");

    private ConversationMessage AddSystemMessage(Conversation conversation, string text)
    {
        var message = new ConversationMessage
        {
            CompanyId = conversation.CompanyId,
            ConversationId = conversation.Id,
            SenderType = SenderType.System,
            Content = text
        };
        db.ConversationMessages.Add(message);
        conversation.LastMessageAt = message.CreatedAt;
        return message;
    }

    private async Task<ConversationSummaryDto> NotifyAsync(Conversation conversation, CancellationToken ct)
    {
        var summary = await SummaryAsync(conversation.Id, ct);
        await realtime.ConversationUpdatedAsync(conversation.CompanyId, summary, ct);
        return summary;
    }

    /// <summary>Projeção única usada pela lista, pelo detalhe e pelos eventos em tempo real.</summary>
    public static IQueryable<ConversationSummaryDto> ProjectSummaries(IAppDbContext db, IQueryable<Conversation> query) =>
        query.Select(c => new ConversationSummaryDto(
            c.Id, c.CompanyId,
            db.Companies.Where(co => co.Id == c.CompanyId).Select(co => co.Name).FirstOrDefault(),
            c.AgentId, c.Agent!.Name, c.Channel, c.Status, c.ContactId,
            c.Contact != null ? c.Contact.Name : "Visitante " + c.Id.ToString().Substring(0, 4).ToUpper(),
            c.Messages.OrderByDescending(m => m.CreatedAt).Select(m => m.Content).FirstOrDefault(),
            c.Messages.OrderByDescending(m => m.CreatedAt).Select(m => (SenderType?)m.SenderType).FirstOrDefault(),
            c.LastMessageAt, c.StartedAt, c.ClosedAt, c.Messages.Count, c.IsDemo,
            c.AssignedUserId, c.AssignedUser != null ? c.AssignedUser.Name : null));

    private IQueryable<ConversationSummaryDto> Project(IQueryable<Conversation> query) => ProjectSummaries(db, query);
}
