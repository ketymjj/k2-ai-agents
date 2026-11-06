using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Auth;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string? rawRefreshToken, CancellationToken ct);
    Task LogoutAsync(string? rawRefreshToken, CancellationToken ct);
    Task<AuthUserDto> MeAsync(CancellationToken ct);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct);
    Task<AuthUserDto> UpdatePreferencesAsync(UpdatePreferencesRequest request, CancellationToken ct);
}

public class AuthService(
    IAppDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    IAuditService audit,
    ICurrentUser currentUser,
    IValidator<LoginRequest> loginValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator,
    IValidator<UpdatePreferencesRequest> preferencesValidator) : IAuthService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static AppException InvalidCredentials() => new("E-mail ou senha inválidos.", 401, "invalid_credentials");
    private static AppException InvalidSession() => new("Sessão expirada. Faça login novamente.", 401, "invalid_refresh_token");

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await loginValidator.ValidateAndThrowAsync(request, ct);
        var email = ValidationRules.NormalizeEmail(request.Email);
        var user = await db.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            // Mesmo custo computacional de um usuário existente, dificultando enumeração de e-mails por tempo.
            hasher.Verify(DummyHash.Value(hasher), request.Password);
            throw InvalidCredentials();
        }

        if (user.LockoutEndAt > DateTime.UtcNow)
            throw new AppException("Conta temporariamente bloqueada por excesso de tentativas. Tente novamente mais tarde.", 401, "locked");

        if (!hasher.Verify(user.PasswordHash, request.Password))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEndAt = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginCount = 0;
                audit.Record("auth.locked", nameof(User), user.Id.ToString(), companyId: user.CompanyId);
            }
            audit.Record("auth.login_failed", nameof(User), user.Id.ToString(), companyId: user.CompanyId);
            await db.SaveChangesAsync(ct);
            throw InvalidCredentials();
        }

        EnsureCanSignIn(user);

        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        user.LastLoginAt = DateTime.UtcNow;
        audit.Record("auth.login", nameof(User), user.Id.ToString(), companyId: user.CompanyId);
        return await IssueAsync(user, ct);
    }

    public async Task<AuthResult> RefreshAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) throw InvalidSession();

        var hash = tokens.HashRefreshToken(rawRefreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User).ThenInclude(u => u!.Company)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored?.User is null) throw InvalidSession();

        if (stored.RevokedAt is not null)
        {
            // Revogado por logout/troca de senha: apenas recusa.
            if (stored.ReplacedByTokenHash is null) throw InvalidSession();

            // Rotacionado há poucos segundos: o cliente não chegou a receber o sucessor (reload duplo,
            // abas concorrentes, conexão interrompida). Emite um novo token sem tratar como roubo.
            if (DateTime.UtcNow - stored.RevokedAt.Value <= tokens.RefreshReuseGrace)
            {
                EnsureCanSignIn(stored.User);
                return await IssueAsync(stored.User, ct);
            }

            // Reutilização tardia de token rotacionado: possível roubo. Revoga todas as sessões do usuário.
            await RevokeAllAsync(stored.UserId, ct);
            audit.Record("auth.refresh_reuse_detected", nameof(User), stored.UserId.ToString(), companyId: stored.User.CompanyId);
            await db.SaveChangesAsync(ct);
            throw InvalidSession();
        }

        if (stored.ExpiresAt <= DateTime.UtcNow) throw InvalidSession();

        EnsureCanSignIn(stored.User);
        var result = await IssueAsync(stored.User, ct, rotated: stored);
        return result;
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) return;
        var hash = tokens.HashRefreshToken(rawRefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (stored is null) return;
        stored.RevokedAt = DateTime.UtcNow;
        audit.Record("auth.logout", nameof(User), stored.UserId.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuthUserDto> MeAsync(CancellationToken ct) => ToDto(await GetCurrentUserAsync(ct));

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        await changePasswordValidator.ValidateAndThrowAsync(request, ct);
        var user = await GetCurrentUserAsync(ct);
        if (!hasher.Verify(user.PasswordHash, request.CurrentPassword))
            throw new AppException("Senha atual incorreta.", 400, "invalid_password");

        user.PasswordHash = hasher.Hash(request.NewPassword);
        await RevokeAllAsync(user.Id, ct);
        audit.Record("auth.password_changed", nameof(User), user.Id.ToString(), companyId: user.CompanyId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuthUserDto> UpdatePreferencesAsync(UpdatePreferencesRequest request, CancellationToken ct)
    {
        await preferencesValidator.ValidateAndThrowAsync(request, ct);
        var user = await GetCurrentUserAsync(ct);
        if (request.PreferredLanguage is not null) user.PreferredLanguage = request.PreferredLanguage;
        if (request.BeginnerMode is { } beginner) user.BeginnerMode = beginner;
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    private async Task<User> GetCurrentUserAsync(CancellationToken ct)
    {
        var id = currentUser.UserId ?? throw new AppException("Não autenticado.", 401);
        return await db.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == id, ct)
               ?? throw new AppException("Não autenticado.", 401);
    }

    private static void EnsureCanSignIn(User user)
    {
        if (!user.IsActive) throw new AppException("Usuário desativado.", 401, "user_inactive");
        if (user.RoleId != UserRole.SuperAdmin && (user.Company is null || user.Company.Status != CompanyStatus.Active))
            throw new AppException("A empresa deste usuário está desativada.", 401, "company_inactive");
    }

    private async Task<AuthResult> IssueAsync(User user, CancellationToken ct, RefreshToken? rotated = null)
    {
        var access = tokens.CreateAccessToken(user);
        var raw = tokens.GenerateRefreshToken();
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokens.HashRefreshToken(raw),
            ExpiresAt = DateTime.UtcNow.Add(tokens.RefreshTokenLifetime),
            CreatedByIp = currentUser.IpAddress
        };
        if (rotated is not null)
        {
            rotated.RevokedAt = DateTime.UtcNow;
            rotated.ReplacedByTokenHash = refresh.TokenHash;
        }
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);
        return new AuthResult(access.Token, access.ExpiresAt, raw, refresh.ExpiresAt, ToDto(user));
    }

    private async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active) t.RevokedAt = DateTime.UtcNow;
    }

    public static AuthUserDto ToDto(User u) =>
        new(u.Id, u.Name, u.Email, u.RoleId, u.CompanyId, u.Company?.Name, u.PreferredLanguage, u.BeginnerMode);

    private static class DummyHash
    {
        private static string? _value;
        public static string Value(IPasswordHasher hasher) => _value ??= hasher.Hash(Guid.NewGuid().ToString());
    }
}
