using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Users;

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct);
    Task<UserDto> GetAsync(Guid id, CancellationToken ct);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);
    Task<UserDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct);
    Task<IReadOnlyList<RoleDto>> RolesAsync(CancellationToken ct);
}

/// <summary>
/// Regras: SuperAdmin gerencia todos; CompanyAdmin gerencia apenas usuários da própria empresa
/// e nunca cria/promove SuperAdmin. Ninguém altera o próprio perfil nem se desativa.
/// </summary>
public class UserService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher hasher,
    IAuditService audit,
    IValidator<CreateUserRequest> createValidator,
    IValidator<UpdateUserRequest> updateValidator) : IUserService
{
    private IQueryable<User> Scoped()
    {
        var users = db.Users.AsNoTracking();
        if (currentUser.IsSuperAdmin) return users;
        if (currentUser.Role != UserRole.CompanyAdmin) throw AppException.Forbidden();
        return users.Where(u => u.CompanyId == currentUser.CompanyId);
    }

    private static IQueryable<UserDto> Project(IQueryable<User> query)
    {
        var now = DateTime.UtcNow;
        return query.Select(u => new UserDto(
            u.Id, u.Name, u.Email, u.RoleId, u.CompanyId, u.Company != null ? u.Company.Name : null,
            u.IsActive, u.PreferredLanguage, u.LastLoginAt, u.LockoutEndAt != null && u.LockoutEndAt > now, u.CreatedAt));
    }

    public async Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct)
    {
        var users = Scoped();
        if (query.CompanyId.HasValue) users = users.Where(u => u.CompanyId == query.CompanyId);
        if (query.Role.HasValue) users = users.Where(u => u.RoleId == query.Role);
        if (query.IsActive.HasValue) users = users.Where(u => u.IsActive == query.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            users = users.Where(u => u.Name.ToLower().Contains(term) || u.Email.Contains(term));
        }
        return await Project(users.OrderBy(u => u.Name)).ToPagedAsync(query, ct);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(Scoped().Where(u => u.Id == id)).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("Usuário");

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var companyId = await ResolveCompanyAsync(request.Role, request.CompanyId, ct);
        var email = ValidationRules.NormalizeEmail(request.Email);
        await EnsureEmailAvailableAsync(email, null, ct);

        var user = new User
        {
            Name = request.Name.Trim(),
            Email = email,
            PasswordHash = hasher.Hash(request.Password),
            RoleId = request.Role,
            CompanyId = companyId,
            PreferredLanguage = request.PreferredLanguage ?? "pt-BR"
        };
        db.Users.Add(user);
        audit.Record("user.created", nameof(User), user.Id.ToString(), $"{email} ({request.Role})", companyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(user.Id, ct);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var user = await LoadManageableAsync(id, ct);

        if (request.Role != user.RoleId)
        {
            if (user.Id == currentUser.UserId) throw AppException.Forbidden("Você não pode alterar o próprio perfil.");
            // Mudar entre perfil de plataforma e perfil de empresa exige trocar o vínculo; não permitido aqui.
            if ((request.Role == UserRole.SuperAdmin) != (user.RoleId == UserRole.SuperAdmin))
                throw new AppException("Não é possível converter entre perfil de plataforma e perfil de empresa.", 400, "invalid_role");
            audit.Record("user.role_changed", nameof(User), user.Id.ToString(), $"{user.RoleId} -> {request.Role}", user.CompanyId);
            user.RoleId = request.Role;
        }

        var email = ValidationRules.NormalizeEmail(request.Email);
        if (email != user.Email)
        {
            await EnsureEmailAvailableAsync(email, user.Id, ct);
            user.Email = email;
        }

        user.Name = request.Name.Trim();
        if (request.PreferredLanguage is not null) user.PreferredLanguage = request.PreferredLanguage;

        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = hasher.Hash(request.Password);
            user.FailedLoginCount = 0;
            user.LockoutEndAt = null;
            await RevokeSessionsAsync(user.Id, ct);
            audit.Record("user.password_reset", nameof(User), user.Id.ToString(), companyId: user.CompanyId);
        }

        audit.Record("user.updated", nameof(User), user.Id.ToString(), companyId: user.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<UserDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        if (id == currentUser.UserId) throw AppException.Forbidden("Você não pode desativar o próprio usuário.");
        var user = await LoadManageableAsync(id, ct);
        if (user.IsActive == isActive) return await GetAsync(id, ct);

        user.IsActive = isActive;
        if (!isActive) await RevokeSessionsAsync(user.Id, ct);
        audit.Record(isActive ? "user.activated" : "user.deactivated", nameof(User), user.Id.ToString(), companyId: user.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<RoleDto>> RolesAsync(CancellationToken ct)
    {
        var roles = db.Roles.AsNoTracking().OrderBy(r => r.Id).AsQueryable();
        if (!currentUser.IsSuperAdmin) roles = roles.Where(r => r.Id != UserRole.SuperAdmin);
        return await roles.Select(r => new RoleDto(r.Id, r.Name, r.Description)).ToListAsync(ct);
    }

    private async Task<Guid?> ResolveCompanyAsync(UserRole role, Guid? requestedCompanyId, CancellationToken ct)
    {
        if (role == UserRole.SuperAdmin)
        {
            if (!currentUser.IsSuperAdmin) throw AppException.Forbidden("Apenas o Super Admin pode criar administradores da plataforma.");
            return null;
        }

        Guid companyId;
        if (currentUser.IsSuperAdmin)
            companyId = requestedCompanyId ?? throw new AppException("Informe a empresa do usuário.", 400, "company_required");
        else if (currentUser.Role == UserRole.CompanyAdmin)
        {
            if (requestedCompanyId.HasValue && requestedCompanyId != currentUser.CompanyId) throw AppException.Forbidden();
            companyId = currentUser.CompanyId!.Value;
        }
        else throw AppException.Forbidden();

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct) ?? throw AppException.NotFound("Empresa");
        if (company.Status != CompanyStatus.Active)
            throw new AppException("Não é possível adicionar usuários a uma empresa desativada.", 400, "company_inactive");
        return companyId;
    }

    private async Task<User> LoadManageableAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw AppException.NotFound("Usuário");
        if (currentUser.IsSuperAdmin) return user;
        if (currentUser.Role != UserRole.CompanyAdmin || user.CompanyId != currentUser.CompanyId)
            throw AppException.NotFound("Usuário"); // 404 em vez de 403: não revela existência em outra empresa.
        return user;
    }

    private async Task EnsureEmailAvailableAsync(string email, Guid? exceptUserId, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != exceptUserId, ct))
            throw AppException.Conflict("Já existe um usuário com este e-mail.");
    }

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
    }
}
