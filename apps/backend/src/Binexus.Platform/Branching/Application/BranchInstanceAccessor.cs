using Binexus.Platform.Branching.Contracts;

namespace Binexus.Platform.Branching.Application;

public sealed class BranchInstanceAccessor(BranchInstanceMemoryStore memoryStore) : IBranchInstanceAccessor
{
    public ValueTask<BranchInstanceInfo> GetAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(memoryStore.GetRequired());
}
