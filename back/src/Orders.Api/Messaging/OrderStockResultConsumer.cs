using MassTransit;
using OrderFlow.Contracts;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;
using Orders.Api.Infrastructure;

namespace Orders.Api.Messaging;

/// <summary>Cierra el ciclo: aplica el resultado de Inventory al pedido.</summary>
public class OrderStockResultConsumer(OrdersDbContext db, ILogger<OrderStockResultConsumer> logger)
    : IConsumer<StockReserved>, IConsumer<StockRejected>
{
    public Task Consume(ConsumeContext<StockReserved> context) =>
        ApplyAsync(context.Message.OrderId, "StockReserved", order => order.Confirm(), context.CancellationToken);

    public Task Consume(ConsumeContext<StockRejected> context) =>
        ApplyAsync(context.Message.OrderId, $"StockRejected ({context.Message.Motivo})",
            order => order.Reject(), context.CancellationToken);

    private async Task ApplyAsync(Guid orderId, string evento, Func<Order, bool> transition, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(order => order.OrderId == orderId, ct);
        if (order is null)
        {
            logger.LogWarning("{Evento}: el pedido {OrderId} no existe; se descarta.", evento, orderId);
            return;
        }

        // La transición solo ocurre desde Pending: un evento duplicado o tardío no cambia nada.
        if (!transition(order))
        {
            logger.LogInformation("{Evento}: pedido {OrderId} ya estaba {Estado}; evento ignorado.",
                evento, orderId, order.EstadoOrden);
            return;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("{Evento}: pedido {OrderId} -> {Estado}.", evento, orderId, order.EstadoOrden);
    }
}