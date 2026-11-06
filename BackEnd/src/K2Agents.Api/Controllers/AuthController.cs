using K2Agents.Api.Infrastructure;
using K2Agents.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace K2Agents.Api.Controllers;

/// <summary>
/// Access token (curto) no corpo da resposta, guardado só em memória pelo frontend.
/// Refresh token em cookie HttpOnly + SameSite=Strict restrito ao caminho /api/v1/auth.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    public const string RefreshCookie = "k2_refresh";
    private const string CookiePath = "/api/v1/auth";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
        => Issue(await auth.LoginAsync(request, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Session)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        try
        {
            return Issue(await auth.RefreshAsync(Request.Cookies[RefreshCookie], ct));
        }
        catch
        {
            ClearCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        ClearCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public Task<AuthUserDto> Me(CancellationToken ct) => auth.MeAsync(ct);

    [HttpPut("me/preferences")]
    [Authorize]
    public Task<AuthUserDto> UpdatePreferences(UpdatePreferencesRequest request, CancellationToken ct)
        => auth.UpdatePreferencesAsync(request, ct);

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(request, ct);
        ClearCookie();
        return NoContent();
    }

    private ActionResult<AuthResponse> Issue(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = result.RefreshTokenExpiresAt
        });
        return new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User);
    }

    private void ClearCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = CookiePath, SameSite = SameSiteMode.Strict });
}
