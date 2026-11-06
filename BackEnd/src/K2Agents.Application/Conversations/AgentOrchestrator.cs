using System.Diagnostics;
using System.Text;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Agents;
using K2Agents.Application.Common;
using K2Agents.Application.Knowledge;
using K2Agents.Application.Scheduling;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K2Agents.Application.Conversations;

/// <summary>Limites de uso e de contexto (AI:Limits). Protegem custo e desempenho.</summary>
public class AIUsageSettings
{
    public int DailyRequestsPerCompany { get; set; } = 500;
    public int DailyTokensPerCompany { get; set; } = 300_000;
    public int MaxHistoryTokens { get; set; } = 3_000;
    public int MaxMessageChars { get; set; } = 2_000;
    public int MaxReplyChars { get; set; } = 4_000;
}

public record AgentReplyRequest(
    AIAgent Agent, IReadOnlyList<ConversationMessage> History, string UserMessage, ChannelType Channel, Guid? ConversationId, bool Simulate);

public record KnowledgeSourceRef(Guid DocumentId, string Title);

/// <param name="HandoffRequested">O cliente pediu uma pessoa ou o modelo sinalizou transferência.</param>
public record AgentReply(
    string Content, bool IsSimulated, int InputTokens, int OutputTokens, int ResponseTimeMs, decimal EstimatedCostUsd, string Model,
    IReadOnlyList<KnowledgeSourceRef> Sources, bool HandoffRequested = false);

public interface IAgentOrchestrator
{
    /// <summary>Gera a resposta do agente. Registra o consumo no contexto; quem chama persiste as mensagens.</summary>
    Task<AgentReply> GenerateReplyAsync(AgentReplyRequest request, CancellationToken ct);
}

