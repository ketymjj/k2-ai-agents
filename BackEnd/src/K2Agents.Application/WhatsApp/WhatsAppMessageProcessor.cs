using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Application.Contacts;
using K2Agents.Application.Conversations;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K2Agents.Application.WhatsApp;

public interface IWhatsAppMessageProcessor
{
    Task ProcessAsync(WhatsAppInbound inbound, CancellationToken ct);
}

/// <summary>
/// Processa uma mensagem recebida (assinatura já conferida): idempotência pelo wamid, conversa e contato pelo número,
/// resposta da IA enviada pela Cloud API. Sem IA real configurada, NÃO responde com texto simulado a um cliente real:
/// a conversa vai para a fila humana.
/// </summary>
public class WhatsAppMessageProcessor(
    IAppDbContext db,
    IAgentOrchestrator orchestrator,
    IAIProvider provider,
    AIUsageSettings limits,
    IRealtimeNotifier realtime,
    IContactCaptureService contactCapture,
    IHandoffService handoff,
    ISecretProtector secrets,
    IWhatsAppClient whatsApp,
    ILogger<WhatsAppMessageProcessor> logger) : IWhatsAppMessageProcessor
{
    public const string SessionPrefix = "wa:";

    public async Task ProcessAsync(WhatsAppInbound inbound, CancellationToken ct)
    {
        var channel = await db.ChannelConfigurations.IgnoreQueryFilters().Include(c => c.Agent)
            .FirstOrDefaultAsync(c => c.Id == inbound.ChannelId && c.ChannelType == ChannelType.WhatsApp, ct);
        if (channel?.Agent is null || channel.Status != ChannelStatus.Active || channel.Agent.Status != AgentStatus.Active)
        {
            logger.LogInformation("Mensagem do WhatsApp ignorada: canal {ChannelId} inativo ou sem agente ativo.", inbound.ChannelId);
            return;
        }
        if (!await db.Companies.IgnoreQueryFilters().AnyAsync(c => c.Id == channel.CompanyId && c.Status == CompanyStatus.Active, ct)) return;

        // A Meta reenvia webhooks: a mesma mensagem nunca é processada duas vezes.
        if (await db.ConversationMessages.IgnoreQueryFilters()
                .AnyAsync(m => m.CompanyId == channel.CompanyId && m.ExternalMessageId == inbound.MessageId, ct))
            return;

        var agent = channel.Agent;
        var conversation = await FindOrCreateConversationAsync(channel, inbound, ct);
        var history = await db.ConversationMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id).OrderBy(m => m.CreatedAt).ToListAsync(ct);

        var isText = inbound.Type == "text" && !string.IsNullOrWhiteSpace(inbound.Text);
        var content = isText ? inbound.Text!.Trim() : $"[{inbound.Type}]";
        if (content.Length > limits.MaxMessageChars) content = content[..limits.MaxMessageChars];

        var customerMessage = NewMessage(conversation, SenderType.Contact, content);
        customerMessage.ExternalMessageId = inbound.MessageId;
        db.ConversationMessages.Add(customerMessage);
        conversation.LastMessageAt = customerMessage.CreatedAt;
        await db.SaveChangesAsync(ct);
        await realtime.MessageCreatedAsync(conversation.CompanyId, conversation.Id, MessageMapping.ToDto(customerMessage), ct);

        if (isText) await contactCapture.CaptureAsync(conversation, content, ct);

        // Com atendimento humano em andamento, a IA não responde.
        if (conversation.Status == ConversationStatus.Open)
        {
            if (!isText)
                await ReplyAsync(channel, conversation, Unsupported(agent), null, ct);
            else if (!provider.IsConfigured)
            {
                AddSystem(conversation, "IA não configurada: a conversa foi encaminhada para a equipe.");
                await db.SaveChangesAsync(ct);
                await handoff.RequestAsync(conversation, HandoffOrigin.AI, ct);
            }
            else
            {
                try
                {
                    var reply = await orchestrator.GenerateReplyAsync(
                        new AgentReplyRequest(agent, history, content, ChannelType.WhatsApp, conversation.Id, false), ct);
                    var sent = await ReplyAsync(channel, conversation, reply.Content, reply, ct);
                    if (sent && reply.HandoffRequested)
                        await handoff.RequestAsync(conversation, HandoffDetector.CustomerAsksForHuman(content) ? HandoffOrigin.Customer : HandoffOrigin.AI, ct);
                }
                catch (AppException ex)
                {
                    logger.LogWarning("WhatsApp sem resposta automática na conversa {ConversationId}: {Code}", conversation.Id, ex.Code);
                    AddSystem(conversation, "A IA não conseguiu responder; a conversa foi encaminhada para a equipe.");
                    await db.SaveChangesAsync(ct);
                    await handoff.RequestAsync(conversation, HandoffOrigin.AI, ct);
                }
            }
        }

        await NotifyAsync(conversation, ct);
    }

    /// <summary>Envia pela Cloud API e grava a mensagem com o wamid. Falha no envio vira aviso interno + fila humana.</summary>
    private async Task<bool> ReplyAsync(ChannelConfiguration channel, Conversation conversation, string text, AgentReply? reply, CancellationToken ct)
    {
        var settings = WhatsAppSettings.Parse(channel.SettingsJson);
        var token = WhatsAppCredentials.Read(secrets, channel).AccessToken;
        var to = conversation.ExternalSessionId![SessionPrefix.Length..];
        try
        {
            if (string.IsNullOrWhiteSpace(token)) throw new WhatsAppApiException("missing_access_token", "Canal sem access token.");
            var wamid = await whatsApp.SendTextAsync(settings.PhoneNumberId, token, to, text, ct);

            var message = NewMessage(conversation, reply is null ? SenderType.System : SenderType.AI, text);
            message.ExternalMessageId = wamid;
            if (reply is not null)
            {
                message.InputTokens = reply.InputTokens;
                message.OutputTokens = reply.OutputTokens;
                message.ResponseTimeMs = reply.ResponseTimeMs;
                message.IsSimulated = reply.IsSimulated;
                message.KnowledgeSources = MessageMapping.SerializeSources(reply.Sources);
            }
            db.ConversationMessages.Add(message);
            conversation.LastMessageAt = message.CreatedAt;
            await db.SaveChangesAsync(ct);
            await realtime.MessageCreatedAsync(conversation.CompanyId, conversation.Id, MessageMapping.ToDto(message), ct);
            return true;
        }
        catch (WhatsAppApiException ex)
        {
            logger.LogWarning("Falha ao enviar pelo WhatsApp na conversa {ConversationId}: {Code}", conversation.Id, ex.Code);
            var notice = AddSystem(conversation, $"Falha ao enviar a resposta pelo WhatsApp ({ex.Code}). Responda manualmente.");
            await db.SaveChangesAsync(ct);
            await realtime.MessageCreatedAsync(conversation.CompanyId, conversation.Id, MessageMapping.ToDto(notice), ct);
            await handoff.RequestAsync(conversation, HandoffOrigin.AI, ct);
            return false;
        }
    }

    private async Task<Conversation> FindOrCreateConversationAsync(ChannelConfiguration channel, WhatsAppInbound inbound, CancellationToken ct)
    {
        var session = SessionPrefix + inbound.From;
        var conversation = await db.Conversations.IgnoreQueryFilters()
            .Where(c => c.CompanyId == channel.CompanyId && c.Channel == ChannelType.WhatsApp && c.ExternalSessionId == session
                        && c.Status != ConversationStatus.Closed)
            .OrderByDescending(c => c.LastMessageAt).FirstOrDefaultAsync(ct);
        if (conversation is not null) return conversation;

        // O número vem da própria plataforma do WhatsApp (o cliente iniciou a conversa); o nome é o do perfil dele.
        var phone = new string(inbound.From.Where(char.IsDigit).ToArray());
        var contact = await db.Contacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CompanyId == channel.CompanyId && c.Phone == phone && !c.IsAnonymized, ct);
        if (contact is null)
        {
            contact = new Contact
            {
                CompanyId = channel.CompanyId,
                Name = string.IsNullOrWhiteSpace(inbound.ProfileName) ? $"WhatsApp {phone}" : inbound.ProfileName.Trim()[..Math.Min(inbound.ProfileName.Trim().Length, 120)],
                Phone = phone,
                Source = "WhatsApp",
                Status = ContactStatus.InService
            };
            db.Contacts.Add(contact);
        }

        conversation = new Conversation
        {
            CompanyId = channel.CompanyId,
            AgentId = channel.AgentId,
            ContactId = contact.Id,
            Channel = ChannelType.WhatsApp,
            Status = ConversationStatus.Open,
            ExternalSessionId = session,
            StartedAt = inbound.SentAt,
            LastMessageAt = inbound.SentAt
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    private async Task NotifyAsync(Conversation conversation, CancellationToken ct)
    {
        var summary = await ConversationService.ProjectSummaries(db, db.Conversations.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == conversation.Id))
            .FirstAsync(ct);
        await realtime.ConversationUpdatedAsync(conversation.CompanyId, summary, ct);
    }

    private ConversationMessage AddSystem(Conversation conversation, string text)
    {
        var message = NewMessage(conversation, SenderType.System, text);
        db.ConversationMessages.Add(message);
        return message;
    }

    private static ConversationMessage NewMessage(Conversation conversation, SenderType sender, string content) => new()
    {
        CompanyId = conversation.CompanyId,
        ConversationId = conversation.Id,
        SenderType = sender,
        Content = content
    };

    private static string Unsupported(AIAgent agent) => agent.ResponseLanguage == "en"
        ? "For now I can only read text messages. Could you type your question?"
        : "Por enquanto só consigo ler mensagens de texto. Pode escrever a sua dúvida?";
}
