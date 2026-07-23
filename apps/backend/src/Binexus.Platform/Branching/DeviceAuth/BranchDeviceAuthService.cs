using System.Security.Cryptography;
using Binexus.Platform.Branching.Application;
using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Crypto;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Branching.DeviceAuth;

public interface IBranchDeviceAuthService
{
    Task<DeviceAuthChallengeResponse> CreateChallengeAsync(Guid deviceId, CancellationToken cancellationToken);
    Task<DeviceAuthTokenResponse> IssueTokenAsync(
        Guid challengeId,
        Guid deviceId,
        string signature,
        string protocolVersion,
        CancellationToken cancellationToken);
    Task<DeviceAuthMeResponse> GetMeAsync(Guid deviceId, CancellationToken cancellationToken);
}

public interface IDeviceStatusResolver
{
    Task<DeviceStatusSnapshot> ResolveAsync(Guid branchInstanceId, Guid deviceId, CancellationToken cancellationToken);
    void Evict(Guid branchInstanceId, Guid deviceId);
}

public sealed record DeviceStatusSnapshot(
    string Status,
    string SecurityStamp,
    Guid TerminalId,
    Guid TenantId,
    Guid BranchId);

public sealed class DeviceStatusResolver(
    BinexusDbContext db,
    IBranchInstanceAccessor branchInstance,
    IMemoryCache cache,
    IOptions<BranchDeviceAuthOptions> options,
    ILogger<DeviceStatusResolver> logger) : IDeviceStatusResolver
{
    public async Task<DeviceStatusSnapshot> ResolveAsync(
        Guid branchInstanceId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKey(branchInstanceId, deviceId);
        if (cache.TryGetValue(cacheKey, out DeviceStatusSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var instance = await branchInstance.GetAsync(cancellationToken);
            if (instance.Id != branchInstanceId
                || instance.TenantId is null
                || instance.BranchId is null)
            {
                throw new DeviceAuthException(
                    DeviceAuthErrorCodes.DeviceBranchMismatch,
                    "Branch instance mismatch.");
            }

            var device = await db.BranchDevices.AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == deviceId && x.BranchInstanceId == branchInstanceId,
                    cancellationToken);
            if (device is null)
            {
                throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceProofInvalid, "Device proof invalid.");
            }

            if (device.Status == BranchDevice.RevokedStatus)
            {
                throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceRevoked, "Device is revoked.");
            }

            if (device.Status != BranchDevice.ActiveStatus)
            {
                throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceNotActive, "Device is not active.");
            }

            var terminals = await db.BranchTerminals.AsNoTracking()
                .Where(x => x.DeviceId == deviceId && x.BranchInstanceId == branchInstanceId)
                .ToListAsync(cancellationToken);
            var active = terminals.Where(x => x.Status == BranchTerminal.ActiveStatus).ToList();
            if (active.Count != 1)
            {
                if (active.Count == 0
                    && terminals.Exists(x => x.Status == BranchTerminal.DisabledStatus))
                {
                    throw new DeviceAuthException(
                        DeviceAuthErrorCodes.DeviceTerminalDisabled,
                        "Terminal is disabled.");
                }

                throw new DeviceAuthException(
                    active.Count == 0
                        ? DeviceAuthErrorCodes.DeviceTerminalMissing
                        : DeviceAuthErrorCodes.DeviceBindingInvalid,
                    "Terminal binding invalid.");
            }

            var snapshot = new DeviceStatusSnapshot(
                device.Status,
                device.SecurityStamp,
                active[0].Id,
                instance.TenantId.Value,
                instance.BranchId.Value);

            cache.Set(
                cacheKey,
                snapshot,
                TimeSpan.FromSeconds(options.Value.StatusCacheSeconds));
            return snapshot;
        }
        catch (DeviceAuthException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsLikelyProgrammingFault(ex))
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-closed for store/network/infra failures; do not admit the device.
            LogStatusResolutionFailed(logger, ex.GetType().Name, branchInstanceId, deviceId, ex);
            throw new DeviceAuthException(
                DeviceAuthErrorCodes.DeviceStatusUnavailable,
                "Device status unavailable.",
                ex);
        }
    }

    public void Evict(Guid branchInstanceId, Guid deviceId) =>
        cache.Remove(CacheKey(branchInstanceId, deviceId));

    private static string CacheKey(Guid branchInstanceId, Guid deviceId) =>
        $"device-auth:{branchInstanceId:D}:{deviceId:D}";

    private static bool IsLikelyProgrammingFault(Exception ex) =>
        ex is NullReferenceException
            or IndexOutOfRangeException
            or InvalidCastException
            or DivideByZeroException;

    private static readonly Action<ILogger, string, Guid, Guid, Exception?> LogStatusResolutionFailed =
        LoggerMessage.Define<string, Guid, Guid>(
            LogLevel.Warning,
            new EventId(2102, "DeviceStatusResolutionFailedClosed"),
            "Device status resolution failed closed. Category={Category} BranchInstanceId={BranchInstanceId} DeviceId={DeviceId}");
}

