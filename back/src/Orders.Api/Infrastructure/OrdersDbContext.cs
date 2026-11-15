using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;

namespace Orders.Api.Infrastructure;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> dbCpontextOptions) : DbContext(dbCpontextOptions)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("Orders");
            order.HasKey(x => x.OrderId);
            order.Property(x => x.ClienteNombre).HasMaxLength(200).IsRequired();
            order.Property(x => x.Sku).HasMaxLength(50).IsRequired();
            order.Property(x => x.EstadoOrden).HasConversion<string>().HasMaxLength(20).IsRequired();
            // datetime2 vuelve con Kind=Unspecified; lo forzamos a UTC para serializar con "Z"
            order.Property(x => x.FechaCreacion).HasColumnType("datetime2")
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            order.HasIndex(x => x.FechaCreacion);
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("Products");
            product.HasKey(x => x.Sku);
            product.Property(x => x.Sku).HasMaxLength(50);
        });

        modelBuilder.Entity<OutboxMessage>(outboxMessage =>
        {
            outboxMessage.ToTable("Outbox");
            outboxMessage.HasKey(outboxMessage => outboxMessage.EventId);
            outboxMessage.Property(x => x.Tipo).HasMaxLength(100).IsRequired();
            outboxMessage.Property(x => x.Payload).IsRequired();
            outboxMessage.Property(x => x.CreadoEn).HasColumnType("datetime2")
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            outboxMessage.Property(x => x.PublicadoEn).HasColumnType("datetime2");
            // Índice filtrado: solo los pendientes, que es lo único que consulta el dispatcher
            outboxMessage.HasIndex(x => x.CreadoEn).HasFilter("[PublicadoEn] IS NULL");
        });
    }
}