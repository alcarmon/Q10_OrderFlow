using Inventory.Worker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Worker.Infrastructure;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(stockItem =>
        {
            stockItem.ToTable("Stock", t => t.HasCheckConstraint("CK_Stock_Disponible_NoNegativo", "[Disponible] >= 0"));
            stockItem.HasKey(x => x.Sku);
            stockItem.Property(x => x.Sku).HasMaxLength(50);
        });

        modelBuilder.Entity<ProcessedEvent>(processedEvent =>
        {
            processedEvent.ToTable("ProcessedEvents");
            processedEvent.HasKey(x => x.EventId);                       // la PK única es lo que garantiza la idempotencia
            processedEvent.Property(x => x.EventId).ValueGeneratedNever();
            processedEvent.Property(x => x.Resultado).HasConversion<string>().HasMaxLength(20).IsRequired();
            processedEvent.Property(x => x.Motivo).HasMaxLength(200);
            processedEvent.Property(x => x.ProcesadoEn)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });
    }
}