using Binexus.Platform.Branching.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Platform.Hosting;

public static class BranchRuntimeInitializationExtensions
{
    /// <summary>
    /// Branch-only: ensures local installation identity exists before the host serves traffic.
    /// Cloud no-ops when <see cref="IBranchInstanceInitializer"/> is not registered.
    /// </summary>
    public static async Task EnsureBranchRuntimeInitializedAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetService<IBranchInstanceInitializer>();
        if (initializer is null)
        {
            return;
        }

        await initializer.EnsureAsync(cancellationToken);
    }
}
