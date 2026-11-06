using System.Security.Cryptography;
using System.Text;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Channels;
using K2Agents.Application.Common;
using K2Agents.Application.Contacts;
using K2Agents.Application.Conversations;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K2Agents.Application.WebChat;

public record WebChatConfigDto(
    string Title, string? Subtitle, string PrimaryColor, string Position, string AgentName, string? WelcomeMessage,
    bool SimulatedMode, bool RepliesAvailable, int MaxMessageChars, string Language);

public record StartWebChatRequest(string Token, Guid? SessionId, string? VisitorKey);

/// <summary>VisitorKey só é devolvida ao criar a sessão; o servidor guarda apenas o hash.</summary>
public record WebChatSessionDto(Guid SessionId, string? VisitorKey, bool Resumed, IReadOnlyList<PublicMessageDto> Messages);

public record SendWebChatMessageRequest(string Token, Guid SessionId, string VisitorKey, string Content);

public record WebChatSendResult(PublicMessageDto UserMessage, PublicMessageDto? Reply);

public interface IWebChatService
{
    Task<WebChatConfigDto> GetConfigAsync(string token, string? origin, CancellationToken ct);
    Task<WebChatSessionDto> StartSessionAsync(StartWebChatRequest request, string? origin, CancellationToken ct);
    Task<WebChatSendResult> SendAsync(SendWebChatMessageRequest request, string? origin, CancellationToken ct);
    Task<IReadOnlyList<PublicMessageDto>> MessagesAsync(string token, Guid sessionId, string visitorKey, DateTime? after, string? origin, CancellationToken ct);
    /// <summary>Usado pelo hub de tempo real para autorizar o visitante na conversa.</summary>
    Task<Guid> AuthorizeVisitorAsync(string token, Guid sessionId, string visitorKey, string? origin, CancellationToken ct);
}

