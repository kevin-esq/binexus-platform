using System.Text.Json.Serialization;
using Binexus.Platform.Branching.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Platform.Hosting;

public static class BranchHealthEndpointExtensions
{
    public static IEndpointRouteBuilder MapBranchHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/health/branch",
                async (HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var accessor = httpContext.RequestServices.GetService<IBranchInstanceAccessor>();
                    if (accessor is null)
                    {
                        return Results.NotFound();
                    }

                    var info = await accessor.GetAsync(cancellationToken);
                    return Results.Json(new BranchHealthResponse(
                        info.Status.ToString(),
                        info.Id.ToString("D"),
                        info.TenantId?.ToString("D"),
                        info.BranchId?.ToString("D")));
                })
            .WithName("HealthBranch")
            .WithTags("Health")
            .ExcludeFromDescription();

        return endpoints;
    }
}

internal sealed record BranchHealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("branchInstanceId")] string BranchInstanceId,
    [property: JsonPropertyName("tenantId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TenantId,
    [property: JsonPropertyName("branchId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BranchId);
