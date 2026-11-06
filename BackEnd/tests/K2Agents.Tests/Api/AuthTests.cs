using System.Net;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Api;

public class AuthTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessTokenAndHttpOnlyRefreshCookie()
    {
        var response = await factory.CreateClient().LoginAsync(Demo.AdminEmail, Demo.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("accessToken").GetString()));
        Assert.Equal("SuperAdmin", json.GetProperty("user").GetProperty("role").GetString());
        Assert.False(json.TryGetProperty("refreshToken", out _));

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("k2_refresh=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_EmailIsCaseInsensitive()
    {
        var response = await factory.CreateClient().LoginAsync("  ADMIN@Padaria.Demo ", Demo.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(Demo.AdminEmail, "senha-errada")]
    [InlineData("ninguem@k2digital.com", "Qualquer@123")]
    public async Task Login_WithInvalidCredentials_Returns401WithGenericMessage(string email, string password)
    {
        var response = await factory.CreateClient().LoginAsync(email, password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_WithInvalidPayload_Returns400()
    {
        var response = await factory.CreateClient().LoginAsync("nao-e-email", "");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_LocksAccountAfterFiveFailures()
    {
        var admin = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);
        var email = $"bloqueio-{Guid.NewGuid():N}@padaria.demo";
        var companyId = await PadariaIdAsync();
        (await admin.PostJsonAsync("/api/v1/users", new { name = "Bloqueio", email, password = "Senha@123", role = "Attendant", companyId }))
            .EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++) await client.LoginAsync(email, "Errada@123");

        var response = await client.LoginAsync(email, "Senha@123");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("locked", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_RotatesToken()
    {
        var client = factory.CreateClient(new() { HandleCookies = false });
        var firstCookie = CookieValue(await client.LoginAsync(Demo.PadariaAttendant, Demo.Password));

        var refreshed = await Refresh(client, firstCookie);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.NotEqual(firstCookie, CookieValue(refreshed));
    }

    [Fact]
    public async Task Refresh_ReuseWithinGraceWindow_IsAccepted()
    {
        // Ex.: reload duplo — o navegador não recebeu o cookie sucessor e reenvia o anterior.
        var client = factory.CreateClient(new() { HandleCookies = false });
        var firstCookie = CookieValue(await client.LoginAsync(Demo.PadariaAttendant, Demo.Password));
        var secondCookie = CookieValue(await Refresh(client, firstCookie));

        var reuse = await Refresh(client, firstCookie);

        Assert.Equal(HttpStatusCode.OK, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, secondCookie)).StatusCode);
    }

    [Fact]
    public async Task Refresh_LateReuseOfRotatedToken_RevokesAllSessions()
    {
        var client = factory.CreateClient(new() { HandleCookies = false });
        var firstCookie = CookieValue(await client.LoginAsync(Demo.PadariaAttendant, Demo.Password));
        var secondCookie = CookieValue(await Refresh(client, firstCookie));

        // Simula a rotação ocorrida há 5 minutos (fora da janela de tolerância).
        await using (var db = factory.SystemDb())
        {
            var rotated = await db.RefreshTokens.Where(t => t.ReplacedByTokenHash != null && t.RevokedAt > DateTime.UtcNow.AddMinutes(-1)).ToListAsync();
            foreach (var t in rotated) t.RevokedAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, firstCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, secondCookie)).StatusCode);
        await using var check = factory.SystemDb();
        Assert.True(await check.AuditLogs.AnyAsync(a => a.Action == "auth.refresh_reuse_detected"));
    }

    [Fact]
    public async Task Refresh_WithoutCookie_Returns401()
    {
        var response = await factory.CreateClient().PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var client = factory.CreateClient(new() { HandleCookies = false });
        var cookie = CookieValue(await client.LoginAsync(Demo.PadariaViewer, Demo.Password));

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", $"k2_refresh={cookie}");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, cookie)).StatusCode);
    }

    [Fact]
    public async Task Me_RequiresAuthentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/auth/me")).StatusCode);

        var client = await factory.AuthenticatedAsync(Demo.BellaAdmin);
        var me = await (await client.GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        Assert.Equal("Studio Bella", me.GetProperty("companyName").GetString());
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPassword_AndAllowsLoginWithNewOne()
    {
        var admin = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);
        var email = $"troca-{Guid.NewGuid():N}@padaria.demo";
        (await admin.PostJsonAsync("/api/v1/users", new { name = "Troca", email, password = "Antiga@123", role = "Viewer", companyId = await PadariaIdAsync() }))
            .EnsureSuccessStatusCode();
        var client = await factory.AuthenticatedAsync(email, "Antiga@123");

        var wrong = await client.PostJsonAsync("/api/v1/auth/change-password", new { currentPassword = "Errada@123", newPassword = "Nova@1234" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var weak = await client.PostJsonAsync("/api/v1/auth/change-password", new { currentPassword = "Antiga@123", newPassword = "fraca" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var ok = await client.PostJsonAsync("/api/v1/auth/change-password", new { currentPassword = "Antiga@123", newPassword = "Nova@1234" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().LoginAsync(email, "Nova@1234")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().LoginAsync(email, "Antiga@123")).StatusCode);
    }

    [Fact]
    public async Task PasswordsAreStoredHashed()
    {
        await using var db = factory.SystemDb();
        var user = await db.Users.FirstAsync(u => u.Email == Demo.PadariaAdmin);
        Assert.DoesNotContain(Demo.Password, user.PasswordHash);
        Assert.True(user.PasswordHash.Length > 40);
    }

    [Fact]
    public async Task LoginEventsAreAudited()
    {
        await factory.CreateClient().LoginAsync(Demo.BellaAdmin, Demo.Password);
        await using var db = factory.SystemDb();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "auth.login"));
    }

    private async Task<Guid> PadariaIdAsync()
    {
        await using var db = factory.SystemDb();
        return (await db.Companies.FirstAsync(c => c.Name == "Padaria Jardim Andrade")).Id;
    }

    private static string CookieValue(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("k2_refresh="));
        return header.Split(';')[0]["k2_refresh=".Length..];
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"k2_refresh={cookie}");
        return client.SendAsync(request);
    }
}

public class AuthRateLimitTests(StrictRateLimitFactory factory) : IClassFixture<StrictRateLimitFactory>
{
    [Fact]
    public async Task Login_IsRateLimitedPerIp()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 3; i++) await client.LoginAsync("x@y.com", "Errada@123");

        var response = await client.LoginAsync(Demo.AdminEmail, Demo.AdminPassword);
        Assert.Equal((HttpStatusCode)429, response.StatusCode);
    }

    /// <summary>
    /// A renovação de sessão tem limite próprio: vários usuários no mesmo IP (escritório) recarregando a página
    /// não podem ser deslogados pelo limite anti-força-bruta do login.
    /// </summary>
    [Fact]
    public async Task Refresh_IsNotCountedAgainstTheLoginLimit()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 8; i++)
        {
            var refresh = await client.PostAsync("/api/v1/auth/refresh", null);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode); // sem cookie: 401, nunca 429
        }
    }
}
