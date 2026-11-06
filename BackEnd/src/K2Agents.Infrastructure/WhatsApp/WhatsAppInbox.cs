using System.Threading.Channels;
using K2Agents.Application.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace K2Agents.Infrastructure.WhatsApp;

/// <summary>
/// Fila em memória entre o webhook (que precisa responder 200 rápido à Meta) e o processamento (IA + envio).
/// Se a API reiniciar com itens na fila, a Meta reenvia os webhooks não confirmados... mas os já confirmados se perdem:
/// em produção com alto volume, troque por uma fila persistente (ver README, "Limitações").
/// </summary>
public sealed class WhatsAppInboxQueue : IWhatsAppInbox
{
    private readonly Channel<WhatsAppInbound> _channel = Channel.CreateBounded<WhatsAppInbound>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ChannelReader<WhatsAppInbound> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(WhatsAppInbound message, CancellationToken ct) => _channel.Writer.WriteAsync(message, ct);
}

public class WhatsAppInboxWorker(WhatsAppInboxQueue queue, IServiceScopeFactory scopes, ILogger<WhatsAppInboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IWhatsAppMessageProcessor>().ProcessAsync(message, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Falha ao processar mensagem do WhatsApp {MessageId}.", message.MessageId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
