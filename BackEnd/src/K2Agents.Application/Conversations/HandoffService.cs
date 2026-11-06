using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Application.Automations;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Conversations;

public enum HandoffOrigin { Customer, AI, Manual }

/// <summary>Entrega a resposta do atendente no canal externo da conversa (ex.: WhatsApp). Retorna o id externo ou null.</summary>
public interface IChannelDelivery
{
    Task<string?> DeliverHumanReplyAsync(Conversation conversation, string text, CancellationToken ct);
}

public interface IHandoffService
{
    /// <summary>Open → WaitingHuman. A IA para de responder e a equipe é notificada.</summary>
    Task RequestAsync(Conversation conversation, HandoffOrigin origin, CancellationToken ct);
    /// <summary>→ HumanActive com o usuário atual como responsável.</summary>
    Task ClaimAsync(Conversation conversation, CancellationToken ct);
    /// <summary>Resposta do atendente; assume a conversa automaticamente se necessário.</summary>
    Task<MessageDto> ReplyAsync(Conversation conversation, string content, CancellationToken ct);
    /// <summary>WaitingHuman/HumanActive → Open: a IA volta a responder.</summary>
    Task ReturnToAiAsync(Conversation conversation, CancellationToken ct);
}

/// <summary>
/// Máquina de estados do atendimento humano. Toda transição gera mensagem de sistema (visível ao cliente),
/// registro de auditoria e evento em tempo real para a central.
/// </summary>
public class HandoffService(
    IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IAutomationEngine automations, IRealtimeNotifier realtime,
    IChannelDelivery delivery) : IHandoffService
{
    public const int MaxReplyChars = 4000;

    public async Task RequestAsync(Conversation conversation, HandoffOrigin origin, CancellationToken ct)
    {
        if (conversation.Status != ConversationStatus.Open) return;
        conversation.Status = ConversationStatus.WaitingHuman;
        var message = AddSystemMessage(conversation, "Conversa transferida para atendimento humano.");
        audit.Record("conversation.handoff_requested", nameof(Conversation), conversation.Id.ToString(),
            origin switch { HandoffOrigin.Customer => "Solicitado pelo cliente", HandoffOrigin.AI => "Solicitado pelo agente de IA", _ => "Transferência manual" },
            conversation.CompanyId);
        await db.SaveChangesAsync(ct);
        await PublishAsync(conversation, [message], ct);

        var agentName = await db.AIAgents.IgnoreQueryFilters().Where(a => a.Id == conversation.AgentId).Select(a => a.Name).FirstOrDefaultAsync(ct);
        var contactName = conversation.ContactId is null ? null
            : await db.Contacts.IgnoreQueryFilters().Where(c => c.Id == conversation.ContactId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        await automations.PublishAsync(new AutomationEvent(conversation.CompanyId, AutomationTrigger.ConversationTransferred,
            new Dictionary<string, string>
            {
                ["agente"] = agentName ?? "assistente",
                ["contato"] = contactName ?? "Cliente não identificado",
                ["canal"] = conversation.Channel.ToString(),
                ["conversa"] = conversation.Id.ToString()[..8]
            }, $"/conversations?id={conversation.Id}"), ct);
    }

    public async Task ClaimAsync(Conversation conversation, CancellationToken ct)
    {
        EnsureNotClosed(conversation);
        var userId = currentUser.UserId ?? throw AppException.Forbidden();
        if (conversation.Status == ConversationStatus.HumanActive && conversation.AssignedUserId == userId) return;

        var userName = await UserNameAsync(userId, ct);
        conversation.Status = ConversationStatus.HumanActive;
        conversation.AssignedUserId = userId;
        var message = AddSystemMessage(conversation, $"{userName} assumiu o atendimento.");

        // Contato novo em atendimento humano avança no funil.
        if (conversation.ContactId is { } contactId)
        {
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, ct);
            if (contact is { Status: ContactStatus.New }) contact.Status = ContactStatus.InService;
        }

        audit.Record("conversation.claimed", nameof(Conversation), conversation.Id.ToString(), userName, conversation.CompanyId);
        await db.SaveChangesAsync(ct);
        await PublishAsync(conversation, [message], ct);
    }

    public async Task<MessageDto> ReplyAsync(Conversation conversation, string content, CancellationToken ct)
    {
        EnsureNotClosed(conversation);
        var text = (content ?? string.Empty).Trim();
        if (text.Length == 0) throw new AppException("A mensagem está vazia.", 400, "empty_message");
        if (text.Length > MaxReplyChars) throw new AppException($"A mensagem excede {MaxReplyChars} caracteres.", 400, "message_too_long");

        // Responder significa assumir: a IA deixa de responder automaticamente.
        if (conversation.Status != ConversationStatus.HumanActive || conversation.AssignedUserId != currentUser.UserId)
            await ClaimAsync(conversation, ct);

        // Canais externos: entrega antes de gravar; se o envio falhar, a mensagem não aparece como enviada.
        var externalId = await delivery.DeliverHumanReplyAsync(conversation, text, ct);
        var message = new ConversationMessage
        {
            CompanyId = conversation.CompanyId,
            ConversationId = conversation.Id,
            SenderType = SenderType.Human,
            SenderUserId = currentUser.UserId,
            Content = text,
            ExternalMessageId = externalId
        };
        db.ConversationMessages.Add(message);
        conversation.LastMessageAt = message.CreatedAt;
        await db.SaveChangesAsync(ct);
        await PublishAsync(conversation, [message], ct);
        return MessageMapping.ToDto(message);
    }

    public async Task ReturnToAiAsync(Conversation conversation, CancellationToken ct)
    {
        if (conversation.Status is not (ConversationStatus.WaitingHuman or ConversationStatus.HumanActive)) return;
        conversation.Status = ConversationStatus.Open;
        conversation.AssignedUserId = null;
        var message = AddSystemMessage(conversation, "Atendimento devolvido ao assistente virtual.");
        audit.Record("conversation.returned_to_ai", nameof(Conversation), conversation.Id.ToString(),
            await UserNameAsync(currentUser.UserId, ct), conversation.CompanyId);
        await db.SaveChangesAsync(ct);
        await PublishAsync(conversation, [message], ct);
    }

    private static void EnsureNotClosed(Conversation conversation)
    {
        if (conversation.Status == ConversationStatus.Closed)
            throw new AppException("A conversa está encerrada. Reabra para continuar.", 409, "conversation_closed");
    }

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

    private async Task<string> UserNameAsync(Guid? userId, CancellationToken ct) =>
        userId is null ? "Equipe" : await db.Users.Where(u => u.Id == userId).Select(u => u.Name).FirstOrDefaultAsync(ct) ?? "Equipe";

    private async Task PublishAsync(Conversation conversation, IEnumerable<ConversationMessage> messages, CancellationToken ct)
    {
        foreach (var m in messages) await realtime.MessageCreatedAsync(conversation.CompanyId, conversation.Id, MessageMapping.ToDto(m), ct);
        var summary = await ConversationService.ProjectSummaries(db, db.Conversations.AsNoTracking().Where(c => c.Id == conversation.Id)).FirstAsync(ct);
        await realtime.ConversationUpdatedAsync(conversation.CompanyId, summary, ct);
    }
}
