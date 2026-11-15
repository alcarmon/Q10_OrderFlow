using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;
using Orders.Api.Infrastructure;
using OrderFlow.Contracts;

namespace Orders.Api.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");
        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateOrderRequest request, OrdersDbContext ordersDbContext, TimeProvider time, CancellationToken cancellationToken)
    {
        var sku = request.Sku?.Trim().ToUpperInvariant() ?? string.Empty;
        var skuExists = sku.Length > 0 && await ordersDbContext.Products.AnyAsync(p => p.Sku == sku, cancellationToken);

        var errors = CreateOrderValidator.Validate(request, skuExists);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = time.GetUtcNow().UtcDateTime;
        var order = Order.Create(request.ClienteNombre!, sku, request.Cantidad, now);
        var created = new OrderCreated(Guid.NewGuid(), order.OrderId, order.Sku, order.Cantidad, now);

        ordersDbContext.Orders.Add(order);
        ordersDbContext.Outbox.Add(OutboxMessage.FromEvent(created.EventId, created, now));
        await ordersDbContext.SaveChangesAsync(cancellationToken);   // una sola transacción: pedido + mensaje, o ninguno de los dos

        return Results.Created($"/orders/{order.OrderId}", OrderResponse.From(order));
    }

    private static async Task<IResult> ListAsync(OrdersDbContext db, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .OrderByDescending(order => order.FechaCreacion)
            .ToListAsync(ct);
        return Results.Ok(orders.Select(OrderResponse.From));
    }

    private static async Task<IResult> GetAsync(Guid id, OrdersDbContext db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(orden => orden.OrderId == id, ct);
        return order is null ? Results.NotFound() : Results.Ok(OrderResponse.From(order));
    }
}