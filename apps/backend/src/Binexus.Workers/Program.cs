using Binexus.Workers.Hosting;
using Binexus.Workers.Outbox;

var builder = WorkersHost.CreateBuilder(args);
builder.Services.AddHostedService<OutboxWorkerHost>();

var app = builder.Build();
await WorkersHost.InitializeAsync(app);
await app.RunAsync();
