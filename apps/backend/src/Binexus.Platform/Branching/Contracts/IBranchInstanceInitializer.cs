namespace Binexus.Platform.Branching.Contracts;

/// <summary>
/// Ensures the singleton local <c>BranchInstance</c> row exists (idempotent).
/// Registered only for <c>RuntimeMode=Branch</c>.
/// </summary>
public interface IBranchInstanceInitializer
{
    Task<BranchInstanceInfo> EnsureAsync(CancellationToken cancellationToken = default);
}
