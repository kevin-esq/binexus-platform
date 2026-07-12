using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Binexus.Modules.Identity.Infrastructure;

public sealed class AuthService(
    BinexusDbContext dbContext,
    IPasswordHasher passwordHasher,
    JwtTokenIssuer jwtTokenIssuer,
    JwtOptions jwtOptions,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    ICurrentTenant currentTenant,
    ILogger<AuthService> logger) : IAuthService
{
    /// <summary>
    /// Precomputed Argon2id hash used only to equalize login timing when the user is missing.
    /// Not a live credential; never logged.
    /// </summary>
    private const string DummyPasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=4$I/ldkJakfKcsF6SV2wB6Zg$aM2RBvTpjCf0yNFfhnE5Cy3juLgzptW87n9vmhHiIik";

    public async Task<AuthTokens> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var slug = request.TenantSlug.Trim().Normalize().ToLowerInvariant();
        var tenant = await dbContext.Set<Tenant>()
            .SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);

        if (tenant is null)
        {
            _ = await passwordHasher.VerifyAsync(DummyPasswordHash, request.Password, cancellationToken);
            AuthSecurityLog.LoginFailed(logger, LoginFailedReason.TenantNotFound);
            throw InvalidCredentials();
        }

        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        var user = await dbContext.Set<User>()
            .Include(x => x.Branch)
            .SingleOrDefaultAsync(
                x => x.TenantId == tenant.Id && x.NormalizedEmail == normalizedEmail,
                cancellationToken);

        if (user is null)
        {
            _ = await passwordHasher.VerifyAsync(DummyPasswordHash, request.Password, cancellationToken);
            AuthSecurityLog.LoginFailed(logger, LoginFailedReason.UserNotFound);
            throw InvalidCredentials();
        }

        if (!await passwordHasher.VerifyAsync(user.PasswordHash, request.Password, cancellationToken))
        {
            AuthSecurityLog.LoginFailed(
                logger,
                LoginFailedReason.InvalidPassword,
                userId: user.Id,
                tenantId: user.TenantId);
            throw InvalidCredentials();
        }

        if (user.IsDisabled)
        {
            AuthSecurityLog.LoginFailed(
                logger,
                LoginFailedReason.UserDisabled,
                userId: user.Id,
                tenantId: user.TenantId);
            throw InvalidCredentials();
        }

        if (!RoleNames.IsKnown(user.Role))
        {
            AuthSecurityLog.LoginFailed(
                logger,
                LoginFailedReason.UnknownRole,
                userId: user.Id,
                tenantId: user.TenantId);
            throw InvalidCredentials();
        }

        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            user.UpdatePasswordHash(await passwordHasher.HashAsync(request.Password, cancellationToken));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var branchId = await ResolveTrustedBranchIdAsync(user, cancellationToken);
        var tokens = await IssueTokenPairAsync(user, branchId, null, null, cancellationToken);
        AuthSecurityLog.LoginSucceeded(logger, user.Id, user.TenantId);
        return tokens;
    }

    public Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        new RefreshTokenRotation(
            dbContext,
            jwtTokenIssuer,
            jwtOptions,
            idGenerator,
            timeProvider,
            logger).RefreshAsync(refreshToken, cancellationToken);

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        new RefreshTokenRotation(
            dbContext,
            jwtTokenIssuer,
            jwtOptions,
            idGenerator,
            timeProvider,
            logger).LogoutAsync(refreshToken, cancellationToken);

    public async Task<AuthSession> GetSessionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = currentTenant.Current?.TenantId
            ?? throw new AuthException(AuthErrorCodes.Forbidden, "Forbidden.");

        var user = await dbContext.Set<User>()
            .Include(x => x.Tenant)
            .Include(x => x.Branch)
            .SingleOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantId, cancellationToken);

        if (user is null)
        {
            throw new AuthException(AuthErrorCodes.Forbidden, "Forbidden.");
        }

        // Authenticated caller with a still-valid JWT whose account was disabled (or role
        // corrupted) after issuance. Public code does not reveal disable vs missing details.
        if (user.IsDisabled || !RoleNames.IsKnown(user.Role))
        {
            throw new AuthException(
                AuthErrorCodes.AccountUnavailable,
                "Account is unavailable.");
        }

        var branch = user.Branch is not null && user.Branch.TenantId == user.TenantId
            ? user.Branch
            : null;

        return new AuthSession(
            new AuthUser(user.Id, user.Email, user.Role, branch?.Id, user.TenantId),
            new AuthTenant(user.Tenant.Id, user.Tenant.Slug, user.Tenant.Name),
            branch is null ? null : new AuthBranch(branch.Id, branch.Name));
    }

    private static AuthException InvalidCredentials() =>
        new(AuthErrorCodes.InvalidCredentials, "Invalid credentials.");

    private async Task<Guid?> ResolveTrustedBranchIdAsync(User user, CancellationToken cancellationToken)
    {
        if (user.BranchId is null)
        {
            return null;
        }

        if (user.Branch is not null)
        {
            return user.Branch.TenantId == user.TenantId ? user.BranchId : null;
        }

        var belongs = await dbContext.Set<Branch>()
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.Id == user.BranchId && x.TenantId == user.TenantId,
                cancellationToken);
        return belongs ? user.BranchId : null;
    }

    private Task<AuthTokens> IssueTokenPairAsync(
        User user,
        Guid? trustedBranchId,
        Guid? familyId,
        Guid? parentTokenId,
        CancellationToken cancellationToken,
        Guid? tokenId = null) =>
        new RefreshTokenRotation(
            dbContext,
            jwtTokenIssuer,
            jwtOptions,
            idGenerator,
            timeProvider,
            logger).IssueTokenPairAsync(
            user,
            trustedBranchId,
            familyId,
            parentTokenId,
            cancellationToken,
            tokenId);
}

