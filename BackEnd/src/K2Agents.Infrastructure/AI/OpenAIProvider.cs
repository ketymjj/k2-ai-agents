using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using K2Agents.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace K2Agents.Infrastructure.AI;

public class OpenAIOptions
{
    public const string Section = "AI:OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string DefaultModel { get; set; } = "gpt-4o-mini";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 1;
}

/// <summary>
/// Chat Completions da OpenAI via HttpClient. A chave vai apenas no cabeçalho Authorization
/// e nunca é registrada em log nem incluída em mensagens de erro.
/// </summary>
public class OpenAIProvider(HttpClient http, IOptions<OpenAIOptions> options, ILogger<OpenAIProvider> logger) : IAIProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OpenAIOptions _options = options.Value;

    public string Name => "OpenAI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<AICompletionResult> CompleteAsync(AICompletionRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new AIProviderException(AIProviderException.NotConfigured, "Provedor de IA não configurado.");

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await SendAsync(request, ct);
            }
            catch (AIProviderException ex) when (ex.Transient && attempt < _options.MaxRetries)
            {
                logger.LogWarning("Falha transitória do provedor ({Code}); nova tentativa {Attempt}.", ex.Code, attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), ct);
            }
        }
    }

    private async Task<AICompletionResult> SendAsync(AICompletionRequest request, CancellationToken ct)
    {
        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        messages.AddRange(request.Messages.Select(ToWire));

        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_completion_tokens"] = request.MaxOutputTokens
        };
        if (request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.Parameters }
            }).ToList();
            body["tool_choice"] = "auto";
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureSlash(_options.BaseUrl)), "chat/completions"))
        {
            Content = JsonContent.Create(body, options: Json)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AIProviderException(AIProviderException.Timeout, "O provedor de IA não respondeu dentro do tempo limite.", transient: true);
        }
        catch (HttpRequestException ex)
        {
            throw new AIProviderException(AIProviderException.ConnectionFailed, "Falha de conexão com o provedor de IA.", transient: true, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw await MapErrorAsync(response, ct);

            var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(Json, ct);
            var choice = payload?.Choices?.FirstOrDefault();
            var content = choice?.Message?.Content?.Trim() ?? string.Empty;
            var toolCalls = choice?.Message?.ToolCalls?
                .Where(t => t.Function is not null)
                .Select(t => new AIToolCall(t.Id, t.Function!.Name, t.Function.Arguments ?? "{}"))
                .ToList();
            // Pedido de ferramenta sem texto é válido; resposta sem texto e sem ferramentas não é.
            if (content.Length == 0 && toolCalls is not { Count: > 0 })
                throw new AIProviderException(AIProviderException.EmptyResponse, "O provedor de IA retornou uma resposta vazia.");

            return new AICompletionResult(
                content,
                payload!.Usage?.PromptTokens ?? 0,
                payload.Usage?.CompletionTokens ?? 0,
                choice!.FinishReason,
                toolCalls is { Count: > 0 } ? toolCalls : null);
        }
    }

    private async Task<AIProviderException> MapErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? providerCode = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(Json, ct);
            providerCode = error?.Error?.Code ?? error?.Error?.Type;
        }
        catch (JsonException)
        {
            /* corpo não-JSON: classifica apenas pelo status */
        }

        // Registra apenas status e código do provedor: nunca cabeçalhos ou corpo da requisição.
        logger.LogWarning("Provedor de IA retornou {Status} ({ProviderCode}).", (int)response.StatusCode, providerCode ?? "-");

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new AIProviderException(AIProviderException.InvalidApiKey, "A chave de API foi recusada pelo provedor."),
            HttpStatusCode.TooManyRequests when providerCode == "insufficient_quota" =>
                new AIProviderException(AIProviderException.QuotaExceeded, "Os créditos da conta do provedor de IA se esgotaram."),
            HttpStatusCode.TooManyRequests =>
                new AIProviderException(AIProviderException.RateLimited, "O provedor de IA limitou as requisições.", transient: true),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity =>
                new AIProviderException(AIProviderException.InvalidRequest, $"O provedor recusou a requisição ({providerCode ?? "bad_request"})."),
            _ when (int)response.StatusCode >= 500 =>
                new AIProviderException(AIProviderException.Unavailable, "O provedor de IA está indisponível no momento.", transient: true),
            _ => new AIProviderException(AIProviderException.InvalidRequest, $"Resposta inesperada do provedor ({(int)response.StatusCode}).")
        };
    }

    private static string EnsureSlash(string url) => url.EndsWith('/') ? url : url + "/";

    /// <summary>Mensagem no formato da API de Chat Completions (inclui pedidos e resultados de ferramentas).</summary>
    private static object ToWire(AIMessage m) => m.Role switch
    {
        AIRole.User => new { role = "user", content = m.Content },
        AIRole.Tool => new { role = "tool", tool_call_id = m.ToolCallId, content = m.Content },
        _ when m.ToolCalls is { Count: > 0 } => new
        {
            role = "assistant",
            content = string.IsNullOrEmpty(m.Content) ? null : m.Content,
            tool_calls = m.ToolCalls.Select(t => new { id = t.Id, type = "function", function = new { name = t.Name, arguments = t.Arguments } })
        },
        _ => new { role = "assistant", content = m.Content }
    };

    private sealed record ChatCompletionResponse(List<Choice>? Choices, UsageInfo? Usage);
    private sealed record Choice(ChoiceMessage? Message, [property: JsonPropertyName("finish_reason")] string? FinishReason);
    private sealed record ChoiceMessage(string? Content, [property: JsonPropertyName("tool_calls")] List<ToolCallWire>? ToolCalls);
    private sealed record ToolCallWire(string Id, string? Type, FunctionWire? Function);
    private sealed record FunctionWire(string Name, string? Arguments);
    private sealed record UsageInfo(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
    private sealed record ErrorEnvelope(ErrorBody? Error);
    private sealed record ErrorBody(string? Code, string? Type, string? Message);
}
