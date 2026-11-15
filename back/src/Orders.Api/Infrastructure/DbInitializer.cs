using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;

namespace Orders.Api.Infrastructure;

internal sealed record SeedProduct(string Sku, string? Nombre, int Disponible);

public static class DbInitializer
{
    private const int MaxAttempts = 30;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DbInitializer));

        await MigrateWithRetryAsync(db, logger, cancellationToken);
        await SeedProductsAsync(db, config, env, logger, cancellationToken);
    }

    private static async Task MigrateWithRetryAsync(OrdersDbContext ordersDbContext, ILogger logger, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ordersDbContext.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (SqlException ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning("SQL Server aún no está listo (intento {Attempt}/{Max}): {Message}",
                    attempt, MaxAttempts, ex.Message);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private static async Task SeedProductsAsync(
        OrdersDbContext orderDbContext, IConfiguration configuration, IHostEnvironment environment, ILogger logger, CancellationToken cancellationToken)
    {
        var configured = configuration["Seed:ProductsPath"]
            ?? throw new InvalidOperationException("Falta la configuración Seed:ProductsPath.");
        var path = Path.GetFullPath(configured, environment.ContentRootPath);

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var items = JsonSerializer.Deserialize<List<SeedProduct>>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        var existing = (await orderDbContext.Products.Select(p => p.Sku).ToListAsync(cancellationToken)).ToHashSet();
        var nuevos = items
            .Select(i => i.Sku.Trim().ToUpperInvariant())
            .Where(sku => !existing.Contains(sku))
            .Distinct()
            .Select(sku => new Product { Sku = sku })
            .ToList();

        if (nuevos.Count == 0) return;

        orderDbContext.Products.AddRange(nuevos);
        await orderDbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seed: {Count} productos nuevos en el catálogo.", nuevos.Count);
    }
}