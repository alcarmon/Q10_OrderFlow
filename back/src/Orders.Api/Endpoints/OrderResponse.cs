using Orders.Api.Domain;

namespace Orders.Api.Endpoints;

public record OrderResponse(
    Guid Id, string ClienteNombre, string Sku, int Cantidad, OrderStatus EstadoOrden, DateTime FechaCreacion)
{
    public static OrderResponse From(Order order) =>
        new(order.OrderId, order.ClienteNombre, order.Sku, order.Cantidad, order.EstadoOrden, order.FechaCreacion);
}