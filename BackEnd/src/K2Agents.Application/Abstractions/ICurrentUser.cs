using K2Agents.Domain.Enums;

namespace K2Agents.Application.Abstractions;

/// <summary>Usuário da requisição atual, extraído do JWT.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? CompanyId { get; }
    UserRole? Role { get; }
    bool IsSuperAdmin { get; }
    string? IpAddress { get; }
}
