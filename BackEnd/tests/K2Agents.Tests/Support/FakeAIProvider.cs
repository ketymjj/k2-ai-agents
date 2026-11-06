using System.Collections.Concurrent;
using K2Agents.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace K2Agents.Tests.Support;

/// <summary>Provedor de IA simulado — usado apenas nos testes automatizados.</summary>
public class FakeAIProvider : IAIProvider
{
    private readonly ConcurrentQueue<Func<AICompletionRequest, AICompletionResult>> _next = new();

    public string Name => "Fake";
    public bool IsConfigured => true;
    /// <summary>Em ordem de chegada (ConcurrentBag não preserva ordem e quebraria Last()).</summary>
    public ConcurrentQueue<AICompletionRequest> Requests { get; } = new();

    /// <summary>Programa a próxima resposta (ou exceção).</summary>
    public void Enqueue(Func<AICompletionRequest, AICompletionResult> behavior) => _next.Enqueue(behavior);

    /// <summary>Descarta respostas programadas que não foram consumidas.</summary>
    public void Clear() => _next.Clear();

    public Task<AICompletionResult> CompleteAsync(AICompletionRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        var behavior = _next.TryDequeue(out var b) ? b : _ => new AICompletionResult("Resposta da IA de teste.", 100, 20, "stop");
        return Task.FromResult(behavior(request));
    }
}

/// <summary>API de teste com o provedor de IA simulado registrado.</summary>
public class AIEnabledFactory : K2ApiFactory
{
    public FakeAIProvider AI { get; } = new();
    protected virtual int DailyRequestLimit => 1000;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<IAIProvider>(AI);
        services.AddSingleton(new Application.Conversations.AIUsageSettings
        {
            DailyRequestsPerCompany = DailyRequestLimit,
            DailyTokensPerCompany = 1_000_000,
            MaxHistoryTokens = 300,
            MaxMessageChars = 2000,
            MaxReplyChars = 4000
        });
    }
}

public class LowLimitAIFactory : AIEnabledFactory
{
    protected override int DailyRequestLimit => 2;
}
