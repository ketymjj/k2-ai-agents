using K2Agents.Application.Privacy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace K2Agents.Infrastructure.Privacy;

/// <summary>Executa a política de retenção periodicamente (Retention:IntervalHours, padrão 24 h).</summary>
public class RetentionWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Retention:Enabled", true)) return;
        var interval = TimeSpan.FromHours(Math.Clamp(configuration.GetValue("Retention:IntervalHours", 24.0), 0.1, 24 * 7));

        // Espera a API terminar migrations/seed antes da primeira execução.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IRetentionService>().PurgeAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao aplicar a política de retenção de conversas.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