public sealed class BranchDeviceAuthService(
    BinexusDbContext db,
    IBranchInstanceAccessor branchInstance,
    IDeviceAccessTokenIssuer issuer,
    IDeviceStatusResolver statusResolver,
    IOptions<BranchDeviceAuthOptions> options,
    ILogger<BranchDeviceAuthService> logger) : IBranchDeviceAuthService
{
    public async Task<DeviceAuthChallengeResponse> CreateChallengeAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var instance = RequireActiveInstance(await branchInstance.GetAsync(cancellationToken));
        var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);

        // Anti-enumeration: only Active devices get a real challenge.
        var device = await db.BranchDevices
            .SingleOrDefaultAsync(
                x => x.Id == deviceId
                    && x.BranchInstanceId == instance.Id
                    && x.Status == BranchDevice.ActiveStatus,
                cancellationToken);
        if (device is null)
        {
            throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceProofInvalid, "Device proof invalid.");
        }

        var terminalOk = await db.BranchTerminals.CountAsync(
            x => x.DeviceId == deviceId
                && x.BranchInstanceId == instance.Id
                && x.Status == BranchTerminal.ActiveStatus,
            cancellationToken) == 1;
        if (!terminalOk)
        {
            throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceProofInvalid, "Device proof invalid.");
        }

        // Invalidate prior open challenges for this device.
        await db.DeviceAuthChallenges
            .Where(x => x.DeviceId == deviceId
                && x.BranchInstanceId == instance.Id
                && x.Status == DeviceAuthChallenge.OpenStatus)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.Status, DeviceAuthChallenge.ConsumedStatus),
                cancellationToken);

        var challengeId = Guid.CreateVersion7();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var expires = now.AddSeconds(options.Value.ChallengeTtlSeconds);
        db.DeviceAuthChallenges.Add(DeviceAuthChallenge.Create(
            challengeId, instance.Id, deviceId, nonce, expires, now));
        await db.SaveChangesAsync(cancellationToken);

        return new DeviceAuthChallengeResponse(
            challengeId,
            nonce,
            instance.Id,
            expires,
            DeviceAuthCryptoFormats.ChallengeVersion);
    }

    public async Task<DeviceAuthTokenResponse> IssueTokenAsync(
        Guid challengeId,
        Guid deviceId,
        string signature,
        string protocolVersion,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(protocolVersion, DeviceAuthCryptoFormats.ChallengeVersion, StringComparison.Ordinal))
        {
            throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceProofInvalid, "Device proof invalid.");
        }

        var instance = RequireActiveInstance(await branchInstance.GetAsync(cancellationToken));

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var now = await BranchPairingSupport.GetDatabaseNowAsync(db, cancellationToken);
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var challenge = await db.DeviceAuthChallenges
                .SingleOrDefaultAsync(x => x.Id == challengeId, cancellationToken);

            if (challenge is null
                || challenge.DeviceId != deviceId
                || challenge.BranchInstanceId != instance.Id)
            {
                throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceProofInvalid, "Device proof invalid.");
            }

            if (challenge.Status != DeviceAuthChallenge.OpenStatus)
            {
                throw new DeviceAuthException(
                    DeviceAuthErrorCodes.DeviceChallengeReplayed,
                    "Challenge already used.");
            }

            if (challenge.ExpiresAtUtc <= now)
            {
                throw new DeviceAuthException(
                    DeviceAuthErrorCodes.DeviceChallengeExpired,
                    "Challenge expired.");
            }

            var device = await db.BranchDevices
                .SingleOrDefaultAsync(
                    x => x.Id == deviceId && x.BranchInstanceId == instance.Id,
                    cancellationToken);
            if (device is null || device.Status != BranchDevice.ActiveStatus)
            {
                throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceProofInvalid, "Device proof invalid.");
            }

            var payload = CanonicalDeviceAuthChallengeCodec.Encode(
                new CanonicalDeviceAuthChallenge(
                    challenge.Id,
                    challenge.BranchInstanceId,
                    challenge.DeviceId,
                    device.PublicKeyFingerprint,
                    device.CredentialHash,
                    challenge.Nonce,
                    challenge.ExpiresAtUtc));

            if (!EcdsaP256ActivationCrypto.Verify(payload, device.PublicKey, signature))
            {
                throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceProofInvalid, "Device proof invalid.");
            }

            var affected = await db.DeviceAuthChallenges
                .Where(x => x.Id == challengeId
                    && x.Status == DeviceAuthChallenge.OpenStatus
                    && x.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, DeviceAuthChallenge.ConsumedStatus)
                        .SetProperty(x => x.ConsumedAtUtc, now),
                    cancellationToken);
            if (affected != 1)
            {
                throw new DeviceAuthException(
                    DeviceAuthErrorCodes.DeviceChallengeReplayed,
                    "Challenge already used.");
            }

            var terminals = await db.BranchTerminals
                .Where(x => x.DeviceId == deviceId && x.BranchInstanceId == instance.Id)
                .ToListAsync(cancellationToken);
            var activeTerminals = terminals.Where(x => x.Status == BranchTerminal.ActiveStatus).ToList();
            if (activeTerminals.Count != 1)
            {
                throw new DeviceAuthException(
                    DeviceAuthErrorCodes.DeviceBindingInvalid,
                    "Terminal binding invalid.");
            }

            var terminal = activeTerminals[0];
            var (token, expires) = issuer.Issue(
                new DeviceAccessTokenSubject(
                    device.Id,
                    instance.Id,
                    instance.TenantId!.Value,
                    instance.BranchId!.Value,
                    terminal.Id,
                    device.SecurityStamp),
                now);

            await tx.CommitAsync(cancellationToken);
            statusResolver.Evict(instance.Id, device.Id);

            if (options.Value.AllowInsecureBranchTransport)
            {
                LogDatIssuedInsecureTransport(logger, true, device.Id, null);
            }

            return new DeviceAuthTokenResponse(
                token,
                DeviceAuthCryptoFormats.TokenType,
                expires,
                device.Id,
                terminal.Id,
                instance.Id);
        });
    }

    public async Task<DeviceAuthMeResponse> GetMeAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var instance = RequireActiveInstance(await branchInstance.GetAsync(cancellationToken));
        var snapshot = await statusResolver.ResolveAsync(instance.Id, deviceId, cancellationToken);
        if (snapshot.Status != BranchDevice.ActiveStatus)
        {
            throw new DeviceAuthException(
                snapshot.Status == BranchDevice.RevokedStatus
                    ? DeviceAuthErrorCodes.DeviceRevoked
                    : DeviceAuthErrorCodes.DeviceNotActive,
                "Device is not active.");
        }

        return new DeviceAuthMeResponse(
            deviceId,
            snapshot.Status,
            snapshot.TerminalId,
            instance.Id,
            snapshot.TenantId,
            snapshot.BranchId);
    }

    private static BranchInstanceInfo RequireActiveInstance(BranchInstanceInfo instance)
    {
        if (instance.Status != BranchServerStatus.Active
            || instance.TenantId is null
            || instance.BranchId is null)
        {
            throw new DeviceAuthException(
                DeviceAuthErrorCodes.DeviceBranchMismatch,
                "Branch instance is not active.");
        }

        return instance;
    }

    private static readonly Action<ILogger, bool, Guid, Exception?> LogDatIssuedInsecureTransport =
        LoggerMessage.Define<bool, Guid>(
            LogLevel.Warning,
            new EventId(2101, "DatIssuedInsecureTransport"),
            "DAT issued with AllowInsecureBranchTransport={Allow} (DeviceId={DeviceId}). HTTP LAN is not a supported production configuration.");
}
