using K2Agents.Domain.Entities;

namespace K2Agents.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public record AccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    /// <summary>Gera um refresh token aleatório; apenas o hash vai para o banco.</summary>
    string GenerateRefreshToken();
    string HashRefreshToken(string rawToken);
    TimeSpan RefreshTokenLifetime { get; }

    /// <summary>Janela em que um token recém-rotacionado ainda é aceito sem ser tratado como roubo.</summary>
    TimeSpan RefreshReuseGrace { get; }
}

/// <summary>Registra eventos de auditoria no mesmo SaveChanges da operação auditada.</summary>
public interface IAuditService
{
    void Record(string action, string entityType, string? entityId = null, string? details = null, Guid? companyId = null);
}
