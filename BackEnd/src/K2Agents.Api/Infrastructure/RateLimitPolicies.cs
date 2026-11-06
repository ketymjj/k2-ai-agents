using System.Threading.RateLimiting;
using K2Agents.Api.Localization;
using Microsoft.AspNetCore.RateLimiting;

namespace K2Agents.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Session = "session";
    public const string AI = "ai";
    public const string WebChat = "webchat";

    /// <summary>
    /// Auth: janela fixa por IP (força bruta de login). Global: por usuário autenticado ou IP.
    /// Limites configuráveis em RateLimiting:*.
    /// </summary>
    public static IServiceCollection AddK2RateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var authPerMinute = configuration.GetValue("RateLimiting:AuthPerMinute", 10);
        var globalPerMinute = configuration.GetValue("RateLimiting:GlobalPerMinute", 600);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    status = 429,
                    title = MessageCatalog.Translate("Muitas requisições. Aguarde alguns instantes e tente novamente.",
                        RequestLanguage.From(context.HttpContext)),
                    code = "rate_limited"
                }, ct);
            };

            options.AddPolicy(Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));

            // Renovação de sessão: o token de 256 bits não é adivinhável, então o risco não é força bruta, e sim abuso.
            // Limite mais generoso que o do login, para vários usuários atrás do mesmo IP (escritório) não serem deslogados.
            var sessionPerMinute = configuration.GetValue("RateLimiting:SessionPerMinute", 120);
            options.AddPolicy(Session, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = sessionPerMinute, Window = TimeSpan.FromMinutes(1) }));

            // Chamadas de IA têm custo: limite por usuário (ou IP) por minuto.
            var aiPerMinute = configuration.GetValue("RateLimiting:AIPerMinute", 20);
            options.AddPolicy(AI, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = aiPerMinute, Window = TimeSpan.FromMinutes(1) }));

            // Widget público (anônimo): limite por IP, independente do token do canal.
            var webChatPerMinute = configuration.GetValue("RateLimiting:WebChatPerMinute", 40);
            options.AddPolicy(WebChat, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = webChatPerMinute, Window = TimeSpan.FromMinutes(1) }));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var key = http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(key,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPerMinute, Window = TimeSpan.FromMinutes(1) });
            });
        });
    }
}
