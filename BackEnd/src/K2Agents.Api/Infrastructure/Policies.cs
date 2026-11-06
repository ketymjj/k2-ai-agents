using K2Agents.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace K2Agents.Api.Infrastructure;

/// <summary>Políticas RBAC. Cada uma inclui os perfis acima dela na hierarquia.</summary>
public static class Policies
{
    public const string PlatformAdmin = nameof(PlatformAdmin);
    public const string CompanyAdmin = nameof(CompanyAdmin);
    public const string AgentManager = nameof(AgentManager);
    public const string Attendant = nameof(Attendant);
    public const string Reader = nameof(Reader);

    public static void Register(AuthorizationOptions options)
    {
        Add(options, PlatformAdmin, UserRole.SuperAdmin);
        Add(options, CompanyAdmin, UserRole.SuperAdmin, UserRole.CompanyAdmin);
        Add(options, AgentManager, UserRole.SuperAdmin, UserRole.CompanyAdmin, UserRole.AgentManager);
        Add(options, Attendant, UserRole.SuperAdmin, UserRole.CompanyAdmin, UserRole.AgentManager, UserRole.Attendant);
        Add(options, Reader, Enum.GetValues<UserRole>());
    }

    private static void Add(AuthorizationOptions options, string name, params UserRole[] roles) =>
        options.AddPolicy(name, p => p.RequireAuthenticatedUser().RequireRole(roles.Select(r => r.ToString())));
}
