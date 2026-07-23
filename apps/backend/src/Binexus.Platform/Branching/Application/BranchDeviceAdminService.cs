using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Crypto;
using Binexus.Platform.Branching.DeviceAuth;
using Binexus.Platform.Branching.Pairing;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Branching.Application;

/// <summary>
/// Admin ceremony surface: create a pairing session, review a request's short fingerprint, approve or
/// reject it, and revoke a device. Approval is the human gate that turns a proof-of-possession request
/// into a real Device + Terminal.
/// </summary>
public sealed class BranchDeviceAdminService(
    BinexusDbContext db,
    IBranchInstanceAccessor branchInstance,
    IPairingReceiptVault receiptVault,
    IOptions<DevicePairingOptions> options,
    TimeProvider timeProvider,
    IDeviceStatusResolver deviceStatusResolver) : IBranchDeviceAdminService
{
    private DevicePairingOptions Options => options.Value;

    public async Task<CreatePairingSessionResult> CreateSessionAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var instance = await branchInstance.GetAsync(cancellationToken);
        var instanceId = BranchPairingSupport.RequireActiveBranch(instance);
        BranchPairingSupport.RequireAdmin(role);
        BranchPairingSupport.RequireCoherentTenantBranch(instance, tenantId, branchId);

        var code = PairingCode.Generate();
        var now = timeProvider.GetUtcNow();
        var session = DevicePairingSession.CreateOpen(
            Guid.CreateVersion7(),
            instanceId,
            PairingCode.Hash(code, Options.CodePepper),
            userId,
            now.Add(Options.CodeTtl),
            now);
        db.DevicePairingSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatePairingSessionResult(session.Id, code, session.ExpiresAtUtc);
    }

    public async Task<PairingRequestView> GetRequestAsync(
        Guid tenantId,
        Guid branchId,
        Guid pairingRequestId,
        CancellationToken cancellationToken = default)
    {
        var instance = await branchInstance.GetAsync(cancellationToken);
        var instanceId = BranchPairingSupport.RequireActiveBranch(instance);
        BranchPairingSupport.RequireCoherentTenantBranch(instance, tenantId, branchId);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);
            var request = await LockRequestAsync(pairingRequestId, instanceId, cancellationToken)
                ?? throw NotFound();

            if (IsExpirable(request.Status) && request.ExpiresAtUtc <= now)
            {
                await ExpireApprovedRequestAsync(request, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return ToRequestView(request);
        });
    }

    public async Task<ApprovePairingRequestResult> ApproveRequestAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid pairingRequestId,
        CancellationToken cancellationToken = default)
    {
        var instance = await branchInstance.GetAsync(cancellationToken);
        var instanceId = BranchPairingSupport.RequireActiveBranch(instance);
        BranchPairingSupport.RequireAdmin(role);
        BranchPairingSupport.RequireCoherentTenantBranch(instance, tenantId, branchId);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);
            var request = await LockRequestAsync(pairingRequestId, instanceId, cancellationToken)
                ?? throw NotFound();

            if (request.Status == DevicePairingRequest.ApprovedStatus)
            {
                var confirmation = await EnsureConfirmationChallengeAsync(request, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return new ApprovePairingRequestResult(
                    request.Id, request.DeviceId, request.TerminalId!.Value, confirmation.Id, request.Status);
            }

            if (request.Status != DevicePairingRequest.PendingApprovalStatus || request.ExpiresAtUtc <= now)
            {
                if (request.Status == DevicePairingRequest.PendingApprovalStatus)
                {
                    request.MarkExpired();
                    await db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                }

                throw Conflict();
            }

            var terminalId = Guid.CreateVersion7();
            var rawReceipt = PairingSecret.Generate();
            var appNow = timeProvider.GetUtcNow();

            db.BranchDevices.Add(BranchDevice.CreatePendingConfirmation(
                request.DeviceId,
                instanceId,
                request.PublicKey,
                request.PublicKeyFingerprint,
                request.CredentialHash,
                request.Id,
                appNow));
            db.BranchTerminals.Add(BranchTerminal.CreatePendingConfirmation(
                terminalId,
                instanceId,
                request.DeviceId,
                request.RequestedTerminalName,
                request.RequestedTerminalNameNormalized,
                appNow));

            request.MarkApproved(terminalId, PairingSecret.Hash(rawReceipt), userId, appNow);
            var confirmationChallenge = DevicePairingChallenge.CreateConfirmation(
                Guid.CreateVersion7(),
                instanceId,
                request.Id,
                request.DeviceId,
                terminalId,
                request.PublicKeyFingerprint,
                request.CredentialHash,
                request.PairingReceiptHash!,
                PairingSecret.Generate(),
                TruncateToSeconds(now.Add(Options.ConfirmationChallengeTtl)),
                appNow);
            db.DevicePairingChallenges.Add(confirmationChallenge);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Unique constraints (terminal name, device_id, fingerprint, credential hash) lost a race.
                throw Conflict();
            }

            receiptVault.Store(request.Id, rawReceipt);
            await tx.CommitAsync(cancellationToken);
            return new ApprovePairingRequestResult(
                request.Id, request.DeviceId, terminalId, confirmationChallenge.Id, request.Status);
        });
    }

    public async Task<RejectPairingRequestResult> RejectRequestAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid pairingRequestId,
        CancellationToken cancellationToken = default)
    {
        var instance = await branchInstance.GetAsync(cancellationToken);
        var instanceId = BranchPairingSupport.RequireActiveBranch(instance);
        BranchPairingSupport.RequireAdmin(role);
        BranchPairingSupport.RequireCoherentTenantBranch(instance, tenantId, branchId);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);
            var request = await LockRequestAsync(pairingRequestId, instanceId, cancellationToken)
                ?? throw NotFound();

            if (request.Status == DevicePairingRequest.RejectedStatus)
            {
                await tx.CommitAsync(cancellationToken);
                return new RejectPairingRequestResult(request.Id, request.Status);
            }

            if (request.Status is DevicePairingRequest.CompletedStatus or DevicePairingRequest.ExpiredStatus)
            {
                throw Conflict();
            }

            if (request.Status == DevicePairingRequest.ApprovedStatus)
            {
                await RollbackApprovedRequestAsync(request, userId, now, cancellationToken);
            }

            request.MarkRejected(userId, now);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new RejectPairingRequestResult(request.Id, request.Status);
        });
    }

    public async Task<RevokeDeviceResult> RevokeDeviceAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var instance = await branchInstance.GetAsync(cancellationToken);
        var instanceId = BranchPairingSupport.RequireActiveBranch(instance);
        BranchPairingSupport.RequireAdmin(role);
        BranchPairingSupport.RequireCoherentTenantBranch(instance, tenantId, branchId);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);

            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM branch_devices WHERE id = {deviceId} FOR UPDATE",
                cancellationToken);
            var device = await db.BranchDevices
                .SingleOrDefaultAsync(x => x.Id == deviceId && x.BranchInstanceId == instanceId, cancellationToken);
            if (device is null)
            {
                throw new DevicePairingException(DevicePairingErrorCodes.DeviceNotFound, "Device was not found.");
            }

            var terminal = await db.BranchTerminals.SingleOrDefaultAsync(x => x.DeviceId == deviceId, cancellationToken);
            if (device.Status == BranchDevice.RevokedStatus)
            {
                await tx.CommitAsync(cancellationToken);
                return new RevokeDeviceResult(device.Id, terminal?.Id, device.Status, AlreadyRevoked: true);
            }

            device.Revoke(userId, now);
            if (terminal is not null && terminal.Status != BranchTerminal.DisabledStatus)
            {
                terminal.Disable();
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            deviceStatusResolver.Evict(instanceId, device.Id);
            return new RevokeDeviceResult(device.Id, terminal?.Id, device.Status, AlreadyRevoked: false);
        });
    }

    public async Task<DisableTerminalResult> DisableTerminalAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid terminalId,
        CancellationToken cancellationToken = default)
    {
        var instance = await branchInstance.GetAsync(cancellationToken);
        var instanceId = BranchPairingSupport.RequireActiveBranch(instance);
        BranchPairingSupport.RequireAdmin(role);
        BranchPairingSupport.RequireCoherentTenantBranch(instance, tenantId, branchId);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var terminal = await db.BranchTerminals
                .SingleOrDefaultAsync(x => x.Id == terminalId && x.BranchInstanceId == instanceId, cancellationToken);
            if (terminal is null)
            {
                throw new DevicePairingException(DevicePairingErrorCodes.DeviceNotFound, "Terminal was not found.");
            }

            var device = await db.BranchDevices
                .SingleOrDefaultAsync(x => x.Id == terminal.DeviceId && x.BranchInstanceId == instanceId, cancellationToken);
            if (device is null)
            {
                throw new DevicePairingException(DevicePairingErrorCodes.DeviceNotFound, "Device was not found.");
            }

            if (terminal.Status != BranchTerminal.DisabledStatus)
            {
                terminal.Disable();
            }

            device.BumpSecurityStamp();
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            deviceStatusResolver.Evict(instanceId, device.Id);
            return new DisableTerminalResult(terminal.Id, device.Id, terminal.Status, device.SecurityStamp);
        });
    }

    public async Task<RebindTerminalResult> RebindTerminalAsync(
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string role,
        Guid deviceId,
        string newTerminalName,
        CancellationToken cancellationToken = default)
    {
        var instance = await branchInstance.GetAsync(cancellationToken);
        var instanceId = BranchPairingSupport.RequireActiveBranch(instance);
        BranchPairingSupport.RequireAdmin(role);
        BranchPairingSupport.RequireCoherentTenantBranch(instance, tenantId, branchId);

        if (string.IsNullOrWhiteSpace(newTerminalName))
        {
            throw new DevicePairingException(DevicePairingErrorCodes.PairingInvalid, "Terminal name is required.");
        }

        var normalized = newTerminalName.Trim().ToUpperInvariant();

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);

            var device = await db.BranchDevices
                .SingleOrDefaultAsync(x => x.Id == deviceId && x.BranchInstanceId == instanceId, cancellationToken);
            if (device is null || device.Status != BranchDevice.ActiveStatus)
            {
                throw new DevicePairingException(DevicePairingErrorCodes.DeviceNotFound, "Active device was not found.");
            }

            var terminals = await db.BranchTerminals
                .Where(x => x.DeviceId == deviceId && x.BranchInstanceId == instanceId)
                .ToListAsync(cancellationToken);
            var current = terminals.SingleOrDefault(x => x.Status == BranchTerminal.ActiveStatus);
            if (current is null)
            {
                throw new DevicePairingException(DevicePairingErrorCodes.PairingConflict, "No active terminal to rebind.");
            }

            var nameTaken = await db.BranchTerminals.AnyAsync(
                x => x.BranchInstanceId == instanceId
                    && x.NormalizedName == normalized
                    && x.Status != BranchTerminal.DisabledStatus
                    && x.Id != current.Id,
                cancellationToken);
            if (nameTaken)
            {
                throw new DevicePairingException(DevicePairingErrorCodes.PairingConflict, "Terminal name is already in use.");
            }

            // One DeviceId → one terminal row (unique index). Rebind renames in place and bumps stamp.
            var previousId = current.Id;
            current.Rename(newTerminalName.Trim(), normalized);
            device.BumpSecurityStamp();

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            deviceStatusResolver.Evict(instanceId, device.Id);
            return new RebindTerminalResult(
                device.Id,
                previousId,
                current.Id,
                current.Name,
                device.SecurityStamp);
        });
    }

    public async Task<IReadOnlyList<PairedDeviceView>> ListDevicesAsync(
        Guid tenantId,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var instance = await branchInstance.GetAsync(cancellationToken);
        var instanceId = BranchPairingSupport.RequireActiveBranch(instance);
        BranchPairingSupport.RequireCoherentTenantBranch(instance, tenantId, branchId);

        var devices = await db.BranchDevices
            .AsNoTracking()
            .Where(x => x.BranchInstanceId == instanceId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return devices.ConvertAll(x => new PairedDeviceView(
            x.Id,
            x.PublicKeyFingerprint,
            DevicePairingFingerprint.ToShortDisplay(x.PublicKeyFingerprint),
            x.Status,
            x.CreatedAtUtc,
            x.PairedAtUtc,
            x.RevokedAtUtc));
    }

    public async Task<IReadOnlyList<BranchTerminalView>> ListTerminalsAsync(
        Guid tenantId,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var instance = await branchInstance.GetAsync(cancellationToken);
        var instanceId = BranchPairingSupport.RequireActiveBranch(instance);
        BranchPairingSupport.RequireCoherentTenantBranch(instance, tenantId, branchId);

        var terminals = await db.BranchTerminals
            .AsNoTracking()
            .Where(x => x.BranchInstanceId == instanceId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return terminals.ConvertAll(x => new BranchTerminalView(
            x.Id,
            x.DeviceId,
            x.Name,
            x.Status,
            x.CreatedAtUtc,
            x.ActivatedAtUtc));
    }

    private async Task<DevicePairingRequest?> LockRequestAsync(
        Guid pairingRequestId,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM device_pairing_requests WHERE id = {pairingRequestId} FOR UPDATE",
            cancellationToken);
        return await db.DevicePairingRequests
            .SingleOrDefaultAsync(x => x.Id == pairingRequestId && x.BranchInstanceId == instanceId, cancellationToken);
    }

    private async Task<DevicePairingChallenge> EnsureConfirmationChallengeAsync(
        DevicePairingRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var live = await db.DevicePairingChallenges
            .Where(x => x.PairingRequestId == request.Id
                && x.Phase == DevicePairingChallenge.ConfirmationPhase
                && x.ConsumedAtUtc == null
                && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (live is not null)
        {
            return live;
        }

        var challenge = DevicePairingChallenge.CreateConfirmation(
            Guid.CreateVersion7(),
            request.BranchInstanceId,
            request.Id,
            request.DeviceId,
            request.TerminalId!.Value,
            request.PublicKeyFingerprint,
            request.CredentialHash,
            request.PairingReceiptHash!,
            PairingSecret.Generate(),
            TruncateToSeconds(now.Add(Options.ConfirmationChallengeTtl)),
            timeProvider.GetUtcNow());
        db.DevicePairingChallenges.Add(challenge);
        return challenge;
    }

    private async Task InvalidateLiveConfirmationChallengesAsync(
        Guid pairingRequestId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var live = await db.DevicePairingChallenges
            .Where(x => x.PairingRequestId == pairingRequestId
                && x.Phase == DevicePairingChallenge.ConfirmationPhase
                && x.ConsumedAtUtc == null
                && x.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);
        foreach (var challenge in live)
        {
            challenge.MarkConsumed(now);
        }
    }

    private async Task RollbackApprovedRequestAsync(
        DevicePairingRequest request,
        Guid actingUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        receiptVault.Discard(request.Id);
        await InvalidateLiveConfirmationChallengesAsync(request.Id, now, cancellationToken);
        var device = await db.BranchDevices.SingleOrDefaultAsync(x => x.Id == request.DeviceId, cancellationToken);
        if (device is { Status: BranchDevice.PendingConfirmationStatus })
        {
            device.Revoke(actingUserId, now);
        }

        if (request.TerminalId is { } terminalId)
        {
            var terminal = await db.BranchTerminals.SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken);
            if (terminal is { Status: BranchTerminal.PendingConfirmationStatus })
            {
                terminal.Disable();
            }
        }
    }

    private async Task ExpireApprovedRequestAsync(
        DevicePairingRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.Status == DevicePairingRequest.ApprovedStatus)
        {
            await RollbackApprovedRequestAsync(request, request.ApprovedByUserId ?? Guid.Empty, now, cancellationToken);
        }

        request.MarkExpired();
    }

    private static PairingRequestView ToRequestView(DevicePairingRequest request) =>
        new(
            request.Id,
            request.DeviceId,
            DevicePairingFingerprint.ToShortDisplay(request.PublicKeyFingerprint),
            request.RequestedTerminalName,
            request.Status,
            request.RequestedAtUtc,
            request.ExpiresAtUtc,
            request.TerminalId,
            request.ApprovedAtUtc,
            request.RejectedAtUtc,
            request.CompletedAtUtc);

    private static bool IsExpirable(string status) =>
        status is DevicePairingRequest.PendingApprovalStatus or DevicePairingRequest.ApprovedStatus;

    private static DevicePairingException NotFound() =>
        new(DevicePairingErrorCodes.PairingRequestNotFound, "Pairing request was not found.");

    private static DevicePairingException Conflict() =>
        new(DevicePairingErrorCodes.PairingConflict, "Pairing request is not in an actionable state.");

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, TimeSpan.Zero);
}
