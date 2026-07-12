using Binexus.Platform.Dispatching;
using Binexus.Platform.Tenancy;
using Binexus.SharedKernel.Abstractions;
using Binexus.SharedKernel.Results;

namespace Binexus.Api.Features.Internal;

public static class TenantProbeFeatureExtensions
{
    public static IServiceCollection AddTenantProbeFeature(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<GetTenantProbeQuery, TenantProbeResult>, GetTenantProbeQueryHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapTenantProbeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/tenant-probe", async (
            IQueryDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.DispatchAsync(new GetTenantProbeQuery(), cancellationToken);
            return result.IsSuccess
                ? Microsoft.AspNetCore.Http.Results.Ok(result.Value)
                : Microsoft.AspNetCore.Http.Results.Problem(result.Error?.Message, statusCode: StatusCodes.Status400BadRequest);
        })
            .RequireHost("*")
            .WithName("TenantProbe")
            .WithTags("Internal")
            .ExcludeFromDescription();

        return app;
    }
}

public sealed record GetTenantProbeQuery : IQuery<TenantProbeResult>;

public sealed record TenantProbeResult(Guid? TenantId, string RequestId);

public sealed class GetTenantProbeQueryHandler(ICurrentTenant currentTenant)
    : IQueryHandler<GetTenantProbeQuery, TenantProbeResult>
{
    public Task<Result<TenantProbeResult>> HandleAsync(
        GetTenantProbeQuery query,
        CancellationToken cancellationToken)
    {
        var current = currentTenant.Current;
        if (current is null)
        {
            return Task.FromResult(ResultFactory.Fail<TenantProbeResult>(
                DomainError.Validation("tenant.missing", "Tenant context is not available.")));
        }

        return Task.FromResult(ResultFactory.Ok(
            new TenantProbeResult(current.TenantId, current.RequestId)));
    }
}
