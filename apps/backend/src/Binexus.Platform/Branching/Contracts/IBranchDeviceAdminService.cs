using Binexus.Platform.Branching.Pairing;

namespace Binexus.Platform.Branching.Contracts;

/// <summary>
/// Admin surface for the pairing ceremony and device lifecycle. Every operation requires an
/// authenticated ADMIN / SUPER_ADMIN user whose tenant/branch match the active Branch instance.
/// </summary>
public interface IBranchDeviceAdminService
{
    Task<CreatePairingSessionResult> CreateSessionAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);

    Task<PairingRequestView> GetRequestAsync(
        Guid tenantId,
        Guid branchId,
        Guid pairingRequestId,
        CancellationToken cancellationToken = default);

    Task<ApprovePairingRequestResult> ApproveRequestAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid pairingRequestId,
        CancellationToken cancellationToken = default);

    Task<RejectPairingRequestResult> RejectRequestAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid pairingRequestId,
        CancellationToken cancellationToken = default);

    Task<RevokeDeviceResult> RevokeDeviceAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>Disables the Active terminal for a device, bumps security stamp, and evicts DAT status cache.</summary>
    Task<DisableTerminalResult> DisableTerminalAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid terminalId,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the Active terminal binding, bumps security stamp, and evicts DAT status cache.</summary>
    Task<RebindTerminalResult> RebindTerminalAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid deviceId,
        string newTerminalName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PairedDeviceView>> ListDevicesAsync(
        Guid tenantId,
        Guid branchId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BranchTerminalView>> ListTerminalsAsync(
        Guid tenantId,
        Guid branchId,
        CancellationToken cancellationToken = default);
}
