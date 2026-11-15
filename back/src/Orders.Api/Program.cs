using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Endpoints;
using Orders.Api.Infrastructure;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrdersDbContext>(ordersDBContext =>
    ordersDBContext.UseSqlServer(builder.Configuration.GetConnectionString("OrdersDb")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:OrdersDb.")));

builder.Services.AddSingleton(TimeProvider.System);

var rabbit = builder.Configuration.GetSection(RabbitMqOptions.Section).Get<RabbitMqOptions>()
    ?? throw new InvalidOperationException("Falta la sección RabbitMq.");
ArgumentException.ThrowIfNullOrWhiteSpace(rabbit.Username, "RabbitMq:Username");
ArgumentException.ThrowIfNullOrWhiteSpace(rabbit.Password, "RabbitMq:Password");

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbit.Host, "/", h =>
        {
            h.Username(rabbit.Username);
            h.Password(rabbit.Password);
        });
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddHostedService<OutboxDispatcher>();

builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
    jsonOptions.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

await DbInitializer.InitializeAsync(app.Services, app.Configuration, app.Lifetime.ApplicationStopping);

app.MapOrdersEndpoints();
app.Run();