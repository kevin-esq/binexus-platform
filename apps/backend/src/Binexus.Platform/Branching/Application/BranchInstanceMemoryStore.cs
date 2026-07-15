using Binexus.Platform.Branching.Contracts;

namespace Binexus.Platform.Branching.Application;

/// <summary>
/// Process-local immutable Branch identity published only after a successful Ensure.
/// Does not hold EF entities or DbContext.
/// </summary>
public sealed class BranchInstanceMemoryStore
{
    private BranchInstanceInfo? _info;

    /// <summary>
    /// Publishes identity once. Subsequent calls with the same Id are no-ops;
    /// a different Id is rejected so a race cannot flip the published value.
    /// </summary>
    public BranchInstanceInfo Publish(BranchInstanceInfo info)
    {
        if (_info is not null)
        {
            if (_info.Id != info.Id || _info.Status != info.Status)
            {
                throw new InvalidOperationException(
                    "BranchInstance identity is already published and cannot be replaced with a different value.");
            }

            return _info;
        }

        _info = info;
        return info;
    }

    public BranchInstanceInfo GetRequired() =>
        _info ?? throw new InvalidOperationException(
            "BranchInstance has not been initialized. Call EnsureBranchRuntimeInitializedAsync before serving.");

    public bool IsPublished => _info is not null;
}
