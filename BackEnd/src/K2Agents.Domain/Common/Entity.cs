namespace K2Agents.Domain.Common;

/// <summary>Base de todas as entidades persistidas: chave Guid e data de criação em UTC.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Entidade pertencente a uma empresa (tenant). O DbContext aplica filtro global por CompanyId,
/// impedindo que um usuário de uma empresa leia dados de outra.
/// </summary>
public interface ITenantEntity
{
    Guid CompanyId { get; set; }
}
