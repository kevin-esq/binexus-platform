using Binexus.Platform.Dispatching;
using Binexus.SharedKernel.Abstractions;
using Binexus.SharedKernel.Results;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.UnitTests.Dispatching;

public sealed class CommandDispatcherTests
{
    [Fact]
    public async Task NonTransactionalCommand_does_not_open_transaction()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<Binexus.Platform.Persistence.BinexusDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<ICommandHandler<ProbeNonTransactionalCommand>, ProbeHandler>();
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Binexus.Platform.Tenancy.ICurrentTenant, Binexus.Platform.Tenancy.CurrentTenant>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.DispatchAsync(new ProbeNonTransactionalCommand());
        result.IsSuccess.Should().BeTrue();
    }

    private sealed record ProbeNonTransactionalCommand : INonTransactionalCommand;

    private sealed class ProbeHandler : ICommandHandler<ProbeNonTransactionalCommand>
    {
        public Task<Result> HandleAsync(ProbeNonTransactionalCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }
}