/// <summary>
/// API pública do widget. Requisições anônimas: não há filtro de empresa pelo usuário, então TODO acesso é
/// escopado pelo canal (token) e, para conversas, pela chave secreta do visitante.
/// </summary>
public class WebChatService(
    IAppDbContext db,
    IAgentOrchestrator orchestrator,
    IAIProvider provider,
    AIUsageSettings limits,
    IRealtimeNotifier realtime,
    IContactCaptureService contactCapture,
    IHandoffService handoff,
    ILogger<WebChatService> logger) : IWebChatService
{
    public const int MaxContactMessagesPerConversation = 200;
    private const string KeyPrefix = "wc:";

    private sealed record ChannelContext(ChannelConfiguration Channel, AIAgent Agent, WebChatSettings Settings);

    public async Task<WebChatConfigDto> GetConfigAsync(string token, string? origin, CancellationToken ct)
    {
        var ctx = await ResolveChannelAsync(token, origin, ct);
        var simulated = !provider.IsConfigured;
        return new WebChatConfigDto(
            ctx.Settings.Title, ctx.Settings.Subtitle, ctx.Settings.PrimaryColor, ctx.Settings.Position, ctx.Agent.Name,
            WelcomeOf(ctx), simulated && ctx.Settings.AllowSimulatedReplies, !simulated || ctx.Settings.AllowSimulatedReplies,
            limits.MaxMessageChars, ctx.Agent.ResponseLanguage);
    }

    public async Task<WebChatSessionDto> StartSessionAsync(StartWebChatRequest request, string? origin, CancellationToken ct)
    {
        var ctx = await ResolveChannelAsync(request.Token, origin, ct);

        // Retoma a conversa do visitante se a chave conferir e ela ainda estiver aberta.
        if (request.SessionId.HasValue && !string.IsNullOrWhiteSpace(request.VisitorKey))
        {
            var existing = await FindConversationAsync(ctx, request.SessionId.Value, request.VisitorKey, ct);
            if (existing is not null && existing.Status != ConversationStatus.Closed)
                return new WebChatSessionDto(existing.Id, null, true, await PublicMessagesAsync(existing.Id, null, ct));
        }

        var visitorKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var conversation = new Conversation
        {
            CompanyId = ctx.Channel.CompanyId,
            AgentId = ctx.Agent.Id,
            Channel = ChannelType.WebChat,
            Status = ConversationStatus.Open,
            ExternalSessionId = KeyPrefix + Hash(visitorKey)
        };
        db.Conversations.Add(conversation);
        var welcome = WelcomeOf(ctx);
        ConversationMessage? welcomeMessage = null;
        if (welcome is not null)
        {
            welcomeMessage = NewMessage(conversation, SenderType.System, welcome);
            db.ConversationMessages.Add(welcomeMessage);
        }
        await db.SaveChangesAsync(ct);

        await NotifyConversationAsync(conversation, ct);
        return new WebChatSessionDto(conversation.Id, visitorKey, false,
            welcomeMessage is null ? [] : [MessageMapping.ToPublic(welcomeMessage)]);
    }

    public async Task<WebChatSendResult> SendAsync(SendWebChatMessageRequest request, string? origin, CancellationToken ct)
    {
        var ctx = await ResolveChannelAsync(request.Token, origin, ct);
        var conversation = await FindConversationAsync(ctx, request.SessionId, request.VisitorKey, ct)
                           ?? throw AppException.NotFound("Conversa");
        if (conversation.Status == ConversationStatus.Closed)
            throw new AppException("Esta conversa foi encerrada. Inicie uma nova conversa.", 409, "conversation_closed");

        var content = (request.Content ?? string.Empty).Trim();
        if (content.Length == 0) throw new AppException("A mensagem está vazia.", 400, "empty_message");
        if (content.Length > limits.MaxMessageChars)
            throw new AppException($"A mensagem excede o limite de {limits.MaxMessageChars} caracteres.", 400, "message_too_long");

        var history = await db.ConversationMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        if (history.Count(m => m.SenderType == SenderType.Contact) >= MaxContactMessagesPerConversation)
            throw new AppException("Limite de mensagens desta conversa atingido.", 429, "conversation_limit");

        // A mensagem do cliente é gravada antes da IA: mesmo que a IA falhe, um atendente poderá vê-la.
        var userMessage = NewMessage(conversation, SenderType.Contact, content);
        db.ConversationMessages.Add(userMessage);
        conversation.LastMessageAt = userMessage.CreatedAt;
        await db.SaveChangesAsync(ct);
        await realtime.MessageCreatedAsync(conversation.CompanyId, conversation.Id, MessageMapping.ToDto(userMessage), ct);

        // Dados de contato só a partir do que o próprio cliente escreveu.
        await contactCapture.CaptureAsync(conversation, content, ct);

        ConversationMessage? reply = null;
        // Com atendimento humano em andamento, a IA não responde automaticamente.
        if (conversation.Status == ConversationStatus.Open)
        {
            (reply, var handoffRequested) = await GenerateReplyAsync(ctx, conversation, history, content, ct);
            db.ConversationMessages.Add(reply);
            conversation.LastMessageAt = reply.CreatedAt;
            await db.SaveChangesAsync(ct);
            await realtime.MessageCreatedAsync(conversation.CompanyId, conversation.Id, MessageMapping.ToDto(reply), ct);

            if (handoffRequested)
                await handoff.RequestAsync(conversation,
                    HandoffDetector.CustomerAsksForHuman(content) ? HandoffOrigin.Customer : HandoffOrigin.AI, ct);
        }

        await NotifyConversationAsync(conversation, ct);
        return new WebChatSendResult(MessageMapping.ToPublic(userMessage), reply is null ? null : MessageMapping.ToPublic(reply));
    }

    public async Task<IReadOnlyList<PublicMessageDto>> MessagesAsync(
        string token, Guid sessionId, string visitorKey, DateTime? after, string? origin, CancellationToken ct)
    {
        var conversationId = await AuthorizeVisitorAsync(token, sessionId, visitorKey, origin, ct);
        return await PublicMessagesAsync(conversationId, after, ct);
    }

    public async Task<Guid> AuthorizeVisitorAsync(string token, Guid sessionId, string visitorKey, string? origin, CancellationToken ct)
    {
        var ctx = await ResolveChannelAsync(token, origin, ct);
        var conversation = await FindConversationAsync(ctx, sessionId, visitorKey, ct) ?? throw AppException.NotFound("Conversa");
        return conversation.Id;
    }

    private async Task<(ConversationMessage Message, bool HandoffRequested)> GenerateReplyAsync(
        ChannelContext ctx, Conversation conversation, IReadOnlyList<ConversationMessage> history, string content, CancellationToken ct)
    {
        var simulate = !provider.IsConfigured;
        // Sem resposta automática possível: pedido explícito de atendente ainda vai para a fila humana.
        if (simulate && !ctx.Settings.AllowSimulatedReplies)
            return (NewMessage(conversation, SenderType.System, OfflineOf(ctx)), HandoffDetector.CustomerAsksForHuman(content));

        try
        {
            var reply = await orchestrator.GenerateReplyAsync(
                new AgentReplyRequest(ctx.Agent, history, content, ChannelType.WebChat, conversation.Id, simulate), ct);
            var message = NewMessage(conversation, SenderType.AI, reply.Content);
            message.InputTokens = reply.InputTokens;
            message.OutputTokens = reply.OutputTokens;
            message.ResponseTimeMs = reply.ResponseTimeMs;
            message.IsSimulated = reply.IsSimulated;
            message.KnowledgeSources = MessageMapping.SerializeSources(reply.Sources);
            return (message, reply.HandoffRequested);
        }
        catch (AppException ex)
        {
            // O visitante nunca vê detalhes internos (cota, chave, limites): recebe a mensagem de ausência
            // e a conversa vai para a fila humana, para não ficar sem resposta.
            logger.LogWarning("Web Chat sem resposta automática na conversa {ConversationId}: {Code}", conversation.Id, ex.Code);
            return (NewMessage(conversation, SenderType.System, OfflineOf(ctx)), true);
        }
    }

    private async Task<ChannelContext> ResolveChannelAsync(string token, string? origin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) throw InvalidChannel();
        var channel = await db.ChannelConfigurations.Include(c => c.Agent)
            .FirstOrDefaultAsync(c => c.PublicToken == token && c.ChannelType == ChannelType.WebChat, ct);
        if (channel?.Agent is null || channel.Status != ChannelStatus.Active || channel.Agent.Status != AgentStatus.Active)
            throw InvalidChannel();

        var companyActive = await db.Companies.AnyAsync(c => c.Id == channel.CompanyId && c.Status == CompanyStatus.Active, ct);
        if (!companyActive) throw InvalidChannel();

        var allowed = ChannelOrigins.Parse(channel.AllowedOrigins);
        if (allowed.Count > 0 && !allowed.Contains(ChannelOrigins.Normalize(origin) ?? "\0"))
            throw new AppException("Este site não está autorizado a usar este chat.", 403, "origin_not_allowed");

        return new ChannelContext(channel, channel.Agent, ChannelService.ParseSettings(channel.SettingsJson));
    }

    /// <summary>Mesma resposta para token inexistente ou canal inativo: não revela quais tokens existem.</summary>
    private static AppException InvalidChannel() => new("Chat indisponível no momento.", 404, "channel_unavailable");

    private async Task<Conversation?> FindConversationAsync(ChannelContext ctx, Guid sessionId, string visitorKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(visitorKey) || visitorKey.Length > 100) return null;
        var hash = KeyPrefix + Hash(visitorKey);
        return await db.Conversations.FirstOrDefaultAsync(c =>
            c.Id == sessionId && c.CompanyId == ctx.Channel.CompanyId && c.AgentId == ctx.Agent.Id
            && c.Channel == ChannelType.WebChat && c.ExternalSessionId == hash, ct);
    }

    private async Task<IReadOnlyList<PublicMessageDto>> PublicMessagesAsync(Guid conversationId, DateTime? after, CancellationToken ct)
    {
        var query = db.ConversationMessages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        if (after.HasValue)
        {
            var since = DateTime.SpecifyKind(after.Value, DateTimeKind.Utc);
            query = query.Where(m => m.CreatedAt > since);
        }
        var messages = await query.OrderBy(m => m.CreatedAt).Take(500).ToListAsync(ct);
        return messages.Select(MessageMapping.ToPublic).ToList();
    }

    private async Task NotifyConversationAsync(Conversation conversation, CancellationToken ct)
    {
        var summary = await ConversationService.ProjectSummaries(db, db.Conversations.AsNoTracking().Where(c => c.Id == conversation.Id))
            .FirstAsync(ct);
        await realtime.ConversationUpdatedAsync(conversation.CompanyId, summary, ct);
    }

    private static ConversationMessage NewMessage(Conversation conversation, SenderType sender, string content) => new()
    {
        CompanyId = conversation.CompanyId,
        ConversationId = conversation.Id,
        SenderType = sender,
        Content = content
    };

    private static string? WelcomeOf(ChannelContext ctx) => ctx.Settings.WelcomeMessage ?? ctx.Agent.WelcomeMessage;

    private static string OfflineOf(ChannelContext ctx) => ctx.Settings.OfflineMessage ?? (ctx.Agent.ResponseLanguage == "en"
        ? "We received your message! An attendant will reply shortly."
        : "Recebemos sua mensagem! Um atendente responderá em breve.");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
