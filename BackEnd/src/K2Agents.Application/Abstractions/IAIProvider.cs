namespace K2Agents.Application.Abstractions;

public enum AIRole { User, Assistant, Tool }

/// <summary>Chamada de ferramenta pedida pelo modelo (function calling). Arguments é JSON.</summary>
public record AIToolCall(string Id, string Name, string Arguments);

/// <summary>Ferramenta oferecida ao modelo. Parameters é um JSON Schema serializável.</summary>
public record AIToolDefinition(string Name, string Description, object Parameters);

/// <param name="ToolCalls">Mensagem do assistente que pediu ferramentas.</param>
/// <param name="ToolCallId">Mensagem com o resultado de uma ferramenta (Role = Tool).</param>
public record AIMessage(AIRole Role, string? Content, IReadOnlyList<AIToolCall>? ToolCalls = null, string? ToolCallId = null);

public record AICompletionRequest(
    string Model, string SystemPrompt, IReadOnlyList<AIMessage> Messages, decimal Temperature, int MaxOutputTokens,
    IReadOnlyList<AIToolDefinition>? Tools = null);

/// <param name="ToolCalls">Preenchido quando o modelo pede para executar ferramentas em vez de responder.</param>
public record AICompletionResult(string Content, int InputTokens, int OutputTokens, string? FinishReason, IReadOnlyList<AIToolCall>? ToolCalls = null);

/// <summary>Falha do provedor já classificada. A mensagem nunca contém a chave de API.</summary>
public class AIProviderException(string code, string message, bool transient = false, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public bool Transient { get; } = transient;

    public const string InvalidApiKey = "invalid_api_key";
    public const string QuotaExceeded = "quota_exceeded";
    public const string RateLimited = "provider_rate_limited";
    public const string Timeout = "timeout";
    public const string Unavailable = "provider_unavailable";
    public const string ConnectionFailed = "connection_failed";
    public const string EmptyResponse = "empty_response";
    public const string InvalidRequest = "invalid_request";
    public const string NotConfigured = "ai_not_configured";
}

/// <summary>Provedor de modelos de linguagem (OpenAI hoje; outros implementam a mesma interface).</summary>
public interface IAIProvider
{
    string Name { get; }

    /// <summary>Há credencial configurada. Não garante que ela seja válida.</summary>
    bool IsConfigured { get; }

    Task<AICompletionResult> CompleteAsync(AICompletionRequest request, CancellationToken ct);
}
