using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Application.Conversations;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static K2Agents.Application.Conversations.MessageMapping;

namespace K2Agents.Application.Playground;

public record SessionTotals(int Requests, int InputTokens, int OutputTokens, decimal EstimatedCostUsd, int SimulatedReplies);

public record PlaygroundSessionDto(
    Guid Id, Guid AgentId, string AgentName, ConversationStatus Status, DateTime StartedAt, IReadOnlyList<MessageDto> Messages, SessionTotals Totals);

public record PlaygroundSessionSummary(Guid Id, DateTime StartedAt, DateTime LastMessageAt, int MessageCount, ConversationStatus Status);

public record StartSessionRequest(Guid AgentId);

public record SendMessageRequest(string Content, bool Simulate);

/// <param name="HandoffRequested">No Playground a conversa não muda de status; o sinal só informa que, num canal real, iria para a fila humana.</param>
public record SendMessageResult(MessageDto UserMessage, MessageDto Reply, decimal EstimatedCostUsd, string Model, SessionTotals Totals, bool HandoffRequested);

public record PlaygroundStatusDto(
    bool AiConfigured, string Provider, int DailyRequestLimit, int DailyTokenLimit, int RequestsToday, int TokensToday, int MaxMessageChars);

public interface IPlaygroundService
{
    Task<PlaygroundStatusDto> StatusAsync(Guid? agentId, CancellationToken ct);
    Task<PlaygroundSessionDto> StartAsync(StartSessionRequest request, CancellationToken ct);
    Task<PlaygroundSessionDto> GetAsync(Guid sessionId, CancellationToken ct);
    Task<IReadOnlyList<PlaygroundSessionSummary>> ListAsync(Guid agentId, CancellationToken ct);
    Task<SendMessageResult> SendAsync(Guid sessionId, SendMessageRequest request, CancellationToken ct);
    Task CloseAsync(Guid sessionId, CancellationToken ct);
}

