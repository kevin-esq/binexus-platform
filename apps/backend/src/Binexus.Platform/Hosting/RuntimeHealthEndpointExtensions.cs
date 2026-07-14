using Binexus.Platform.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Binexus.Platform.Hosting;

public static class RuntimeHealthEndpointExtensions
{
    public static IEndpointRouteBuilder MapRuntimeHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/health/runtime",
                (IRuntimeDescriptor descriptor) =>
                    Results.Json(new RuntimeHealthResponse(descriptor.Mode.ToString())))
            .WithName("HealthRuntime")
            .WithTags("Health")
            .ExcludeFromDescription();

        return endpoints;
    }
}
