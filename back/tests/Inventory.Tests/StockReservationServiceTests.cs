using Inventory.Worker.Domain;
using Inventory.Worker.Infrastructure;
using Inventory.Worker.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;

namespace Inventory.Tests;

public sealed class StockReservationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly InventoryDbContext _db;
    private readonly StockReservationService _service;

    public StockReservationServiceTests()
    {
        _connection.Open(); // la BD en memoria vive mientras la conexión siga abierta

        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_connection).Options;
        _db = new InventoryDbContext(options);
        _db.Database.EnsureCreated();

        _db.Stock.AddRange(new StockItem("ABC-01", 100), new StockItem("ABC-03", 10));
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _service = new StockReservationService(_db, TimeProvider.System);
    }

    [Fact]
    public async Task ReserveAsync_SameEventTwice_DecrementsStockOnce()
    {
        var evt = new OrderCreated(Guid.NewGuid(), Guid.NewGuid(), "ABC-01", 2, DateTime.UtcNow);

        var first = await _service.ReserveAsync(evt, CancellationToken.None);
        var second = await _service.ReserveAsync(evt, CancellationToken.None);

        Assert.True(first.Reserved);
        Assert.False(first.Duplicate);
        Assert.True(second.Reserved);      // se devuelve el mismo resultado...
        Assert.True(second.Duplicate);     // ...marcado como duplicado

        Assert.Equal(98, await StockOfAsync("ABC-01"));   // 100 - 2, no 100 - 4
        Assert.Equal(1, await _db.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task ReserveAsync_InsufficientStock_RejectsAndKeepsStock()
    {
        var evt = new OrderCreated(Guid.NewGuid(), Guid.NewGuid(), "ABC-03", 11, DateTime.UtcNow);

        var result = await _service.ReserveAsync(evt, CancellationToken.None);

        Assert.False(result.Reserved);
        Assert.Equal("Stock insuficiente.", result.Motivo);
        Assert.Equal(10, await StockOfAsync("ABC-03"));

        var processed = await _db.ProcessedEvents.AsNoTracking().SingleAsync();
        Assert.Equal(ReservationOutcome.Rejected, processed.Resultado);
    }

    private Task<int> StockOfAsync(string sku) =>
        _db.Stock.AsNoTracking().Where(s => s.Sku == sku).Select(s => s.Disponible).SingleAsync();

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}