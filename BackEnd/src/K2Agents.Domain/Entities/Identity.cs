using K2Agents.Domain.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Domain.Entities;

/// <summary>Perfil de acesso (tabela Roles). O Id coincide com o valor de <see cref="UserRole"/>.</summary>
public class Role
{
    public UserRole Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Usuário da plataforma. SuperAdmin não pertence a empresa (CompanyId nulo).</summary>
public class User : Entity
{
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole RoleId { get; set; }
    public Role? Role { get; set; }
    public bool IsActive { get; set; } = true;
    public string PreferredLanguage { get; set; } = "pt-BR";

    /// <summary>Modo Iniciante: exibe orientações contextuais. Opcional, desligado por padrão.</summary>
    public bool BeginnerMode { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndAt { get; set; }
}

/// <summary>Refresh token rotativo. Apenas o hash SHA-256 é persistido.</summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
