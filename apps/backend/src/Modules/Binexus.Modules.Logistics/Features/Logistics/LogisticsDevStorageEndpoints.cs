using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Binexus.Modules.Logistics.Features.Logistics;

/// <summary>
/// Development/Testing-only PUT sink for <see cref="LocalObjectStorage"/> browser uploads.
/// Not registered for use in Production/Staging (returns 404). Excluded from OpenAPI.
/// Only accepts keys previously issued by <see cref="IObjectStorage.PresignPutAsync"/>;
/// rejects traversal, unissued keys, wrong MIME, oversized bodies, and overwrite (second PUT).
/// </summary>
public static class LogisticsDevStorageEndpoints
{
    public static IEndpointRouteBuilder MapLogisticsDevStorageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/internal/dev-object-storage/{**objectKey}", PutAsync)
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithName("DevObjectStoragePut")
            .WithTags("Internal");

        return endpoints;
    }

    private static async Task<IResult> PutAsync(
        string objectKey,
        HttpRequest request,
        IObjectStorage storage,
        IOptions<LogisticsStorageOptions> options,
        IHostEnvironment environment,
        CancellationToken ct)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return Results.NotFound();
        }

        if (!options.Value.IsLocal)
        {
            return Results.NotFound();
        }

        var key = Uri.UnescapeDataString(objectKey).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(key)
            || !key.StartsWith("tenants/", StringComparison.Ordinal)
            || key.Contains("..", StringComparison.Ordinal)
            || key.Contains('\\', StringComparison.Ordinal)
            || key.Split('/').Any(string.IsNullOrWhiteSpace))
        {
            return Results.BadRequest(new { detail = "Object key must be under tenants/ without traversal.", code = "INVALID_OBJECT_KEY" });
        }

        var maxBytes = options.Value.MaxProofBytes > 0 ? options.Value.MaxProofBytes : 10 * 1024 * 1024;
        var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = maxBytes;
        }

        if (storage is not LocalObjectStorage local)
        {
            return Results.NotFound();
        }

        if (request.ContentLength is > 0 and var prematureLength && prematureLength > maxBytes)
        {
            await request.Body.CopyToAsync(Stream.Null, ct);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var acceptance = local.TryAcceptPut(key, request.ContentType, request.ContentLength);
        if (acceptance != LocalPutAcceptance.Accepted)
        {
            // Drain body so the client connection completes cleanly on rejection paths.
            await request.Body.CopyToAsync(Stream.Null, ct);
            return acceptance switch
            {
                LocalPutAcceptance.Unissued => Results.NotFound(new { detail = "Object key was not issued by PresignPut.", code = "UNISSUED_OBJECT_KEY" }),
                LocalPutAcceptance.AlreadyUploaded => Results.Conflict(new { detail = "Object key was already uploaded. Overwrite is rejected.", code = "OBJECT_ALREADY_UPLOADED" }),
                LocalPutAcceptance.WrongContentType => Results.BadRequest(new { detail = "Content-Type does not match the Presign intent.", code = "WRONG_CONTENT_TYPE" }),
                LocalPutAcceptance.Oversized => Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
                _ => Results.BadRequest(new { detail = "Invalid object upload.", code = "INVALID_OBJECT_UPLOAD" }),
            };
        }

        await request.Body.CopyToAsync(Stream.Null, ct);
        return Results.NoContent();
    }
}
