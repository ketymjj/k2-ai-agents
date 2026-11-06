using System.Security.Claims;
using K2Agents.Application.Abstractions;
using K2Agents.Domain.Enums;
using K2Agents.Infrastructure.Security;

namespace K2Agents.Api.Infrastructure;

/// <summary>Lê o usuário autenticado a partir das claims do JWT da requisição.</summary>
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(K2ClaimTypes.Subject), out var id) ? id : null;

    public Guid? CompanyId => Guid.TryParse(Principal?.FindFirstValue(K2ClaimTypes.CompanyId), out var id) ? id : null;

    public UserRole? Role => Enum.TryParse<UserRole>(Principal?.FindFirstValue(K2ClaimTypes.Role), out var role) ? role : null;

    public bool IsSuperAdmin => Role == UserRole.SuperAdmin;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
