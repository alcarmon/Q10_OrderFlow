using Inventory.Worker.Infrastructure;
using Inventory.Worker.Messaging;
using Inventory.Worker.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(inventoryDbContext =>
    inventoryDbContext.UseSqlServer(builder.Configuration.GetConnectionString("InventoryDb")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:InventoryDb.")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<StockReservationService>();

var rabbit = builder.Configuration.GetSection(RabbitMqOptions.Section).Get<RabbitMqOptions>()
    ?? throw new InvalidOperationException("Falta la sección RabbitMq.");
ArgumentException.ThrowIfNullOrWhiteSpace(rabbit.Username, "RabbitMq:Username");
ArgumentException.ThrowIfNullOrWhiteSpace(rabbit.Password, "RabbitMq:Password");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbit.Host, "/", h =>
        {
            h.Username(rabbit.Username);
            h.Password(rabbit.Password);
        });

        // 5 reintentos; si siguen fallando el mensaje va a la cola "order-created_error"
        cfg.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(2)));
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
await DbInitializer.InitializeAsync(host.Services, builder.Configuration, lifetime.ApplicationStopping);

host.Run();