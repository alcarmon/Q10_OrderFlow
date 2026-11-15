namespace Orders.Api.Endpoints;

public record CreateOrderRequest(string? ClienteNombre, string? Sku, int Cantidad);