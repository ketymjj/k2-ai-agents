using K2Agents.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace K2Agents.Infrastructure.AI;

/// <summary>
/// Modelos habilitados, lidos de AI:Models. Os preços são referências para ESTIMAR custos e devem ser
/// conferidos com a tabela vigente do provedor; ajustá-los não exige recompilar.
/// </summary>
public class ModelCatalog : IModelCatalog
{
    private static readonly AIModelOption[] Fallback =
    [
        new("gpt-4o-mini", "GPT-4o mini", "OpenAI", 0.15m, 0.60m, true),
        new("gpt-4.1-mini", "GPT-4.1 mini", "OpenAI", 0.40m, 1.60m, false),
        new("gpt-4o", "GPT-4o", "OpenAI", 2.50m, 10.00m, false),
        new("gpt-4.1", "GPT-4.1", "OpenAI", 2.00m, 8.00m, false)
    ];

    public ModelCatalog(IConfiguration configuration)
    {
        var configured = configuration.GetSection("AI:Models").Get<List<AIModelOption>>();
        All = configured is { Count: > 0 } ? configured : Fallback;
        var defaultId = configuration["AI:OpenAI:DefaultModel"];
        Default = All.FirstOrDefault(m => m.Id == defaultId) ?? All.FirstOrDefault(m => m.IsDefault) ?? All[0];
    }

    public IReadOnlyList<AIModelOption> All { get; }
    public AIModelOption Default { get; }
    public AIModelOption? Find(string? id) => All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
}