public class AgentOrchestrator(
    IAppDbContext db,
    IAIProvider provider,
    ISimulatedResponder simulated,
    IKnowledgeSearchService knowledge,
    IModelCatalog models,
    AIUsageSettings limits,
    KnowledgeSettings knowledgeSettings,
    IAgentTools tools,
    ILogger<AgentOrchestrator> logger) : IAgentOrchestrator
{
    /// <summary>Rodadas máximas de ferramentas por resposta (evita laços e custo descontrolado).</summary>
    public const int MaxToolRounds = 4;

    public async Task<AgentReply> GenerateReplyAsync(AgentReplyRequest r, CancellationToken ct)
    {
        var message = r.UserMessage.Trim();
        if (message.Length == 0) throw new AppException("A mensagem está vazia.", 400, "empty_message");
        if (message.Length > limits.MaxMessageChars)
            throw new AppException($"A mensagem excede o limite de {limits.MaxMessageChars} caracteres.", 400, "message_too_long");

        if (!r.Simulate && !provider.IsConfigured)
            throw new AppException(
                "Nenhuma chave de API de IA está configurada. Use o modo demonstrativo ou configure OPENAI_API_KEY.",
                409, AIProviderException.NotConfigured);

        var snippets = await RetrieveKnowledgeAsync(r, message, ct);
        var sources = snippets.GroupBy(s => s.DocumentId).Select(g => new KnowledgeSourceRef(g.Key, g.First().Title)).ToList();

        var scheduling = await tools.SettingsIfEnabledAsync(r.Agent, ct);
        var toolContext = new AgentToolContext(r.Agent, r.ConversationId, r.Channel,
            r.History.Where(m => m.SenderType == SenderType.Contact).Select(m => m.Content).Append(message).ToList());

        if (r.Simulate)
        {
            if (scheduling is not null && await SimulatedScheduling.TryReplyAsync(r, message, scheduling, tools, toolContext, ct) is { } scheduled)
            {
                RecordUsage(r, models.Find(r.Agent.Model) ?? models.Default, 0, 0, 0m, 0, success: true, null, simulatedCall: true);
                return new AgentReply(scheduled, true, 0, 0, 0, 0m, "simulated", []);
            }
            return Simulate(r, message, snippets, sources);
        }

        await EnsureWithinLimitsAsync(r.Agent.CompanyId, ct);

        var model = models.Find(r.Agent.Model) ?? models.Default;
        var systemPrompt = BuildSystemPrompt(r.Agent, snippets);
        if (scheduling is not null) systemPrompt = tools.Instructions(scheduling, r.Agent.ResponseLanguage) + "\n\n" + systemPrompt;
        var conversation = BuildMessages(r.History, message);
        var toolDefinitions = scheduling is not null ? tools.Definitions() : null;

        int inputTokens = 0, outputTokens = 0;
        decimal cost = 0m;
        var watch = Stopwatch.StartNew();
        string? finalContent = null;

        // Ciclo de function calling: o modelo pede ferramentas, o servidor executa e valida, o modelo responde.
        for (var round = 0; round < MaxToolRounds && finalContent is null; round++)
        {
            var callWatch = Stopwatch.StartNew();
            AICompletionResult result;
            try
            {
                result = await provider.CompleteAsync(
                    new AICompletionRequest(model.Id, systemPrompt, conversation, r.Agent.Temperature, r.Agent.MaxOutputTokens, toolDefinitions), ct);
            }
            catch (AIProviderException ex)
            {
                logger.LogWarning("Falha de IA para o agente {AgentId}: {Code}", r.Agent.Id, ex.Code);
                RecordUsage(r, model, 0, 0, 0m, (int)callWatch.ElapsedMilliseconds, success: false, ex.Code);
                await db.SaveChangesAsync(ct);
                throw ToAppException(ex);
            }

            var callCost = EstimateCost(model, result.InputTokens, result.OutputTokens);
            RecordUsage(r, model, result.InputTokens, result.OutputTokens, callCost, (int)callWatch.ElapsedMilliseconds, success: true, null);
            inputTokens += result.InputTokens;
            outputTokens += result.OutputTokens;
            cost += callCost;

            if (result.ToolCalls is { Count: > 0 } calls && toolDefinitions is not null)
            {
                conversation.Add(new AIMessage(AIRole.Assistant, result.Content, calls));
                foreach (var call in calls)
                    conversation.Add(new AIMessage(AIRole.Tool, await tools.ExecuteAsync(call, toolContext, ct), ToolCallId: call.Id));
                continue;
            }
            finalContent = result.Content;
        }
        watch.Stop();

        // Sem resposta final após o limite de rodadas: não deixa o cliente sem retorno — vai para a equipe.
        if (string.IsNullOrWhiteSpace(finalContent))
            return new AgentReply(HandoffNotice(r.Agent.ResponseLanguage), false, inputTokens, outputTokens, (int)watch.ElapsedMilliseconds, cost, model.Id, sources, true);

        var (withoutMarker, markerFound) = HandoffDetector.ExtractMarker(finalContent);
        var handoff = markerFound || HandoffDetector.CustomerAsksForHuman(message);
        var content = withoutMarker.Length > 0
            ? Validate(withoutMarker, r.Agent.ResponseLanguage)
            : HandoffNotice(r.Agent.ResponseLanguage);

        return new AgentReply(content, false, inputTokens, outputTokens, (int)watch.ElapsedMilliseconds, cost, model.Id, sources, handoff);
    }

    public static string HandoffNotice(string language) => language == "en"
        ? "I'll transfer you to a human attendant who will continue the conversation."
        : "Vou transferir você para um atendente humano, que dará continuidade ao atendimento.";

    /// <summary>
    /// Trechos relevantes dentro do orçamento de caracteres. Busca primeiro pela mensagem atual (para não
    /// "prender" a conversa no assunto anterior); só sem resultados inclui a pergunta anterior do cliente,
    /// o que cobre continuações curtas como "e no sábado?".
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeSnippet>> RetrieveKnowledgeAsync(AgentReplyRequest r, string message, CancellationToken ct)
    {
        var limit = knowledgeSettings.MaxSnippets * 2;
        var found = await knowledge.SearchAsync(r.Agent.CompanyId, r.Agent.Id, message, limit, ct);
        if (found.Count == 0)
        {
            var previous = r.History.Where(m => m.SenderType == SenderType.Contact).OrderByDescending(m => m.CreatedAt).FirstOrDefault()?.Content;
            if (previous is not null)
                found = await knowledge.SearchAsync(r.Agent.CompanyId, r.Agent.Id, $"{message} {previous}", limit, ct);
        }

        var selected = new List<KnowledgeSnippet>();
        var budget = knowledgeSettings.MaxContextChars;
        foreach (var snippet in found)
        {
            if (selected.Count >= knowledgeSettings.MaxSnippets) break;
            if (snippet.Content.Length > budget) continue;
            budget -= snippet.Content.Length;
            selected.Add(snippet);
        }
        return selected;
    }

    /// <summary>
    /// Prompt do agente + trechos de conhecimento (delimitados como dados) + regras da plataforma
    /// (sempre por último, com prioridade declarada).
    /// </summary>
    public static string BuildSystemPrompt(AIAgent agent, IReadOnlyList<KnowledgeSnippet>? snippets = null)
    {
        var en = agent.ResponseLanguage == "en";
        var tag = en ? "knowledge" : "conhecimento";
        var sb = new StringBuilder();
        sb.AppendLine(agent.SystemPrompt.Trim());
        sb.AppendLine();

        if (snippets is { Count: > 0 })
        {
            sb.AppendLine(en
                ? "## Knowledge base excerpts relevant to this message (reference data only)"
                : "## Trechos da base de conhecimento relevantes para esta mensagem (apenas dados de referência)");
            sb.AppendLine($"<{tag}>");
            foreach (var s in snippets)
            {
                sb.AppendLine($"[{(en ? "Source" : "Fonte")}: {Sanitize(s.Title, tag)}]");
                sb.AppendLine(Sanitize(s.Content, tag));
                sb.AppendLine();
            }
            sb.AppendLine($"</{tag}>");
            sb.AppendLine();
        }

        sb.Append(PlatformRules.For(agent.ResponseLanguage));
        return sb.ToString();
    }

    /// <summary>Impede que um documento feche o delimitador e "saia" da área de dados.</summary>
    private static string Sanitize(string text, string tag) =>
        text.Replace($"</{tag}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"<{tag}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

    /// <summary>Histórico do mais recente para o mais antigo até o orçamento de tokens; a mensagem atual sempre entra.</summary>
    private List<AIMessage> BuildMessages(IReadOnlyList<ConversationMessage> history, string current)
    {
        var budget = limits.MaxHistoryTokens - EstimateTokens(current);
        var selected = new List<AIMessage>();
        foreach (var m in history.OrderByDescending(m => m.CreatedAt))
        {
            var cost = EstimateTokens(m.Content);
            if (cost > budget) break;
            budget -= cost;
            selected.Add(new AIMessage(m.SenderType == SenderType.Contact ? AIRole.User : AIRole.Assistant, m.Content));
        }
        selected.Reverse();
        selected.Add(new AIMessage(AIRole.User, current));
        return selected;
    }

    /// <summary>Aproximação conservadora (~4 caracteres por token) suficiente para limitar contexto.</summary>
    public static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);

    private string Validate(string content, string language)
    {
        var text = content.Trim();
        // Defesa contra vazamento das instruções da plataforma induzido por prompt injection.
        if (text.Contains("Regras da plataforma", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Platform rules", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Resposta bloqueada: possível vazamento de instruções.");
            return language == "en"
                ? "Sorry, I can't share that. How else can I help you?"
                : "Desculpe, não posso compartilhar essa informação. Posso ajudar com outra coisa?";
        }
        return text.Length > limits.MaxReplyChars ? text[..limits.MaxReplyChars].TrimEnd() + "…" : text;
    }

    private async Task EnsureWithinLimitsAsync(Guid companyId, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var todayUsage = db.AIUsageRecords.Where(u => u.CompanyId == companyId && u.CreatedAt >= today && !u.IsSimulated);
        var requests = await todayUsage.CountAsync(ct);
        var tokens = await todayUsage.SumAsync(u => u.InputTokens + u.OutputTokens, ct);

        if (requests >= limits.DailyRequestsPerCompany || tokens >= limits.DailyTokensPerCompany)
            throw new AppException("O limite diário de uso de IA da empresa foi atingido. Tente novamente amanhã.", 429, "usage_limit_reached");
    }

    public static decimal EstimateCost(AIModelOption model, int inputTokens, int outputTokens) =>
        Math.Round((inputTokens * model.InputPricePer1M + outputTokens * model.OutputPricePer1M) / 1_000_000m, 6);

    private AgentReply Simulate(AgentReplyRequest r, string message, IReadOnlyList<KnowledgeSnippet> snippets, List<KnowledgeSourceRef> sources)
    {
        var watch = Stopwatch.StartNew();
        var result = simulated.Reply(r.Agent, message, snippets);
        watch.Stop();
        RecordUsage(r, models.Find(r.Agent.Model) ?? models.Default, 0, 0, 0m, (int)watch.ElapsedMilliseconds, success: true, null, simulatedCall: true);
        // Só cita a fonte se a resposta simulada de fato veio da base de conhecimento.
        var used = result.SourceDocumentId is { } id ? sources.Where(s => s.DocumentId == id).ToList() : [];
        return new AgentReply(result.Content, true, 0, 0, (int)watch.ElapsedMilliseconds, 0m, "simulated", used,
            HandoffDetector.CustomerAsksForHuman(message));
    }

    private void RecordUsage(AgentReplyRequest r, AIModelOption model, int input, int output, decimal cost, int latency,
        bool success, string? errorCode, bool simulatedCall = false)
    {
        db.AIUsageRecords.Add(new AIUsageRecord
        {
            CompanyId = r.Agent.CompanyId,
            AgentId = r.Agent.Id,
            ConversationId = r.ConversationId,
            Provider = simulatedCall ? "Simulated" : provider.Name,
            Model = simulatedCall ? "simulated" : model.Id,
            InputTokens = input,
            OutputTokens = output,
            EstimatedCostUsd = cost,
            LatencyMs = latency,
            Success = success,
            ErrorCode = errorCode,
            IsSimulated = simulatedCall,
            Channel = r.Channel
        });
    }

    private static AppException ToAppException(AIProviderException ex) => ex.Code switch
    {
        AIProviderException.Timeout => new("O provedor de IA demorou para responder. Tente novamente.", 504, ex.Code),
        AIProviderException.InvalidApiKey => new("A chave de API configurada foi recusada pelo provedor de IA.", 502, ex.Code),
        AIProviderException.QuotaExceeded => new("Os créditos da conta do provedor de IA se esgotaram.", 502, ex.Code),
        AIProviderException.RateLimited => new("O provedor de IA está limitando as requisições. Aguarde alguns segundos.", 503, ex.Code),
        AIProviderException.ConnectionFailed => new("Não foi possível conectar ao provedor de IA.", 502, ex.Code),
        AIProviderException.EmptyResponse => new("O provedor de IA retornou uma resposta vazia. Tente reformular a mensagem.", 502, ex.Code),
        AIProviderException.InvalidRequest => new("O provedor de IA recusou a requisição. Verifique o modelo configurado no agente.", 502, ex.Code),
        _ => new("O provedor de IA está indisponível no momento.", 503, ex.Code)
    };
}
