using Binexus.Platform.Configuration;
using Binexus.Platform.Messaging;
using Microsoft.Extensions.Options;

namespace Binexus.Workers.Outbox;

public sealed class OutboxWorkerHost(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxWorkerHost> logger,
    IOptions<OutboxWorkerOptions> options) : BackgroundService
{
    private readonly string _workerId = $"worker-{Guid.CreateVersion7()}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WorkerLog.WorkerStarted(logger, options.Value.PollInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                await processor.ProcessBatchAsync(_workerId, stoppingToken);
                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            WorkerLog.WorkerStopping(logger);
        }
    }
}
