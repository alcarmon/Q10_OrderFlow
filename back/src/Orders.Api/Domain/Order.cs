namespace Orders.Api.Domain;

public class Order
{
    public const int MinCantidad = 1;
    public const int MaxCantidad = 100;

    private Order() { }

    public Guid OrderId { get; private set; }
    public string ClienteNombre { get; private set; } = default!;
    public string Sku { get; private set; } = default!;
    public int Cantidad { get; private set; }
    public OrderStatus EstadoOrden { get; private set; }
    public DateTime FechaCreacion { get; private set; }

    public static Order Create(string clienteNombre, string sku, int cantidad, DateTime FechacreacionUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clienteNombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentOutOfRangeException.ThrowIfLessThan(cantidad, MinCantidad);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cantidad, MaxCantidad);

        return new Order
        {
            OrderId = Guid.NewGuid(),
            ClienteNombre = clienteNombre.Trim(),
            Sku = sku.Trim().ToUpperInvariant(),
            Cantidad = cantidad,
            EstadoOrden = OrderStatus.Pending,
            FechaCreacion = FechacreacionUtc
        };
    }

    /// <returns>Devuelve true si cambió el estado; false si ya no estaba Pending (esto para eventos duplicado o tardío).</returns>
    public bool Confirm() => Transition(OrderStatus.Confirmed);

    /// <returns>true si cambió el estado; false si ya no estaba Pending.</returns>
    public bool Reject() => Transition(OrderStatus.Rejected);

    private bool Transition(OrderStatus target)
    {
        if (EstadoOrden != OrderStatus.Pending) return false;
        EstadoOrden = target;
        return true;
    }
}