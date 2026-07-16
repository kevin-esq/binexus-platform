using System.Security.Cryptography;
using Binexus.Platform.Branching.Activation;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Credentials;
using Binexus.Platform.Branching.Crypto;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Platform.Branching.Application;

public interface IBranchActivationOrchestrator
{
    Task ActivateAsync(string activationCode, CancellationToken cancellationToken = default);

    Task FinalizeAsync(CancellationToken cancellationToken = default);

    Task<BranchActivationStage> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class BranchActivationOrchestrator(
    IBranchInstanceAccessor branchInstanceAccessor,
    BranchInstanceMemoryStore memoryStore,
    IBranchCredentialStore credentialStore,
    ICloudActivationClient cloudClient,
    BinexusDbContext db,
    TimeProvider timeProvider) : IBranchActivationOrchestrator
{
    public async Task ActivateAsync(string activationCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationCode);
        var local = await branchInstanceAccessor.GetAsync(cancellationToken);
        if (local.Status == BranchServerStatus.Active)
        {
            // Idempotent success — local Active already binds tenant/branch.
            return;
        }

        var existing = await credentialStore.GetSessionAsync(cancellationToken);
        if (existing is not null
            && existing.Stage is BranchActivationStage.Reserved
                or BranchActivationStage.CloudConfirmed
                or BranchActivationStage.FinalizeRequired
            && existing.ActivationId is not null
            && existing.Receipt is not null
            && existing.InstallationToken is not null
            && existing.TenantId is not null
            && existing.BranchId is not null)
        {
            if (existing.Stage == BranchActivationStage.Reserved
                && existing.PrivateKeyPkcs8Base64Url is not null)
            {
                var challenge = await cloudClient.CreateChallengeAsync(
                    local.Id,
                    existing.PublicKey,
                    existing.InstallationTokenHash,
                    cancellationToken);
                var payload = CanonicalActivationChallengeCodec.Encode(new CanonicalActivationChallenge(
                    challenge.ChallengeId,
                    local.Id,
                    existing.PublicKeyFingerprint,
                    existing.InstallationTokenHash,
                    challenge.Nonce,
                    challenge.ExpiresAtUtc));
                var signature = EcdsaP256ActivationCrypto.Sign(
                    payload,
                    Base64Url.Decode(existing.PrivateKeyPkcs8Base64Url));
                var resume = await cloudClient.ResumeAsync(
                    existing.ActivationId.Value,
                    local.Id,
                    existing.PublicKey,
                    challenge.ChallengeId,
                    signature,
                    existing.InstallationTokenHash,
                    cancellationToken);
                existing = existing with
                {
                    Receipt = resume.Receipt,
                    UpdatedAtUtc = timeProvider.GetUtcNow(),
                };
                await credentialStore.SaveSessionAsync(existing, cancellationToken);
            }

            await FinalizeAsync(cancellationToken);
            return;
        }

        var materials = ResolveOrCreateMaterials(local.Id, existing);
        try
        {
            var attemptId = existing?.LocalAttemptId ?? Guid.CreateVersion7();
            if (existing is null || existing.Stage == BranchActivationStage.NotStarted)
            {
                await credentialStore.SaveSessionAsync(
                    new BranchActivationSession(
                        attemptId,
                        BranchActivationStage.MaterialPrepared,
                        local.Id,
                        materials.PublicKey,
                        materials.Fingerprint,
                        InstallationToken.Hash(materials.InstallationToken),
                        Base64Url.Encode(materials.PrivateKeyPkcs8),
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        materials.InstallationToken,
                        timeProvider.GetUtcNow()),
                    cancellationToken);
            }

            var challenge = await cloudClient.CreateChallengeAsync(
                local.Id,
                materials.PublicKey,
                InstallationToken.Hash(materials.InstallationToken),
                cancellationToken);

            var payload = CanonicalActivationChallengeCodec.Encode(new CanonicalActivationChallenge(
                challenge.ChallengeId,
                local.Id,
                materials.Fingerprint,
                InstallationToken.Hash(materials.InstallationToken),
                challenge.Nonce,
                challenge.ExpiresAtUtc));
            var signature = EcdsaP256ActivationCrypto.Sign(payload, materials.PrivateKeyPkcs8);

            var exchange = await cloudClient.ExchangeAsync(
                activationCode,
                local.Id,
                materials.PublicKey,
                challenge.ChallengeId,
                signature,
                InstallationToken.Hash(materials.InstallationToken),
                cancellationToken);

            await credentialStore.SaveSessionAsync(
                new BranchActivationSession(
                    attemptId,
                    BranchActivationStage.Reserved,
                    local.Id,
                    materials.PublicKey,
                    materials.Fingerprint,
                    InstallationToken.Hash(materials.InstallationToken),
                    Base64Url.Encode(materials.PrivateKeyPkcs8),
                    challenge.ChallengeId,
                    challenge.Nonce,
                    exchange.ActivationId,
                    exchange.TenantId,
                    exchange.BranchId,
                    exchange.Receipt,
                    materials.InstallationToken,
                    timeProvider.GetUtcNow()),
                cancellationToken);

            await ConfirmAndFinalizeLocalAsync(
                exchange.ActivationId,
                exchange.Receipt,
                materials.InstallationToken,
                materials.PublicKey,
                materials.Fingerprint,
                InstallationToken.Hash(materials.InstallationToken),
                Base64Url.Encode(materials.PrivateKeyPkcs8),
                exchange.TenantId,
                exchange.BranchId,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(materials.PrivateKeyPkcs8);
        }
    }

    public async Task FinalizeAsync(CancellationToken cancellationToken = default)
    {
        var local = await branchInstanceAccessor.GetAsync(cancellationToken);
        if (local.Status == BranchServerStatus.Active)
        {
            return;
        }

        var session = await credentialStore.GetSessionAsync(cancellationToken)
            ?? throw new BranchActivationException(
                BranchActivationErrorCodes.ActivationInvalid,
                "No activation session is available to finalize.");

        if (session.ActivationId is null
            || session.Receipt is null
            || session.InstallationToken is null
            || session.TenantId is null
            || session.BranchId is null
            || session.PrivateKeyPkcs8Base64Url is null)
        {
            throw new BranchActivationException(
                BranchActivationErrorCodes.ActivationInvalid,
                "Activation session is incomplete.");
        }

        await ConfirmAndFinalizeLocalAsync(
            session.ActivationId.Value,
            session.Receipt,
            session.InstallationToken,
            session.PublicKey,
            session.PublicKeyFingerprint,
            session.InstallationTokenHash,
            session.PrivateKeyPkcs8Base64Url,
            session.TenantId.Value,
            session.BranchId.Value,
            cancellationToken);
    }

    public async Task<BranchActivationStage> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var local = await branchInstanceAccessor.GetAsync(cancellationToken);
        if (local.Status == BranchServerStatus.Active
            || await credentialStore.GetPermanentAsync(cancellationToken) is not null)
        {
            return BranchActivationStage.Completed;
        }

        var session = await credentialStore.GetSessionAsync(cancellationToken);
        if (session is null)
        {
            return BranchActivationStage.NotStarted;
        }

        if (session.Stage is BranchActivationStage.CloudConfirmed or BranchActivationStage.FinalizeRequired)
        {
            return BranchActivationStage.FinalizeRequired;
        }

        return session.Stage;
    }

