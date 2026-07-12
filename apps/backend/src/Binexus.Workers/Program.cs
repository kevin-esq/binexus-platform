using Binexus.Modules.Identity;
using Binexus.Modules.Inventory;
using Binexus.Modules.Logistics;
using Binexus.Modules.Orders;
using Binexus.Modules.Sales;
using Binexus.Modules.Warehouse;
using Binexus.Platform.DependencyInjection;
using Binexus.Workers.Outbox;

// Lightweight Kestrel host so compose/K8s can probe /health without coupling to Api.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBinexusPlatform(builder.Configuration);
builder.Services.AddBinexusDispatching();
builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);
builder.Services.AddInventoryModule();
builder.Services.AddOrdersModule();
builder.Services.AddWarehouseModule();
builder.Services.AddLogisticsModule(builder.Configuration);
builder.Services.AddSalesModule();
builder.Services.AddHostedService<OutboxWorkerHost>();

var app = builder.Build();

var liveness = () => Results.Ok(new { status = "ok" });
app.MapGet("/health", liveness);
app.MapGet("/health/live", liveness);

await app.RunAsync();
