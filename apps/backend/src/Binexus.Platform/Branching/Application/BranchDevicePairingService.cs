using System.Security.Cryptography;
using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Crypto;
using Binexus.Platform.Branching.Pairing;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Branching.Application;

/// <summary>
/// Anonymous, Device-driven pairing ceremony. Proof-of-possession is necessary but never sufficient:
/// a request only becomes a Device/Terminal after explicit admin approval. The raw device credential
/// never crosses the wire — the Device signs an ECDSA challenge that binds its credential hash.
/// </summary>
public sealed class BranchDevicePairingService(
    BinexusDbContext db,
    IBranchInstanceAccessor branchInstance,
    IPairingReceiptVault receiptVault,
    IOptions<DevicePairingOptions> options,
    TimeProvider timeProvider) : IBranchDevicePairingService
{
    private DevicePairingOptions Options => options.Value;

    public async Task<CreateExchangeChallengeResult> CreateExchangeChallengeAsync(
        Guid pairingSessionId,
        string pairingCode,
        Guid deviceId,
        string publicKey,
        string credentialHash,
        CancellationToken cancellationToken = default)
    {
        var instanceId = BranchPairingSupport.RequireActiveBranch(await branchInstance.GetAsync(cancellationToken));
        if (pairingSessionId == Guid.Empty
            || deviceId == Guid.Empty
            || string.IsNullOrWhiteSpace(pairingCode)
            || string.IsNullOrWhiteSpace(publicKey)
            || !IsHash(credentialHash))
        {
            throw BranchPairingSupport.Invalid();
        }

        var fingerprint = SafeFingerprint(publicKey);
        var normalizedCredentialHash = credentialHash.ToLowerInvariant();

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);

            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM device_pairing_sessions WHERE id = {pairingSessionId} FOR UPDATE",
                cancellationToken);
            var session = await db.DevicePairingSessions
                .SingleOrDefaultAsync(x => x.Id == pairingSessionId, cancellationToken);
            if (session is null || session.BranchInstanceId != instanceId)
            {
                throw BranchPairingSupport.Invalid();
            }

            if (session.Status == DevicePairingSession.OpenStatus && session.ExpiresAtUtc <= now)
            {
                session.MarkExpired();
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                throw BranchPairingSupport.Invalid();
            }

            if (session.Status == DevicePairingSession.ExpiredStatus)
            {
                throw BranchPairingSupport.Invalid();
            }

            if (session.IsLocked(now))
            {
                throw new DevicePairingException(DevicePairingErrorCodes.PairingLocked, "Too many attempts.");
            }

            if (!PairingCode.FixedTimeEqualsHash(
                    session.CodeHash,
                    PairingCode.Hash(pairingCode, Options.CodePepper)))
            {
                session.RecordFailedAttempt(Options.MaxFailedAttempts, now, Options.LockoutDuration);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                throw BranchPairingSupport.Invalid();
            }

            var challenge = DevicePairingChallenge.CreateExchange(
                Guid.CreateVersion7(),
                instanceId,
                pairingSessionId,
                deviceId,
                fingerprint,
                normalizedCredentialHash,
                PairingSecret.Generate(),
                TruncateToSeconds(now.Add(Options.ExchangeChallengeTtl)),
                timeProvider.GetUtcNow());
            db.DevicePairingChallenges.Add(challenge);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new CreateExchangeChallengeResult(challenge.Id, instanceId, challenge.Nonce, challenge.ExpiresAtUtc);
        });
    }

    public async Task<PairingExchangeResult> ExchangeAsync(
        Guid pairingSessionId,
        string pairingCode,
        Guid deviceId,
        string publicKey,
        Guid challengeId,
        string signature,
        string credentialHash,
        string terminalName,
        CancellationToken cancellationToken = default)
    {
        var instanceId = BranchPairingSupport.RequireActiveBranch(await branchInstance.GetAsync(cancellationToken));
        if (pairingSessionId == Guid.Empty
            || deviceId == Guid.Empty
            || challengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(pairingCode)
            || string.IsNullOrWhiteSpace(publicKey)
            || string.IsNullOrWhiteSpace(signature)
            || !IsHash(credentialHash)
            || string.IsNullOrWhiteSpace(terminalName))
        {
            throw BranchPairingSupport.Invalid();
        }

        var fingerprint = SafeFingerprint(publicKey);
        var normalizedCredentialHash = credentialHash.ToLowerInvariant();
        string terminalDisplayName;
        string terminalNormalizedName;
        try
        {
            terminalDisplayName = TerminalName.Validate(terminalName);
            terminalNormalizedName = TerminalName.Normalize(terminalName);
        }
        catch (FormatException)
        {
            throw BranchPairingSupport.Invalid();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);

            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM device_pairing_sessions WHERE id = {pairingSessionId} FOR UPDATE",
                cancellationToken);
            var session = await db.DevicePairingSessions
                .SingleOrDefaultAsync(x => x.Id == pairingSessionId, cancellationToken);
            if (session is null || session.BranchInstanceId != instanceId)
            {
                throw BranchPairingSupport.Invalid();
            }

            var challenge = await db.DevicePairingChallenges
                .SingleOrDefaultAsync(x => x.Id == challengeId, cancellationToken);
            if (!IsUsableExchangeChallenge(challenge, instanceId, pairingSessionId, deviceId, fingerprint, normalizedCredentialHash, now))
            {
                throw BranchPairingSupport.Invalid();
            }

            var payload = CanonicalDevicePairingChallengeCodec.EncodeExchange(
                new CanonicalDevicePairingExchangeChallenge(
                    challenge!.Id,
                    challenge.BranchInstanceId,
                    session.Id,
                    deviceId,
                    challenge.PublicKeyFingerprint,
                    challenge.CredentialHash,
                    challenge.Nonce,
                    challenge.ExpiresAtUtc));
            if (!EcdsaP256ActivationCrypto.Verify(payload, publicKey, signature))
            {
                throw BranchPairingSupport.Invalid();
            }

            challenge.MarkConsumed(now);

            var existing = await db.DevicePairingRequests.SingleOrDefaultAsync(
                x => x.PairingSessionId == pairingSessionId && x.DeviceId == deviceId,
                cancellationToken);
            if (existing is not null)
            {
                if (existing.Status != DevicePairingRequest.PendingApprovalStatus
                    || !string.Equals(existing.PublicKeyFingerprint, fingerprint, StringComparison.Ordinal)
                    || !string.Equals(existing.CredentialHash, normalizedCredentialHash, StringComparison.Ordinal))
                {
                    throw BranchPairingSupport.Invalid();
                }

                if (existing.ExpiresAtUtc <= now)
                {
                    existing.MarkExpired();
                    await db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                    throw BranchPairingSupport.Invalid();
                }

                var rotated = PairingSecret.Generate();
                existing.RotateStatusToken(
                    PairingSecret.Hash(rotated),
                    TruncateToSeconds(now.Add(Options.StatusTokenTtl)));
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return ToExchangeResult(existing, rotated);
            }

            if (session.Status != DevicePairingSession.OpenStatus || session.ExpiresAtUtc <= now)
            {
                throw BranchPairingSupport.Invalid();
            }

            if (!PairingCode.FixedTimeEqualsHash(
                    session.CodeHash,
                    PairingCode.Hash(pairingCode, Options.CodePepper)))
            {
                session.RecordFailedAttempt(Options.MaxFailedAttempts, now, Options.LockoutDuration);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                throw BranchPairingSupport.Invalid();
            }

            await EnsureCryptographicMaterialUnusedAsync(instanceId, deviceId, fingerprint, normalizedCredentialHash, cancellationToken);

            var statusToken = PairingSecret.Generate();
            var appNow = timeProvider.GetUtcNow();
            var request = DevicePairingRequest.CreatePending(
                Guid.CreateVersion7(),
                pairingSessionId,
                instanceId,
                deviceId,
                publicKey,
                fingerprint,
                normalizedCredentialHash,
                terminalDisplayName,
                terminalNormalizedName,
                PairingSecret.Hash(statusToken),
                TruncateToSeconds(now.Add(Options.StatusTokenTtl)),
                appNow,
                TruncateToSeconds(now.Add(Options.RequestTtl)));
            db.DevicePairingRequests.Add(request);
            session.MarkConsumed(now);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return ToExchangeResult(request, statusToken);
        });
    }

    public async Task<PairingStatusResult> GetStatusAsync(
        Guid pairingRequestId,
        string pairingStatusToken,
        CancellationToken cancellationToken = default)
    {
        var instanceId = BranchPairingSupport.RequireActiveBranch(await branchInstance.GetAsync(cancellationToken));
        if (pairingRequestId == Guid.Empty || string.IsNullOrWhiteSpace(pairingStatusToken))
        {
            throw BranchPairingSupport.Invalid();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);

            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM device_pairing_requests WHERE id = {pairingRequestId} FOR UPDATE",
                cancellationToken);
            var request = await db.DevicePairingRequests
                .SingleOrDefaultAsync(x => x.Id == pairingRequestId, cancellationToken);
            if (request is null
                || request.BranchInstanceId != instanceId
                || request.StatusTokenExpiresAtUtc <= now
                || !PairingSecret.FixedTimeEqualsHash(request.StatusTokenHash, PairingSecret.Hash(pairingStatusToken)))
            {
                throw BranchPairingSupport.Invalid();
            }

            if (IsExpirable(request.Status) && request.ExpiresAtUtc <= now)
            {
                await ExpireRequestAsync(request, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return new PairingStatusResult(request.Id, request.Status, instanceId, null, null, null, null, null);
            }

            PairingStatusResult result;
            if (request.Status == DevicePairingRequest.ApprovedStatus)
            {
                var confirmation = await EnsureConfirmationChallengeAsync(request, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                result = new PairingStatusResult(
                    request.Id,
                    request.Status,
                    instanceId,
                    request.TerminalId,
                    confirmation.Id,
                    confirmation.Nonce,
                    confirmation.ExpiresAtUtc,
                    receiptVault.Consume(request.Id));
            }
            else
            {
                result = new PairingStatusResult(request.Id, request.Status, instanceId, request.TerminalId, null, null, null, null);
            }

            await tx.CommitAsync(cancellationToken);
            return result;
        });
    }

    public async Task<CreateReceiptReissueChallengeResult> CreateReceiptReissueChallengeAsync(
        Guid pairingRequestId,
        string pairingStatusToken,
        CancellationToken cancellationToken = default)
    {
        var instanceId = BranchPairingSupport.RequireActiveBranch(await branchInstance.GetAsync(cancellationToken));
        if (pairingRequestId == Guid.Empty || string.IsNullOrWhiteSpace(pairingStatusToken))
        {
            throw BranchPairingSupport.Invalid();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);
            var request = await LockAndValidateStatusTokenAsync(
                pairingRequestId, instanceId, pairingStatusToken, now, cancellationToken);

            if (request.Status != DevicePairingRequest.ApprovedStatus || request.TerminalId is null || request.PairingReceiptHash is null)
            {
                throw BranchPairingSupport.Invalid();
            }

            if (request.ExpiresAtUtc <= now)
            {
                await ExpireRequestAsync(request, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                throw BranchPairingSupport.Invalid();
            }

            var challenge = await EnsureReceiptReissueChallengeAsync(request, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new CreateReceiptReissueChallengeResult(
                challenge.Id, instanceId, challenge.Nonce, challenge.ExpiresAtUtc);
        });
    }

    public async Task<ReissuePairingReceiptResult> ReissueReceiptAsync(
        Guid pairingRequestId,
        string pairingStatusToken,
        Guid reissueChallengeId,
        string signature,
        CancellationToken cancellationToken = default)
    {
        var instanceId = BranchPairingSupport.RequireActiveBranch(await branchInstance.GetAsync(cancellationToken));
        if (pairingRequestId == Guid.Empty
            || reissueChallengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(pairingStatusToken)
            || string.IsNullOrWhiteSpace(signature))
        {
            throw BranchPairingSupport.Invalid();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);
            var request = await LockAndValidateStatusTokenAsync(
                pairingRequestId, instanceId, pairingStatusToken, now, cancellationToken);

            if (request.Status != DevicePairingRequest.ApprovedStatus || request.TerminalId is null || request.PairingReceiptHash is null)
            {
                throw BranchPairingSupport.Invalid();
            }

            if (request.ExpiresAtUtc <= now)
            {
                await ExpireRequestAsync(request, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                throw BranchPairingSupport.Invalid();
            }

            var reissueChallenge = await db.DevicePairingChallenges
                .SingleOrDefaultAsync(x => x.Id == reissueChallengeId, cancellationToken);
            if (!IsUsableReceiptReissueChallenge(reissueChallenge, request, now))
            {
                throw BranchPairingSupport.Invalid();
            }

            var payload = CanonicalDevicePairingChallengeCodec.EncodeReceiptReissue(
                new CanonicalDevicePairingReceiptReissueChallenge(
                    reissueChallenge!.Id,
                    request.Id,
                    request.BranchInstanceId,
                    request.DeviceId,
                    reissueChallenge.PublicKeyFingerprint,
                    reissueChallenge.CredentialHash,
                    reissueChallenge.Nonce,
                    reissueChallenge.ExpiresAtUtc));
            if (!EcdsaP256ActivationCrypto.Verify(payload, request.PublicKey, signature))
            {
                throw BranchPairingSupport.Invalid();
            }

            reissueChallenge.MarkConsumed(now);
            await InvalidateLiveConfirmationChallengesAsync(request.Id, now, cancellationToken);
            receiptVault.Discard(request.Id);

            var rawReceipt = PairingSecret.Generate();
            request.RotatePairingReceipt(PairingSecret.Hash(rawReceipt));

            var confirmation = DevicePairingChallenge.CreateConfirmation(
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
            db.DevicePairingChallenges.Add(confirmation);
            receiptVault.Store(request.Id, rawReceipt);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new ReissuePairingReceiptResult(
                request.Id,
                request.BranchInstanceId,
                request.TerminalId!.Value,
                rawReceipt,
                confirmation.Id,
                confirmation.Nonce,
                confirmation.ExpiresAtUtc);
        });
    }

    public async Task<PairingConfirmResult> ConfirmAsync(
        Guid pairingRequestId,
        Guid confirmationChallengeId,
        string signature,
        string pairingReceipt,
        string pairingStatusToken,
        CancellationToken cancellationToken = default)
    {
        var instanceId = BranchPairingSupport.RequireActiveBranch(await branchInstance.GetAsync(cancellationToken));
        if (pairingRequestId == Guid.Empty
            || confirmationChallengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(signature)
            || string.IsNullOrWhiteSpace(pairingReceipt)
            || string.IsNullOrWhiteSpace(pairingStatusToken))
        {
            throw BranchPairingSupport.Invalid();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);

            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM device_pairing_requests WHERE id = {pairingRequestId} FOR UPDATE",
                cancellationToken);
            var request = await db.DevicePairingRequests
                .SingleOrDefaultAsync(x => x.Id == pairingRequestId, cancellationToken);
            if (request is null
                || request.BranchInstanceId != instanceId
                || request.StatusTokenExpiresAtUtc <= now
                || !PairingSecret.FixedTimeEqualsHash(request.StatusTokenHash, PairingSecret.Hash(pairingStatusToken)))
            {
                throw BranchPairingSupport.Invalid();
            }

            if (request.Status == DevicePairingRequest.CompletedStatus)
            {
                var completedDevice = await db.BranchDevices.SingleOrDefaultAsync(x => x.Id == request.DeviceId, cancellationToken);
                var completedTerminal = request.TerminalId is { } tid
                    ? await db.BranchTerminals.SingleOrDefaultAsync(x => x.Id == tid, cancellationToken)
                    : null;
                if (completedDevice is { Status: BranchDevice.ActiveStatus } && completedTerminal is { Status: BranchTerminal.ActiveStatus })
                {
                    await tx.CommitAsync(cancellationToken);
                    return new PairingConfirmResult(request.Id, completedDevice.Id, completedTerminal.Id, request.Status, AlreadyActive: true);
                }

                throw BranchPairingSupport.Invalid();
            }

            if (request.Status != DevicePairingRequest.ApprovedStatus || request.TerminalId is null || request.PairingReceiptHash is null)
            {
                throw BranchPairingSupport.Invalid();
            }

            if (request.ExpiresAtUtc <= now)
            {
                await ExpireRequestAsync(request, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                throw BranchPairingSupport.Invalid();
            }

            var challenge = await db.DevicePairingChallenges
                .SingleOrDefaultAsync(x => x.Id == confirmationChallengeId, cancellationToken);
            if (!IsUsableConfirmationChallenge(challenge, request, now))
            {
                throw BranchPairingSupport.Invalid();
            }

            if (!PairingSecret.FixedTimeEqualsHash(request.PairingReceiptHash, PairingSecret.Hash(pairingReceipt)))
            {
                throw BranchPairingSupport.Invalid();
            }

            var payload = CanonicalDevicePairingChallengeCodec.EncodeConfirmation(
                new CanonicalDevicePairingConfirmChallenge(
                    challenge!.Id,
                    request.Id,
                    request.BranchInstanceId,
                    request.DeviceId,
                    request.TerminalId!.Value,
                    challenge.PublicKeyFingerprint,
                    challenge.CredentialHash,
                    challenge.PairingReceiptHash!,
                    challenge.Nonce,
                    challenge.ExpiresAtUtc));
            if (!EcdsaP256ActivationCrypto.Verify(payload, request.PublicKey, signature))
            {
                throw BranchPairingSupport.Invalid();
            }

            var device = await db.BranchDevices.SingleOrDefaultAsync(x => x.Id == request.DeviceId, cancellationToken);
            var terminal = await db.BranchTerminals.SingleOrDefaultAsync(x => x.Id == request.TerminalId!.Value, cancellationToken);
            if (device is not { Status: BranchDevice.PendingConfirmationStatus }
                || terminal is not { Status: BranchTerminal.PendingConfirmationStatus })
            {
                throw BranchPairingSupport.Invalid();
            }

            challenge.MarkConsumed(now);
            var appNow = timeProvider.GetUtcNow();
            device.MarkActive(appNow);
            terminal.MarkActive(appNow);
            request.MarkCompleted(appNow);
            receiptVault.Discard(request.Id);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new PairingConfirmResult(request.Id, device.Id, terminal.Id, request.Status, AlreadyActive: false);
        });
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

    private async Task<DevicePairingChallenge> EnsureReceiptReissueChallengeAsync(
        DevicePairingRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var live = await db.DevicePairingChallenges
            .Where(x => x.PairingRequestId == request.Id
                && x.Phase == DevicePairingChallenge.ReceiptReissuePhase
                && x.ConsumedAtUtc == null
                && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (live is not null)
        {
            return live;
        }

        var challenge = DevicePairingChallenge.CreateReceiptReissue(
            Guid.CreateVersion7(),
            request.BranchInstanceId,
            request.Id,
            request.DeviceId,
            request.PublicKeyFingerprint,
            request.CredentialHash,
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

    private async Task<DevicePairingRequest> LockAndValidateStatusTokenAsync(
        Guid pairingRequestId,
        Guid instanceId,
        string pairingStatusToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM device_pairing_requests WHERE id = {pairingRequestId} FOR UPDATE",
            cancellationToken);
        var request = await db.DevicePairingRequests
            .SingleOrDefaultAsync(x => x.Id == pairingRequestId, cancellationToken);
        if (request is null
            || request.BranchInstanceId != instanceId
            || request.StatusTokenExpiresAtUtc <= now
            || !PairingSecret.FixedTimeEqualsHash(request.StatusTokenHash, PairingSecret.Hash(pairingStatusToken)))
        {
            throw BranchPairingSupport.Invalid();
        }

        return request;
    }

    private async Task ExpireRequestAsync(DevicePairingRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        request.MarkExpired();
        receiptVault.Discard(request.Id);
        await InvalidateLiveConfirmationChallengesAsync(request.Id, now, cancellationToken);

        if (request.TerminalId is { } terminalId)
        {
            var device = await db.BranchDevices.SingleOrDefaultAsync(x => x.Id == request.DeviceId, cancellationToken);
            if (device is { Status: BranchDevice.PendingConfirmationStatus })
            {
                device.Revoke(request.ApprovedByUserId ?? Guid.Empty, now);
            }

            var terminal = await db.BranchTerminals.SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken);
            if (terminal is { Status: BranchTerminal.PendingConfirmationStatus })
            {
                terminal.Disable();
            }
        }
    }

    private async Task EnsureCryptographicMaterialUnusedAsync(
        Guid instanceId,
        Guid deviceId,
        string fingerprint,
        string credentialHash,
        CancellationToken cancellationToken)
    {
        var clash = await db.BranchDevices.AnyAsync(
            x => x.BranchInstanceId == instanceId
                && (x.Id == deviceId
                    || x.PublicKeyFingerprint == fingerprint
                    || x.CredentialHash == credentialHash),
            cancellationToken);
        if (clash)
        {
            throw BranchPairingSupport.Invalid();
        }
    }

    private static bool IsUsableExchangeChallenge(
        DevicePairingChallenge? challenge,
        Guid instanceId,
        Guid sessionId,
        Guid deviceId,
        string fingerprint,
        string credentialHash,
        DateTimeOffset now) =>
        challenge is not null
        && challenge.Phase == DevicePairingChallenge.ExchangePhase
        && challenge.ConsumedAtUtc is null
        && challenge.ExpiresAtUtc > now
        && challenge.BranchInstanceId == instanceId
        && challenge.PairingSessionId == sessionId
        && challenge.DeviceId == deviceId
        && string.Equals(challenge.PublicKeyFingerprint, fingerprint, StringComparison.Ordinal)
        && string.Equals(challenge.CredentialHash, credentialHash, StringComparison.Ordinal);

    private static bool IsUsableConfirmationChallenge(
        DevicePairingChallenge? challenge,
        DevicePairingRequest request,
        DateTimeOffset now) =>
        challenge is not null
        && challenge.Phase == DevicePairingChallenge.ConfirmationPhase
        && challenge.ConsumedAtUtc is null
        && challenge.ExpiresAtUtc > now
        && challenge.PairingRequestId == request.Id
        && challenge.DeviceId == request.DeviceId
        && challenge.TerminalId == request.TerminalId
        && string.Equals(challenge.PublicKeyFingerprint, request.PublicKeyFingerprint, StringComparison.Ordinal)
        && string.Equals(challenge.CredentialHash, request.CredentialHash, StringComparison.Ordinal)
        && challenge.PairingReceiptHash is not null
        && string.Equals(challenge.PairingReceiptHash, request.PairingReceiptHash, StringComparison.Ordinal);

    private static bool IsUsableReceiptReissueChallenge(
        DevicePairingChallenge? challenge,
        DevicePairingRequest request,
        DateTimeOffset now) =>
        challenge is not null
        && challenge.Phase == DevicePairingChallenge.ReceiptReissuePhase
        && challenge.ConsumedAtUtc is null
        && challenge.ExpiresAtUtc > now
        && challenge.PairingRequestId == request.Id
        && challenge.DeviceId == request.DeviceId
        && string.Equals(challenge.PublicKeyFingerprint, request.PublicKeyFingerprint, StringComparison.Ordinal)
        && string.Equals(challenge.CredentialHash, request.CredentialHash, StringComparison.Ordinal);

    private static PairingExchangeResult ToExchangeResult(DevicePairingRequest request, string statusToken) =>
        new(
            request.Id,
            DevicePairingFingerprint.ToShortDisplay(request.PublicKeyFingerprint),
            request.Status,
            statusToken,
            request.ExpiresAtUtc);

    private static bool IsExpirable(string status) =>
        status is DevicePairingRequest.PendingApprovalStatus or DevicePairingRequest.ApprovedStatus;

    private static bool IsHash(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 64;

    private static string SafeFingerprint(string publicKey)
    {
        try
        {
            using var _ = EcdsaP256ActivationCrypto.ImportPublicKey(publicKey);
            return EcdsaP256ActivationCrypto.Fingerprint(publicKey);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or CryptographicException)
        {
            throw BranchPairingSupport.Invalid();
        }
    }

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, TimeSpan.Zero);
}
