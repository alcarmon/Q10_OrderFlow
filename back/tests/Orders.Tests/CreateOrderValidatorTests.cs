using Orders.Api.Domain;
using Orders.Api.Endpoints;

namespace Orders.Tests;

public class CreateOrderValidatorTests
{
    [Fact]
    public void Validate_InvalidRequest_ReturnsErrorForEachField()
    {
        var request = new CreateOrderRequest("   ", "NOEXISTE", 0);

        var errors = CreateOrderValidator.Validate(request, skuExists: false);

        Assert.Contains("clienteNombre", errors.Keys);
        Assert.Contains("sku", errors.Keys);
        Assert.Contains("cantidad", errors.Keys);
    }

    [Fact]
    public void Validate_ValidRequest_AtQuantityBoundaries_ReturnsNoErrors()
    {
        foreach (var cantidad in new[] { Order.MinCantidad, Order.MaxCantidad })
        {
            var request = new CreateOrderRequest("Ana Pérez", "ABC-01", cantidad);

            var errors = CreateOrderValidator.Validate(request, skuExists: true);

            Assert.Empty(errors);
        }
    }
}