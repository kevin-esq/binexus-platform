using Binexus.Platform.Branching.Contracts;

namespace Binexus.Platform.Branching.Application;

/// <summary>
/// Process-local Branch identity published only after a successful Ensure (or Activate upgrade).
/// Does not hold EF entities or DbContext.
/// </summary>
public sealed class BranchInstanceMemoryStore
{
    private BranchInstanceInfo? _info;

    /// <summary>
    /// Publishes identity. Same Id with identical payload is a no-op.
    /// Same Id may upgrade <see cref="BranchServerStatus.ReadyForActivation"/> →
    /// <see cref="BranchServerStatus.Active"/> (with TenantId/BranchId).
    /// A different Id is always rejected.
    /// </summary>
    public BranchInstanceInfo Publish(BranchInstanceInfo info)
    {
        if (_info is null)
        {
            _info = info;
            return info;
        }

        if (_info.Id != info.Id)
        {
            throw new InvalidOperationException(
                "BranchInstance identity is already published and cannot be replaced with a different Id.");
        }

        if (_info.Status == info.Status
            && _info.TenantId == info.TenantId
            && _info.BranchId == info.BranchId)
        {
            return _info;
        }

        if (_info.Status == BranchServerStatus.ReadyForActivation
            && info.Status == BranchServerStatus.Active
            && info.TenantId is not null
            && info.BranchId is not null)
        {
            _info = info;
            return info;
        }

        throw new InvalidOperationException(
            "BranchInstance identity is already published and cannot be replaced with a different value.");
    }

    public BranchInstanceInfo GetRequired() =>
        _info ?? throw new InvalidOperationException(
            "BranchInstance has not been initialized. Call EnsureBranchRuntimeInitializedAsync before serving.");

    public bool IsPublished => _info is not null;
}
