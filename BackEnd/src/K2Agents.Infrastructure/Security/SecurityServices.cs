using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using K2Agents.Application.Abstractions;
using K2Agents.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using IPasswordHasher = K2Agents.Application.Abstractions.IPasswordHasher;

namespace K2Agents.Infrastructure.Security;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "k2-ai-agents";
    public string Audience { get; set; } = "k2-ai-agents-web";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public int RefreshReuseGraceSeconds { get; set; } = 30;
}

public static class K2ClaimTypes
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string CompanyId = "company_id";
    public const string Name = "name";
}

/// <summary>PBKDF2 (ASP.NET Core Identity v3): salt aleatório e iterações embutidos no hash.</summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string hash, string password)
    {
        try
        {
            return _inner.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);
    public TimeSpan RefreshReuseGrace => TimeSpan.FromSeconds(_options.RefreshReuseGraceSeconds);

    public AccessToken CreateAccessToken(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(K2ClaimTypes.Subject, user.Id.ToString()),
            new(K2ClaimTypes.Name, user.Name),
            new(K2ClaimTypes.Role, user.RoleId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (user.CompanyId.HasValue) claims.Add(new Claim(K2ClaimTypes.CompanyId, user.CompanyId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var token = new JwtSecurityToken(
            _options.Issuer, _options.Audience, claims,
            notBefore: DateTime.UtcNow, expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string GenerateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}

public class AuditService(IAppDbContext db, ICurrentUser currentUser) : IAuditService
{
    public void Record(string action, string entityType, string? entityId = null, string? details = null, Guid? companyId = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is { Length: > 4000 } ? details[..4000] : details,
            CompanyId = companyId ?? currentUser.CompanyId,
            UserId = currentUser.UserId,
            IpAddress = currentUser.IpAddress
        });
    }
}
