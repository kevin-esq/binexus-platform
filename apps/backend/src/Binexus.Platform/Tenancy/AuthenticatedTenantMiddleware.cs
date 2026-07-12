using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Binexus.Platform.Tenancy;

/// <summary>
/// Resolves <see cref="ICurrentTenant"/> from a validated JWT <see cref="ClaimsPrincipal"/>.
/// Active in every environment, including Production. Never reads client-supplied headers.
/// </summary>
public sealed class AuthenticatedTenantMiddleware(RequestDelegate next, ICurrentTenant currentTenant)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            SetFromClaimsOrThrow(context);
        }

        try
        {
            await next(context);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private void SetFromClaimsOrThrow(HttpContext context)
    {
        var principal = context.User;
        if (!TryGetGuid(principal, "tenantId", out var tenantId))
        {
            throw new InvalidOperationException("Authenticated principal is missing a valid tenantId claim.");
        }

        if (!TryGetGuid(principal, "sub", out var userId)
            && !TryGetGuid(principal, ClaimTypes.NameIdentifier, out userId))
        {
            throw new InvalidOperationException("Authenticated principal is missing a valid sub claim.");
        }

        Guid? branchId = null;
        var branchRaw = principal.FindFirstValue("branchId");
        if (!string.IsNullOrWhiteSpace(branchRaw))
        {
            if (!Guid.TryParse(branchRaw, out var parsedBranchId))
            {
                throw new InvalidOperationException("Authenticated principal has an invalid branchId claim.");
            }

            branchId = parsedBranchId;
        }

        currentTenant.SetContext(new TenantContext(
            tenantId,
            userId,
            principal.FindFirstValue("role"),
            branchId,
            Activity.Current?.Id ?? context.TraceIdentifier));
    }

    private static bool TryGetGuid(ClaimsPrincipal principal, string claimType, out Guid value) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out value);
}
