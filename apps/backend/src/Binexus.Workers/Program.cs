using Binexus.Workers.Hosting;
using Binexus.Workers.Outbox;

var builder = WorkersHost.CreateBuilder(args);
builder.Services.AddHostedService<OutboxWorkerHost>();

var app = builder.Build();
WorkersHost.MapOperationalEndpoints(app);

await app.RunAsync();
