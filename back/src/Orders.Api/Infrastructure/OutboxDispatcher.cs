using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using Orders.Api.Domain;

namespace Orders.Api.Infrastructure;

public class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    await DispatchPendingAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // Broker caído, timeout, etc.: los mensajes siguen pendientes y se reintentan en el próximo ciclo.
                    logger.LogError(ex, "No se pudo publicar desde el outbox; se reintentará en {Seconds}s.",
                        PollInterval.TotalSeconds);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // apagado ordenado
        }
    }

    private async Task DispatchPendingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pending = await db.Outbox
            .Where(m => m.PublicadoEn == null)
            .OrderBy(m => m.CreadoEn)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            await PublishAsync(bus, message, ct);   // si falla, corta el lote y reintenta luego
            message.MarkPublished(time.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Outbox: publicado {Tipo} {EventId}.", message.Tipo, message.EventId);
        }
    }

    private static async Task PublishAsync(IPublishEndpoint bus, OutboxMessage message, CancellationToken ct)
    {
        // Timeout propio: si RabbitMQ no responde, no nos quedamos esperando indefinidamente.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PublishTimeout);

        switch (message.Tipo)
        {
            case nameof(OrderCreated):
                var evt = JsonSerializer.Deserialize<OrderCreated>(message.Payload)!;
                await bus.Publish(evt, cts.Token);
                break;
            default:
                throw new InvalidOperationException($"Tipo de evento desconocido en el outbox: {message.Tipo}");
        }
    }
}