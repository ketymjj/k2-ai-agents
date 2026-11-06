namespace K2Agents.Application.Abstractions;

/// <summary>
/// Modelo de IA habilitado na plataforma. Preços por 1 milhão de tokens, em USD, usados apenas para
/// ESTIMAR custos — o valor faturado é o do painel do provedor.
/// </summary>
public record AIModelOption(string Id, string Label, string Provider, decimal InputPricePer1M, decimal OutputPricePer1M, bool IsDefault);

public interface IModelCatalog
{
    IReadOnlyList<AIModelOption> All { get; }
    AIModelOption? Find(string? id);
    AIModelOption Default { get; }
}