    private async Task ConfirmAndFinalizeLocalAsync(
        Guid activationId,
        string receipt,
        string installationToken,
        string publicKey,
        string fingerprint,
        string installationTokenHash,
        string privateKeyPkcs8Base64Url,
        Guid tenantId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        // Cloud confirm MUST succeed before local Activate + Publish.
        var confirm = await cloudClient.ConfirmAsync(activationId, receipt, installationToken, cancellationToken);

        await credentialStore.SaveSessionAsync(
            new BranchActivationSession(
                Guid.CreateVersion7(),
                BranchActivationStage.CloudConfirmed,
                confirm.BranchInstanceId,
                publicKey,
                fingerprint,
                installationTokenHash,
                privateKeyPkcs8Base64Url,
                null,
                null,
                confirm.ActivationId,
                confirm.TenantId,
                confirm.BranchId,
                receipt,
                installationToken,
                timeProvider.GetUtcNow()),
            cancellationToken);

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var entity = await db.BranchInstances
                .SingleAsync(x => x.SingletonKey == BranchInstance.LocalSingletonKey, cancellationToken);
            if (entity.Status != BranchInstance.ActiveStatus)
            {
                entity.Activate(tenantId, branchId, activationId, timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);

            memoryStore.Publish(new BranchInstanceInfo(
                entity.Id,
                BranchServerStatus.Active,
                tenantId,
                branchId));
        });

        await credentialStore.SavePermanentAsync(
            new PermanentBranchCredentials(
                confirm.BranchInstanceId,
                tenantId,
                branchId,
                activationId,
                publicKey,
                fingerprint,
                installationToken,
                installationTokenHash,
                privateKeyPkcs8Base64Url,
                timeProvider.GetUtcNow()),
            cancellationToken);
        await credentialStore.ClearSessionAsync(cancellationToken);
    }

    private static PreparedMaterials ResolveOrCreateMaterials(Guid branchInstanceId, BranchActivationSession? existing)
    {
        if (existing is not null
            && existing.Stage == BranchActivationStage.MaterialPrepared
            && existing.InstallationToken is not null
            && existing.PrivateKeyPkcs8Base64Url is not null)
        {
            return new PreparedMaterials(
                branchInstanceId,
                existing.PublicKey,
                existing.PublicKeyFingerprint,
                Base64Url.Decode(existing.PrivateKeyPkcs8Base64Url),
                existing.InstallationToken);
        }

        var keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        var token = InstallationToken.Generate();
        return new PreparedMaterials(
            branchInstanceId,
            keyPair.PublicKey,
            EcdsaP256ActivationCrypto.Fingerprint(keyPair.PublicKey),
            keyPair.PrivateKeyPkcs8,
            token);
    }

    private sealed record PreparedMaterials(
        Guid BranchInstanceId,
        string PublicKey,
        string Fingerprint,
        byte[] PrivateKeyPkcs8,
        string InstallationToken);
}