/// <summary>Refresh rotation, logout, and token pair issuance — split from AuthService for SRP.</summary>
internal sealed class RefreshTokenRotation(
    BinexusDbContext dbContext,
    JwtTokenIssuer jwtTokenIssuer,
    JwtOptions jwtOptions,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    ILogger logger)
{
    internal async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AuthException(AuthErrorCodes.InvalidRefreshToken, "Invalid refresh token.");
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => RefreshWithinTransactionAsync(refreshToken, cancellationToken));
    }

    private async Task<AuthTokens> RefreshWithinTransactionAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = RefreshTokenHasher.Hash(refreshToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var record = await dbContext.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Include(x => x.User)
                .ThenInclude(x => x.Branch)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (record is null || record.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            throw new AuthException(AuthErrorCodes.InvalidRefreshToken, "Invalid refresh token.");
        }

        if (record.UsedAtUtc is not null || record.RevokedAtUtc is not null)
        {
            await RevokeFamilyAndCommitAsync(record.FamilyId, transaction, cancellationToken);
            AuthSecurityLog.RefreshReuseDetected(logger, record.FamilyId);
            throw new AuthException(AuthErrorCodes.RefreshTokenReused, "Refresh token reuse detected.");
        }

        if (record.User.IsDisabled || !RoleNames.IsKnown(record.User.Role))
        {
            await RevokeFamilyAndCommitAsync(record.FamilyId, transaction, cancellationToken);
            AuthSecurityLog.LoginFailed(
                logger,
                record.User.IsDisabled ? LoginFailedReason.UserDisabled : LoginFailedReason.UnknownRole,
                userId: record.UserId,
                tenantId: record.TenantId);
            throw new AuthException(AuthErrorCodes.InvalidRefreshToken, "Invalid refresh token.");
        }

        var now = timeProvider.GetUtcNow();
        var replacementId = idGenerator.NewId();
        var claimed = await dbContext.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(x => x.Id == record.Id && x.UsedAtUtc == null && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.UsedAtUtc, now)
                .SetProperty(x => x.ReplacedByTokenId, replacementId), cancellationToken);

        if (claimed != 1)
        {
            await RevokeFamilyAndCommitAsync(record.FamilyId, transaction, cancellationToken);
            AuthSecurityLog.RefreshReuseDetected(logger, record.FamilyId);
            throw new AuthException(AuthErrorCodes.RefreshTokenReused, "Refresh token reuse detected.");
        }

        var branchId = await ResolveTrustedBranchIdAsync(record.User, cancellationToken);
        var tokens = await IssueTokenPairAsync(
            record.User,
            branchId,
            record.FamilyId,
            record.Id,
            cancellationToken,
            replacementId);
        await transaction.CommitAsync(cancellationToken);
        AuthSecurityLog.RefreshSucceeded(logger, record.UserId, record.TenantId);
        return tokens;
    }

    internal async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var tokenHash = RefreshTokenHasher.Hash(refreshToken);
        var now = timeProvider.GetUtcNow();
        var record = await dbContext.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (record is null)
        {
            return;
        }

        var revoked = await dbContext.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(x => x.Id == record.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevocationReason, "LOGOUT"), cancellationToken);

        if (revoked == 1)
        {
            AuthSecurityLog.LogoutSucceeded(logger, record.UserId);
        }
    }

    internal async Task<AuthTokens> IssueTokenPairAsync(
        User user,
        Guid? trustedBranchId,
        Guid? familyId,
        Guid? parentTokenId,
        CancellationToken cancellationToken,
        Guid? tokenId = null)
    {
        var now = timeProvider.GetUtcNow();
        var opaqueToken = RefreshTokenHasher.Generate();
        var refreshToken = new RefreshToken(
            tokenId ?? idGenerator.NewId(),
            user.TenantId,
            user.Id,
            RefreshTokenHasher.Hash(opaqueToken),
            familyId ?? idGenerator.NewId(),
            parentTokenId,
            now,
            now.Add(jwtOptions.RefreshTokenLifetime));

        dbContext.Set<RefreshToken>().Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = jwtTokenIssuer.Issue(
            new AccessTokenSubject(user.Id, user.TenantId, user.Role, trustedBranchId));
        return new AuthTokens(accessToken, opaqueToken);
    }

    private async Task<Guid?> ResolveTrustedBranchIdAsync(User user, CancellationToken cancellationToken)
    {
        if (user.BranchId is null)
        {
            return null;
        }

        if (user.Branch is not null)
        {
            return user.Branch.TenantId == user.TenantId ? user.BranchId : null;
        }

        var belongs = await dbContext.Set<Branch>()
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.Id == user.BranchId && x.TenantId == user.TenantId,
                cancellationToken);
        return belongs ? user.BranchId : null;
    }

    private async Task RevokeFamilyAndCommitAsync(
        Guid familyId,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await dbContext.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(x => x.FamilyId == familyId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevocationReason, "REUSE_DETECTED"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
