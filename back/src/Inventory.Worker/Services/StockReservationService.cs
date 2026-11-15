using Inventory.Worker.Domain;
using Inventory.Worker.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;

namespace Inventory.Worker.Services;

public sealed record ReservationResult(bool Reserved, string? Motivo, bool Duplicate);

public class StockReservationService(InventoryDbContext inventoryDbContext, TimeProvider time)
{
    public async Task<ReservationResult> ReserveAsync(OrderCreated orderCreatedEvent, CancellationToken cancellationToken)
    {
        // 1) Si ya lo procesamos, no tocamos el stock: devolvemos el resultado guardado.
        var solicPrevia = await FindProcessedAsync(orderCreatedEvent.EventId, cancellationToken);
        if (solicPrevia is not null) return ToResult(solicPrevia, duplicate: true);

        try
        {
            // 2) Descuento + registro del evento en UNA transacción. Pasan los dos o ninguno.
            await using var transaction = await inventoryDbContext.Database.BeginTransactionAsync(cancellationToken);

            // El descuento es atómico y condicional: nunca deja el stock negativo, aun con concurrencia.
            var updated = await inventoryDbContext.Stock
                .Where(s => s.Sku == orderCreatedEvent.Sku && s.Disponible >= orderCreatedEvent.Cantidad)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Disponible, x => x.Disponible - orderCreatedEvent.Cantidad), cancellationToken);

            var now = time.GetUtcNow().UtcDateTime;
            ProcessedEvent processed;
            if (updated == 1)
            {
                processed = ProcessedEvent.Create(orderCreatedEvent.EventId, ReservationOutcome.Reserved, null, now);
            }
            else
            {
                var skuExists = await inventoryDbContext.Stock.AnyAsync(s => s.Sku == orderCreatedEvent.Sku, cancellationToken);
                var motivo = skuExists ? "Stock insuficiente." : "El SKU no existe en el inventario.";
                processed = ProcessedEvent.Create(orderCreatedEvent.EventId, ReservationOutcome.Rejected, motivo, now);
            }

            inventoryDbContext.ProcessedEvents.Add(processed);
            await inventoryDbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToResult(processed, duplicate: false);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Carrera: otro consumidor procesó este mismo evento entre el paso 1 y el insert.
            // Nuestra transacción se revirtió (incluido el descuento), así que devolvemos su resultado.
            inventoryDbContext.ChangeTracker.Clear();
            var existing = await FindProcessedAsync(orderCreatedEvent.EventId, cancellationToken)
                ?? throw new InvalidOperationException($"Evento {orderCreatedEvent.EventId} duplicado pero sin registro.");
            return ToResult(existing, duplicate: true);
        }
    }

    private Task<ProcessedEvent?> FindProcessedAsync(Guid eventId, CancellationToken cancellationToken) =>
        inventoryDbContext.ProcessedEvents.AsNoTracking().FirstOrDefaultAsync(p => p.EventId == eventId, cancellationToken);

    private static ReservationResult ToResult(ProcessedEvent eventPropcessed, bool duplicate) =>
        new(eventPropcessed.Resultado == ReservationOutcome.Reserved, eventPropcessed.Motivo, duplicate);

    // 2627 = violación de PK/unique; 2601 = índice único duplicado
    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2627 or 2601 };
}