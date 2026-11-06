using K2Agents.Application.Abstractions;
using K2Agents.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Api.Controllers;

/// <summary>Informações públicas de estado da plataforma (sem dados sensíveis).</summary>
[ApiController]
[Route("api/v1/system")]
public class SystemController(AppDbContext db, IAIProvider aiProvider) : ControllerBase
{
    [HttpGet("info")]
    public async Task<IActionResult> Info(CancellationToken ct)
    {
        var databaseOnline = await db.Database.CanConnectAsync(ct);
        string[] pendingMigrations = [];
        if (databaseOnline && db.Database.IsRelational())
            pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();

        return Ok(new
        {
            name = "K2 AI Agents",
            version = typeof(SystemController).Assembly.GetName().Version?.ToString(3),
            environment = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
            database = new { online = databaseOnline, pendingMigrations = pendingMigrations.Length },
            // Indica apenas se a chave existe; o valor nunca sai do servidor.
            aiProviderConfigured = aiProvider.IsConfigured,
            aiProvider = aiProvider.Name,
            serverTimeUtc = DateTime.UtcNow
        });
    }
}
