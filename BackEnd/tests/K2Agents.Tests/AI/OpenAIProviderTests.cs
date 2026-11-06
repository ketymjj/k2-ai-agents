using System.Net;
using System.Text;
using System.Text.Json;
using K2Agents.Application.Abstractions;
using K2Agents.Infrastructure.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace K2Agents.Tests.AI;

/// <summary>Testa o adaptador OpenAI contra respostas HTTP simuladas — nenhuma chamada real é feita.</summary>
public class OpenAIProviderTests
{
    private const string SecretKey = "sk-test-SEGREDO-NAO-PODE-VAZAR";

    private sealed class StubHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request, Calls);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Success = """
        {"choices":[{"message":{"role":"assistant","content":"  Olá! Posso ajudar.  "},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":120,"completion_tokens":15}}
        """;

    private static (OpenAIProvider provider, StubHandler handler) Create(
        Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond, string apiKey = SecretKey, int timeoutSeconds = 30)
    {
        var handler = new StubHandler(respond);
        var options = Options.Create(new OpenAIOptions
        {
            ApiKey = apiKey, BaseUrl = "https://api.example.test/v1", TimeoutSeconds = timeoutSeconds, MaxRetries = 1
        });
        return (new OpenAIProvider(new HttpClient(handler), options, NullLogger<OpenAIProvider>.Instance), handler);
    }

    private static readonly AICompletionRequest Request = new(
        "gpt-4o-mini", "Você é a Ana.", [new AIMessage(AIRole.User, "Oi")], 0.3m, 200);

    [Fact]
    public async Task Success_ParsesContentAndUsage_AndSendsExpectedPayload()
    {
        var (provider, handler) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Success)));

        var result = await provider.CompleteAsync(Request, CancellationToken.None);

        Assert.Equal("Olá! Posso ajudar.", result.Content);
        Assert.Equal(120, result.InputTokens);
        Assert.Equal(15, result.OutputTokens);
        Assert.Equal("https://api.example.test/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("gpt-4o-mini", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(200, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"code":"invalid_api_key"}}""", AIProviderException.InvalidApiKey)]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":"insufficient_quota"}}""", AIProviderException.QuotaExceeded)]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":"model_not_found"}}""", AIProviderException.InvalidRequest)]
    public async Task NonTransientErrors_AreClassified_WithoutRetry(HttpStatusCode status, string body, string expectedCode)
    {
        var (provider, handler) = Create((_, _) => Task.FromResult(Json(status, body)));

        var ex = await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request, CancellationToken.None));

        Assert.Equal(expectedCode, ex.Code);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain(SecretKey, ex.ToString());
    }

    [Fact]
    public async Task ServerError_IsRetriedOnce_ThenSucceeds()
    {
        var (provider, handler) = Create((_, call) =>
            Task.FromResult(call == 1 ? Json(HttpStatusCode.ServiceUnavailable, "{}") : Json(HttpStatusCode.OK, Success)));

        var result = await provider.CompleteAsync(Request, CancellationToken.None);

        Assert.Equal(2, handler.Calls);
        Assert.Equal("Olá! Posso ajudar.", result.Content);
    }

    [Fact]
    public async Task PersistentServerError_FailsAsUnavailable()
    {
        var (provider, handler) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.BadGateway, "<html>bad gateway</html>")));

        var ex = await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request, CancellationToken.None));

        Assert.Equal(AIProviderException.Unavailable, ex.Code);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task RateLimit_IsTransient()
    {
        var (provider, handler) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"rate_limit_exceeded"}}""")));
        var ex = await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request, CancellationToken.None));
        Assert.Equal(AIProviderException.RateLimited, ex.Code);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Timeout_IsReportedAsTimeout()
    {
        var (provider, _) = Create(async (req, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            return Json(HttpStatusCode.OK, Success);
        }, timeoutSeconds: 1);

        var ex = await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request, CancellationToken.None));
        Assert.Equal(AIProviderException.Timeout, ex.Code);
    }

    [Fact]
    public async Task ConnectionFailure_IsReported()
    {
        var (provider, _) = Create((_, _) => throw new HttpRequestException("No such host"));
        var ex = await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request, CancellationToken.None));
        Assert.Equal(AIProviderException.ConnectionFailed, ex.Code);
    }

    [Fact]
    public async Task EmptyContent_IsRejected()
    {
        var (provider, _) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"   "}}],"usage":{"prompt_tokens":1,"completion_tokens":0}}""")));
        var ex = await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request, CancellationToken.None));
        Assert.Equal(AIProviderException.EmptyResponse, ex.Code);
    }

    [Fact]
    public async Task ToolCalls_AreParsed_EvenWithoutTextContent()
    {
        var (provider, _) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """
            {"choices":[{"message":{"role":"assistant","content":null,
              "tool_calls":[{"id":"call_1","type":"function","function":{"name":"check_availability","arguments":"{\"service\":\"Escova\",\"date\":\"2026-11-03\"}"}}]},
              "finish_reason":"tool_calls"}],"usage":{"prompt_tokens":50,"completion_tokens":12}}
            """)));

        var result = await provider.CompleteAsync(Request, CancellationToken.None);

        var call = Assert.Single(result.ToolCalls!);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("check_availability", call.Name);
        Assert.Contains("Escova", call.Arguments);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ToolDefinitionsAndToolMessages_AreSentInOpenAIFormat()
    {
        var (provider, handler) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Success)));
        var request = new AICompletionRequest("gpt-4o-mini", "sistema",
        [
            new AIMessage(AIRole.User, "Tem horário amanhã?"),
            new AIMessage(AIRole.Assistant, null, [new AIToolCall("call_9", "check_availability", "{\"service\":\"Escova\"}")]),
            new AIMessage(AIRole.Tool, "{\"ok\":true}", ToolCallId: "call_9")
        ], 0.3m, 200,
        [new AIToolDefinition("check_availability", "Consulta horários", new { type = "object", properties = new { } })]);

        await provider.CompleteAsync(request, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("function", root.GetProperty("tools")[0].GetProperty("type").GetString());
        Assert.Equal("check_availability", root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        var messages = root.GetProperty("messages");
        Assert.Equal("call_9", messages[2].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("tool", messages[3].GetProperty("role").GetString());
        Assert.Equal("call_9", messages[3].GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task WithoutKey_IsNotConfigured_AndNeverCallsTheProvider()
    {
        var (provider, handler) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Success)), apiKey: "");

        Assert.False(provider.IsConfigured);
        var ex = await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request, CancellationToken.None));
        Assert.Equal(AIProviderException.NotConfigured, ex.Code);
        Assert.Equal(0, handler.Calls);
    }
}
