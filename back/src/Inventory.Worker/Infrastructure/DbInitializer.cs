using System.Text.Json;
using Inventory.Worker.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Worker.Infrastructure;

internal sealed record SeedProduct(string Sku, string? Nombre, int Disponible);

public static class DbInitializer
{
    private const int MaxAttempts = 30;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var inventoryDbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DbInitializer));

        await MigrateWithRetryAsync(inventoryDbContext, logger, cancellationToken);
        await SeedStockAsync(inventoryDbContext, config, env, logger, cancellationToken);
    }

    private static async Task MigrateWithRetryAsync(InventoryDbContext inventoryDbContext, ILogger logger, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await inventoryDbContext.Database.MigrateAsync(cancellationToken);
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

   private static async Task SeedStockAsync(
        InventoryDbContext inventoryDbContext, IConfiguration config, IHostEnvironment env, ILogger logger, CancellationToken cancellationToken)
    {
        var configured = config["Seed:ProductsPath"]
            ?? throw new InvalidOperationException("Falta la configuración Seed:ProductsPath.");
        var path = Path.GetFullPath(configured, env.ContentRootPath);

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var productItems = JsonSerializer.Deserialize<List<SeedProduct>>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        var existing = (await inventoryDbContext.Stock.Select(s => s.Sku).ToListAsync(cancellationToken)).ToHashSet();
        var nuevos = productItems
            .Select(productItem => new { Sku = productItem.Sku.Trim().ToUpperInvariant(), productItem.Disponible })
            .Where(productItem => !existing.Contains(productItem.Sku))
            .DistinctBy(productItem => productItem.Sku)
            .Select(productItem => new StockItem(productItem.Sku, productItem.Disponible))
            .ToList();

        logger.LogInformation("Seed: archivo {Path}, {Items} elementos, {Existing} SKU ya existentes.",
            path, productItems.Count, existing.Count);

        if (nuevos.Count == 0) return;

        inventoryDbContext.Stock.AddRange(nuevos);
        await inventoryDbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seed: {Count} productos nuevos en el inventario.", nuevos.Count);
    }
}