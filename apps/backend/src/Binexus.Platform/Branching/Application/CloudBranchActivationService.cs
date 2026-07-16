using Binexus.Platform.Branching.Activation;
using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Crypto;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Branching.Application;

/// <summary>
/// Cloud-side activation ceremony. Expiry comparisons use <see cref="TimeProvider"/> consistently
/// (not database wall-clock) for app and DB timestamps written in the same transaction.
/// </summary>
public sealed class CloudBranchActivationService(
    BinexusDbContext db,
    ITenantBranchLookup tenantBranchLookup,
    IOptions<CloudActivationOptions> options,
    TimeProvider timeProvider) : ICloudBranchActivationService
{
    public async Task<GenerateBranchActivationResult> GenerateAsync(
        Guid tenantId,
        Guid userId,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        if (!await tenantBranchLookup.ExistsForTenantAsync(tenantId, branchId, cancellationToken))
        {
            throw new BranchActivationException(
                BranchActivationErrorCodes.BranchNotFound,
                "Branch was not found for the tenant.");
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            await LazyExpireAsync(tenantId, branchId, now, cancellationToken);

            if (await db.CloudBranchInstances.AnyAsync(
                    x => x.TenantId == tenantId
                        && x.BranchId == branchId
                        && x.Status == CloudBranchInstance.ActiveStatus,
                    cancellationToken))
            {
                throw new BranchActivationException(
                    BranchActivationErrorCodes.BranchAlreadyActive,
                    "Branch already has an Active cloud instance.");
            }

            if (await db.CloudBranchInstances.AnyAsync(
                    x => x.TenantId == tenantId
                        && x.BranchId == branchId
                        && x.Status == CloudBranchInstance.ActivatingStatus,
                    cancellationToken))
            {
                throw new BranchActivationException(
                    BranchActivationErrorCodes.ActivationInProgress,
                    "Branch activation is already in progress.");
            }

            var liveReserved = await db.BranchActivations.AnyAsync(
                x => x.TenantId == tenantId
                    && x.BranchId == branchId
                    && x.Status == BranchActivation.ReservedStatus
                    && x.ReservedUntilUtc > now,
                cancellationToken);
            if (liveReserved)
            {
                throw new BranchActivationException(
                    BranchActivationErrorCodes.ActivationInProgress,
                    "A reserved activation is still live for this branch.");
            }

            var opens = await db.BranchActivations
                .Where(x => x.TenantId == tenantId
                    && x.BranchId == branchId
                    && x.Status == BranchActivation.OpenStatus)
                .ToListAsync(cancellationToken);
            foreach (var open in opens)
            {
                open.MarkExpired();
            }

            var code = BranchActivationCode.Generate();
            var activation = BranchActivation.CreateOpen(
                Guid.CreateVersion7(),
                tenantId,
                branchId,
                BranchActivationCode.Hash(code, options.Value.CodePepper),
                now.Add(options.Value.CodeTtl),
                userId,
                now);
            db.BranchActivations.Add(activation);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new GenerateBranchActivationResult(activation.Id, code, activation.ExpiresAtUtc);
        });
    }

    public async Task<CreateBranchActivationChallengeResult> CreateChallengeAsync(
        Guid branchInstanceId,
        string publicKey,
        string installationTokenHash,
        CancellationToken cancellationToken = default)
    {
        if (branchInstanceId == Guid.Empty
            || string.IsNullOrWhiteSpace(publicKey)
            || string.IsNullOrWhiteSpace(installationTokenHash)
            || installationTokenHash.Length != 64)
        {
            throw Invalid();
        }

        string fingerprint;
        try
        {
            fingerprint = EcdsaP256ActivationCrypto.Fingerprint(publicKey);
            using var _ = EcdsaP256ActivationCrypto.ImportPublicKey(publicKey);
        }
        catch (Exception)
        {
            throw Invalid();
        }

        var now = timeProvider.GetUtcNow();
        var nonce = InstallationToken.Generate();
        var expiresAtUtc = TruncateToSeconds(now.Add(options.Value.ChallengeTtl));
        var challenge = BranchActivationChallenge.Create(
            Guid.CreateVersion7(),
            branchInstanceId,
            fingerprint,
            installationTokenHash.ToLowerInvariant(),
            nonce,
            expiresAtUtc);
        db.BranchActivationChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateBranchActivationChallengeResult(
            challenge.Id,
            challenge.Nonce,
            challenge.ExpiresAtUtc,
            fingerprint);
    }

    public async Task<ExchangeBranchActivationResult> ExchangeAsync(
        string activationCode,
        Guid branchInstanceId,
        string publicKey,
        Guid challengeId,
        string signature,
        string installationTokenHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(activationCode)
            || branchInstanceId == Guid.Empty
            || string.IsNullOrWhiteSpace(publicKey)
            || challengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(signature)
            || string.IsNullOrWhiteSpace(installationTokenHash))
        {
            throw Invalid();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();

            string fingerprint;
            string codeHash;
            try
            {
                fingerprint = EcdsaP256ActivationCrypto.Fingerprint(publicKey);
                codeHash = BranchActivationCode.Hash(activationCode, options.Value.CodePepper);
            }
            catch (Exception)
            {
                throw Invalid();
            }

            var normalizedTokenHash = installationTokenHash.ToLowerInvariant();

            var challenge = await db.BranchActivationChallenges
                .SingleOrDefaultAsync(x => x.Id == challengeId, cancellationToken);
            if (challenge is null
                || challenge.ConsumedAtUtc is not null
                || challenge.ExpiresAtUtc <= now
                || challenge.BranchInstanceId != branchInstanceId
                || !string.Equals(challenge.PublicKeyFingerprint, fingerprint, StringComparison.Ordinal)
                || !InstallationToken.FixedTimeEqualsHash(challenge.InstallationTokenHash, normalizedTokenHash))
            {
                throw Invalid();
            }

            var payload = CanonicalActivationChallengeCodec.Encode(new CanonicalActivationChallenge(
                challenge.Id,
                challenge.BranchInstanceId,
                challenge.PublicKeyFingerprint,
                challenge.InstallationTokenHash,
                challenge.Nonce,
                challenge.ExpiresAtUtc));
            if (!EcdsaP256ActivationCrypto.Verify(payload, publicKey, signature))
            {
                throw Invalid();
            }

            challenge.MarkConsumed(now);

            // Lock the activation row within the open transaction, then load via EF mapping.
            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM branch_activations WHERE code_hash = {codeHash} FOR UPDATE",
                cancellationToken);
            var activation = await db.BranchActivations
                .SingleOrDefaultAsync(x => x.CodeHash == codeHash, cancellationToken);
            if (activation is null)
            {
                throw Invalid();
            }

            await LazyExpireAsync(activation.TenantId, activation.BranchId, now, cancellationToken);
            await db.Entry(activation).ReloadAsync(cancellationToken);

            if (activation.Status == BranchActivation.ReservedStatus
                && activation.AdoptedBranchInstanceId == branchInstanceId
                && string.Equals(activation.PublicKeyFingerprint, fingerprint, StringComparison.Ordinal)
                && activation.InstallationTokenHash is not null
                && InstallationToken.FixedTimeEqualsHash(activation.InstallationTokenHash, normalizedTokenHash)
                && activation.ReservedUntilUtc > now)
            {
                var retryReceipt = InstallationToken.Generate();
                activation.RotateReceipt(InstallationToken.Hash(retryReceipt));
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return new ExchangeBranchActivationResult(
                    activation.Id,
                    activation.TenantId,
                    activation.BranchId,
                    retryReceipt,
                    activation.ReservedUntilUtc!.Value);
            }

            if (activation.Status != BranchActivation.OpenStatus
                || activation.ExpiresAtUtc <= now
                || activation.IsLocked(now))
            {
                if (activation.Status == BranchActivation.OpenStatus && !activation.IsLocked(now))
                {
                    activation.RecordFailedAttempt(
                        options.Value.MaxFailedAttempts,
                        now,
                        options.Value.CodeTtl);
                    await db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                }

                throw Invalid();
            }

            if (await db.CloudBranchInstances.AnyAsync(
                    x => x.TenantId == activation.TenantId
                        && x.BranchId == activation.BranchId
                        && x.Status == CloudBranchInstance.ActiveStatus,
                    cancellationToken))
            {
                throw new BranchActivationException(
                    BranchActivationErrorCodes.BranchAlreadyActive,
                    "Branch already has an Active cloud instance.");
            }

            var receipt = InstallationToken.Generate();
            var reservedUntil = now.Add(options.Value.ReservedDuration);
            activation.MarkReserved(
                branchInstanceId,
                fingerprint,
                normalizedTokenHash,
                InstallationToken.Hash(receipt),
                now,
                reservedUntil);

            var cloud = await db.CloudBranchInstances
                .SingleOrDefaultAsync(x => x.BranchInstanceId == branchInstanceId, cancellationToken);
            if (cloud is null)
            {
                db.CloudBranchInstances.Add(CloudBranchInstance.CreateActivating(
                    branchInstanceId,
                    activation.TenantId,
                    activation.BranchId,
                    normalizedTokenHash,
                    publicKey,
                    fingerprint,
                    activation.Id,
                    now,
                    reservedUntil));
            }
            else if (cloud.Status == CloudBranchInstance.ActiveStatus)
            {
                throw new BranchActivationException(
                    BranchActivationErrorCodes.BranchAlreadyActive,
                    "Branch already has an Active cloud instance.");
            }
            else
            {
                cloud.RefreshActivating(
                    activation.Id,
                    normalizedTokenHash,
                    publicKey,
                    fingerprint,
                    reservedUntil);
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new ExchangeBranchActivationResult(
                activation.Id,
                activation.TenantId,
                activation.BranchId,
                receipt,
                reservedUntil);
        });
    }

    public async Task<ResumeBranchActivationResult> ResumeAsync(
        Guid activationId,
        Guid branchInstanceId,
        string publicKey,
        Guid challengeId,
        string signature,
        string installationTokenHash,
        CancellationToken cancellationToken = default)
    {
        if (activationId == Guid.Empty
            || branchInstanceId == Guid.Empty
            || string.IsNullOrWhiteSpace(publicKey)
            || challengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(signature)
            || string.IsNullOrWhiteSpace(installationTokenHash))
        {
            throw Invalid();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            string fingerprint;
            try
            {
                fingerprint = EcdsaP256ActivationCrypto.Fingerprint(publicKey);
            }
            catch (Exception)
            {
                throw Invalid();
            }

            var normalizedTokenHash = installationTokenHash.ToLowerInvariant();
            var challenge = await db.BranchActivationChallenges
                .SingleOrDefaultAsync(x => x.Id == challengeId, cancellationToken);
            if (challenge is null
                || challenge.ConsumedAtUtc is not null
                || challenge.ExpiresAtUtc <= now
                || challenge.BranchInstanceId != branchInstanceId
                || !string.Equals(challenge.PublicKeyFingerprint, fingerprint, StringComparison.Ordinal)
                || !InstallationToken.FixedTimeEqualsHash(challenge.InstallationTokenHash, normalizedTokenHash))
            {
                throw Invalid();
            }

            var payload = CanonicalActivationChallengeCodec.Encode(new CanonicalActivationChallenge(
                challenge.Id,
                challenge.BranchInstanceId,
                challenge.PublicKeyFingerprint,
                challenge.InstallationTokenHash,
                challenge.Nonce,
                challenge.ExpiresAtUtc));
            if (!EcdsaP256ActivationCrypto.Verify(payload, publicKey, signature))
            {
                throw Invalid();
            }

            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM branch_activations WHERE id = {activationId} FOR UPDATE",
                cancellationToken);
            var activation = await db.BranchActivations
                .SingleOrDefaultAsync(x => x.Id == activationId, cancellationToken);
            if (activation is null
                || activation.Status != BranchActivation.ReservedStatus
                || activation.ReservedUntilUtc is null
                || activation.ReservedUntilUtc <= now
                || activation.AdoptedBranchInstanceId != branchInstanceId
                || !string.Equals(activation.PublicKeyFingerprint, fingerprint, StringComparison.Ordinal)
                || activation.InstallationTokenHash is null
                || !InstallationToken.FixedTimeEqualsHash(activation.InstallationTokenHash, normalizedTokenHash))
            {
                throw Invalid();
            }

            challenge.MarkConsumed(now);
            var receipt = InstallationToken.Generate();
            activation.RotateReceipt(InstallationToken.Hash(receipt));
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new ResumeBranchActivationResult(
                activation.Id,
                activation.TenantId,
                activation.BranchId,
                receipt,
                activation.ReservedUntilUtc.Value);
        });
    }

    public async Task<ConfirmBranchActivationResult> ConfirmAsync(
        Guid activationId,
        string receipt,
        string installationToken,
        CancellationToken cancellationToken = default)
    {
        if (activationId == Guid.Empty
            || string.IsNullOrWhiteSpace(receipt)
            || string.IsNullOrWhiteSpace(installationToken))
        {
            throw Invalid();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var activation = await db.BranchActivations
                .SingleOrDefaultAsync(x => x.Id == activationId, cancellationToken);
            if (activation is null)
            {
                throw Invalid();
            }

            var cloud = activation.AdoptedBranchInstanceId is { } instanceId
                ? await db.CloudBranchInstances.SingleOrDefaultAsync(
                    x => x.BranchInstanceId == instanceId,
                    cancellationToken)
                : null;

            if (activation.Status == BranchActivation.ConsumedStatus
                && cloud is { Status: CloudBranchInstance.ActiveStatus }
                && activation.InstallationTokenHash is not null
                && InstallationToken.FixedTimeEqualsHash(
                    activation.InstallationTokenHash,
                    InstallationToken.Hash(installationToken))
                && activation.ActivationReceiptHash is not null
                && InstallationToken.FixedTimeEqualsHash(
                    activation.ActivationReceiptHash,
                    InstallationToken.Hash(receipt)))
            {
                await tx.CommitAsync(cancellationToken);
                return new ConfirmBranchActivationResult(
                    activation.Id,
                    activation.TenantId,
                    activation.BranchId,
                    cloud.BranchInstanceId,
                    cloud.ActivatedAtUtc ?? now,
                    AlreadyActive: true);
            }

            if (activation.Status != BranchActivation.ReservedStatus
                || activation.ReservedUntilUtc is null
                || activation.ReservedUntilUtc <= now
                || activation.ActivationReceiptHash is null
                || activation.InstallationTokenHash is null
                || cloud is null
                || cloud.Status != CloudBranchInstance.ActivatingStatus)
            {
                throw Invalid();
            }

            if (!InstallationToken.FixedTimeEqualsHash(
                    activation.ActivationReceiptHash,
                    InstallationToken.Hash(receipt))
                || !InstallationToken.FixedTimeEqualsHash(
                    activation.InstallationTokenHash,
                    InstallationToken.Hash(installationToken)))
            {
                throw Invalid();
            }

            activation.MarkConsumed(now);
            cloud.MarkActive(now);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new ConfirmBranchActivationResult(
                activation.Id,
                activation.TenantId,
                activation.BranchId,
                cloud.BranchInstanceId,
                now,
                AlreadyActive: false);
        });
    }

    private async Task LazyExpireAsync(
        Guid tenantId,
        Guid branchId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activations = await db.BranchActivations
            .Where(x => x.TenantId == tenantId && x.BranchId == branchId
                && (x.Status == BranchActivation.OpenStatus || x.Status == BranchActivation.ReservedStatus))
            .ToListAsync(cancellationToken);
        foreach (var item in activations)
        {
            if (item.Status == BranchActivation.OpenStatus && item.ExpiresAtUtc <= now)
            {
                item.MarkExpired();
            }
            else if (item.Status == BranchActivation.ReservedStatus
                && (item.ReservedUntilUtc is null || item.ReservedUntilUtc <= now))
            {
                item.MarkExpired();
            }
        }

        var activating = await db.CloudBranchInstances
            .Where(x => x.TenantId == tenantId
                && x.BranchId == branchId
                && x.Status == CloudBranchInstance.ActivatingStatus
                && x.ActivatingUntilUtc != null
                && x.ActivatingUntilUtc <= now)
            .ToListAsync(cancellationToken);
        if (activating.Count > 0)
        {
            db.CloudBranchInstances.RemoveRange(activating);
        }

        if (activations.Count > 0 || activating.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static BranchActivationException Invalid() =>
        new(BranchActivationErrorCodes.ActivationInvalid, "Activation request is invalid.");

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, TimeSpan.Zero);
}
