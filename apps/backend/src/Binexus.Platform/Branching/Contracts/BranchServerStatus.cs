namespace Binexus.Platform.Branching.Contracts;

/// <summary>
/// Branch Server installation status. PR 2 persists only <see cref="ReadyForActivation"/>.
/// Future activation may add Active and related lifecycle values.
/// </summary>
public enum BranchServerStatus
{
    ReadyForActivation = 0,
}
