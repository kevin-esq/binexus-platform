namespace Binexus.Modules.Identity.Application;

public sealed record LoginRequest(string TenantSlug, string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthTokens(string AccessToken, string RefreshToken);

public sealed record AuthUser(
    Guid Id,
    string Email,
    string Role,
    Guid? BranchId,
    Guid TenantId);

public sealed record AuthTenant(Guid Id, string Slug, string Name);

public sealed record AuthBranch(Guid Id, string Name);

public sealed record AuthSession(AuthUser User, AuthTenant Tenant, AuthBranch? Branch);

public sealed record AccessTokenSubject(
    Guid UserId,
    Guid TenantId,
    string Role,
    Guid? BranchId);
