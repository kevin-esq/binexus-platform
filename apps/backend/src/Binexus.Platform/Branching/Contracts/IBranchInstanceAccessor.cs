namespace Binexus.Platform.Branching.Contracts;

/// <summary>
/// Reads the local Branch Server installation identity after startup initialization.
/// Registered only for <c>RuntimeMode=Branch</c>.
/// </summary>
public interface IBranchInstanceAccessor
{
    ValueTask<BranchInstanceInfo> GetAsync(CancellationToken cancellationToken = default);
}
