using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Binexus.Platform.Tenancy;

/// <summary>
/// Development/Testing-only override that sets tenant from probe headers when the request is
/// <em>not</em> authenticated. Never overrides JWT-derived identity. Must not be registered in Production.
/// </summary>
public sealed class DevelopmentTenantOverrideMiddleware(
    RequestDelegate next,
    ICurrentTenant currentTenant,
    IHostEnvironment environment)
{
    public const string TenantHeader = "X-Binexus-Tenant-Id";
    public const string UserHeader = "X-Binexus-User-Id";
    public const string BranchHeader = "X-Binexus-Branch-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                $"{nameof(DevelopmentTenantOverrideMiddleware)} must not run outside Development/Testing.");
        }

        if (context.User.Identity?.IsAuthenticated != true
            && currentTenant.Current is null)
        {
            TrySetFromHeaders(context);
        }

        await next(context);
    }

    private void TrySetFromHeaders(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(TenantHeader, out var tenantValue)
            || !Guid.TryParse(tenantValue.ToString(), out var tenantId))
        {
            return;
        }

        Guid? userId = null;
        if (context.Request.Headers.TryGetValue(UserHeader, out var userValue)
            && Guid.TryParse(userValue.ToString(), out var parsedUserId))
        {
            userId = parsedUserId;
        }

        Guid? branchId = null;
        if (context.Request.Headers.TryGetValue(BranchHeader, out var branchValue)
            && Guid.TryParse(branchValue.ToString(), out var parsedBranchId))
        {
            branchId = parsedBranchId;
        }

        currentTenant.SetContext(new TenantContext(
            tenantId,
            userId,
            Role: null,
            branchId,
            Activity.Current?.Id ?? context.TraceIdentifier));
    }
}
