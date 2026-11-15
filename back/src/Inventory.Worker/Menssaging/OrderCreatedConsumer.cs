using Inventory.Worker.Services;
using MassTransit;
using OrderFlow.Contracts;

namespace Inventory.Worker.Messaging;

public class OrderCreatedConsumer(
    StockReservationService reservations,
    TimeProvider time,
    ILogger<OrderCreatedConsumer> logger) : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        var evt = context.Message;
        var result = await reservations.ReserveAsync(evt, context.CancellationToken);
        var now = time.GetUtcNow().UtcDateTime;

        // Se publica SIEMPRE el resultado, también en duplicados: si la app cayó después de confirmar
        // la transacción y antes de publicar, la reentrega del mensaje cierra el ciclo y el pedido no se queda Pending.
        if (result.Reserved)
        {
            await context.Publish(new StockReserved(Guid.NewGuid(), evt.OrderId, evt.Sku, evt.Cantidad, now));
        }
        else
        {
            await context.Publish(new StockRejected(
                Guid.NewGuid(), evt.OrderId, evt.Sku, evt.Cantidad, result.Motivo ?? "Rechazado.", now));
        }

        logger.LogInformation(
            "OrderCreated {EventId} (pedido {OrderId}): {Resultado}{Duplicado}.",
            evt.EventId, evt.OrderId,
            result.Reserved ? "StockReserved" : $"StockRejected ({result.Motivo})",
            result.Duplicate ? " [DUPLICADO: stock NO descontado de nuevo]" : string.Empty);
    }
}