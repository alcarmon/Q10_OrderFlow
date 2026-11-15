using Orders.Api.Domain;

namespace Orders.Tests;

public class OrderTests
{
    [Fact]
    public void Transitions_OnlyLeavePending_AnyLaterEventIsIgnored()
    {
        var confirmed = Order.Create("Ana Pérez", "ABC-01", 2, DateTime.UtcNow);
        Assert.Equal(OrderStatus.Pending, confirmed.EstadoOrden);

        Assert.True(confirmed.Confirm());     // Pending -> Confirmed
        Assert.False(confirmed.Confirm());    // evento duplicado
        Assert.False(confirmed.Reject());     // evento contradictorio
        Assert.Equal(OrderStatus.Confirmed, confirmed.EstadoOrden);

        var rejected = Order.Create("Luis Gómez", "ABC-03", 11, DateTime.UtcNow);
        Assert.True(rejected.Reject());       // Pending -> Rejected
        Assert.False(rejected.Confirm());
        Assert.Equal(OrderStatus.Rejected, rejected.EstadoOrden);
    }
}