/// <summary>
/// Conversa de teste com o agente antes da publicação. Funciona com agentes em rascunho.
/// Cada sessão é uma Conversation do canal Playground (isolada por empresa pelo filtro global).
/// </summary>
public class PlaygroundService(
    IAppDbContext db, IAgentOrchestrator orchestrator, IAIProvider provider, AIUsageSettings limits, IAuditService audit) : IPlaygroundService
{
    public async Task<PlaygroundStatusDto> StatusAsync(Guid? agentId, CancellationToken ct)
    {
        int requests = 0, tokens = 0;
        if (agentId.HasValue)
        {
            var companyId = await db.AIAgents.Where(a => a.Id == agentId).Select(a => (Guid?)a.CompanyId).FirstOrDefaultAsync(ct)
                            ?? throw AppException.NotFound("Agente");
            var today = DateTime.UtcNow.Date;
            var usage = db.AIUsageRecords.Where(u => u.CompanyId == companyId && u.CreatedAt >= today && !u.IsSimulated);
            requests = await usage.CountAsync(ct);
            tokens = await usage.SumAsync(u => u.InputTokens + u.OutputTokens, ct);
        }
        return new PlaygroundStatusDto(provider.IsConfigured, provider.Name, limits.DailyRequestsPerCompany, limits.DailyTokensPerCompany,
            requests, tokens, limits.MaxMessageChars);
    }

    public async Task<PlaygroundSessionDto> StartAsync(StartSessionRequest request, CancellationToken ct)
    {
        var agent = await db.AIAgents.FirstOrDefaultAsync(a => a.Id == request.AgentId, ct) ?? throw AppException.NotFound("Agente");

        var conversation = new Conversation
        {
            CompanyId = agent.CompanyId,
            AgentId = agent.Id,
            Channel = ChannelType.Playground,
            Status = ConversationStatus.Open
        };
        db.Conversations.Add(conversation);
        if (!string.IsNullOrWhiteSpace(agent.WelcomeMessage))
        {
            db.ConversationMessages.Add(new ConversationMessage
            {
                CompanyId = agent.CompanyId,
                ConversationId = conversation.Id,
                SenderType = SenderType.System,
                Content = agent.WelcomeMessage
            });
        }
        audit.Record("playground.session_started", nameof(Conversation), conversation.Id.ToString(), agent.Name, agent.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(conversation.Id, ct);
    }

    public async Task<PlaygroundSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == sessionId && c.Channel == ChannelType.Playground)
            .Select(c => new { c.Id, c.AgentId, AgentName = c.Agent!.Name, c.Status, c.StartedAt })
            .FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("Sessão");

        var messages = (await db.ConversationMessages.AsNoTracking()
            .Where(m => m.ConversationId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct)).Select(ToDto).ToList();

        return new PlaygroundSessionDto(session.Id, session.AgentId, session.AgentName, session.Status, session.StartedAt, messages,
            await TotalsAsync(sessionId, ct));
    }

    public async Task<IReadOnlyList<PlaygroundSessionSummary>> ListAsync(Guid agentId, CancellationToken ct) =>
        await db.Conversations.AsNoTracking()
            .Where(c => c.AgentId == agentId && c.Channel == ChannelType.Playground)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(20)
            .Select(c => new PlaygroundSessionSummary(c.Id, c.StartedAt, c.LastMessageAt, c.Messages.Count, c.Status))
            .ToListAsync(ct);

    public async Task<SendMessageResult> SendAsync(Guid sessionId, SendMessageRequest request, CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Agent)
            .FirstOrDefaultAsync(c => c.Id == sessionId && c.Channel == ChannelType.Playground, ct)
            ?? throw AppException.NotFound("Sessão");
        if (conversation.Status == ConversationStatus.Closed)
            throw new AppException("Esta sessão foi encerrada. Inicie uma nova conversa.", 409, "session_closed");

        var history = await db.ConversationMessages.AsNoTracking()
            .Where(m => m.ConversationId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        // A IA é chamada antes de gravar: se falhar, nenhuma mensagem órfã fica na conversa.
        var reply = await orchestrator.GenerateReplyAsync(
            new AgentReplyRequest(conversation.Agent!, history, request.Content ?? string.Empty, ChannelType.Playground, conversation.Id, request.Simulate), ct);

        var now = DateTime.UtcNow;
        var userMessage = new ConversationMessage
        {
            CompanyId = conversation.CompanyId,
            ConversationId = conversation.Id,
            SenderType = SenderType.Contact,
            Content = request.Content!.Trim(),
            CreatedAt = now
        };
        var aiMessage = new ConversationMessage
        {
            CompanyId = conversation.CompanyId,
            ConversationId = conversation.Id,
            SenderType = SenderType.AI,
            Content = reply.Content,
            InputTokens = reply.InputTokens,
            OutputTokens = reply.OutputTokens,
            ResponseTimeMs = reply.ResponseTimeMs,
            IsSimulated = reply.IsSimulated,
            KnowledgeSources = SerializeSources(reply.Sources),
            CreatedAt = now.AddMilliseconds(1)
        };
        db.ConversationMessages.AddRange(userMessage, aiMessage);
        conversation.LastMessageAt = aiMessage.CreatedAt;
        await db.SaveChangesAsync(ct);

        return new SendMessageResult(ToDto(userMessage), ToDto(aiMessage), reply.EstimatedCostUsd, reply.Model, await TotalsAsync(sessionId, ct), reply.HandoffRequested);
    }

    public async Task CloseAsync(Guid sessionId, CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == sessionId && c.Channel == ChannelType.Playground, ct)
                           ?? throw AppException.NotFound("Sessão");
        if (conversation.Status == ConversationStatus.Closed) return;
        conversation.Status = ConversationStatus.Closed;
        conversation.ClosedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<SessionTotals> TotalsAsync(Guid sessionId, CancellationToken ct)
    {
        var usage = await db.AIUsageRecords.AsNoTracking().Where(u => u.ConversationId == sessionId && u.Success).ToListAsync(ct);
        return new SessionTotals(
            usage.Count(u => !u.IsSimulated),
            usage.Sum(u => u.InputTokens),
            usage.Sum(u => u.OutputTokens),
            usage.Sum(u => u.EstimatedCostUsd),
            usage.Count(u => u.IsSimulated));
    }
}
