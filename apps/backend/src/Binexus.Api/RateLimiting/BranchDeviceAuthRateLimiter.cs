using System.Text.Json;
using System.Threading.RateLimiting;

namespace Binexus.Api.RateLimiting;

/// <summary>
/// Peeks <c>deviceId</c> from buffered JSON bodies for Branch device-auth rate partitions.
/// Invalid or missing IDs collapse to a shared bucket so attackers cannot mint unbounded partitions.
/// </summary>
internal static class BranchDeviceAuthRateLimitKeys
{
    public const string InvalidDeviceBucket = "invalid";
    public const string HttpContextItemKey = "binexus.deviceAuth.rateLimit.deviceId";

    public static string NormalizeDeviceId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return InvalidDeviceBucket;
        }

        return Guid.TryParse(raw.Trim(), out var id)
            ? id.ToString("D")
            : InvalidDeviceBucket;
    }

    public static async Task BufferAndCaptureDeviceIdAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!HttpMethods.IsPost(httpContext.Request.Method))
        {
            httpContext.Items[HttpContextItemKey] = InvalidDeviceBucket;
            return;
        }

        httpContext.Request.EnableBuffering();
        httpContext.Request.Body.Position = 0;
        try
        {
            using var document = await JsonDocument.ParseAsync(
                httpContext.Request.Body,
                cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("deviceId", out var deviceIdElement))
            {
                httpContext.Items[HttpContextItemKey] = NormalizeDeviceId(deviceIdElement.GetString());
                return;
            }
        }
        catch (JsonException)
        {
            httpContext.Items[HttpContextItemKey] = InvalidDeviceBucket;
            return;
        }
        finally
        {
            httpContext.Request.Body.Position = 0;
        }

        httpContext.Items[HttpContextItemKey] = InvalidDeviceBucket;
    }

    public static string GetCapturedDeviceId(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(HttpContextItemKey, out var value) && value is string deviceId
            ? deviceId
            : InvalidDeviceBucket;
}

/// <summary>
/// Builds a chained limiter enforcing global + IP + DeviceId windows for device-auth endpoints.
/// </summary>
internal static class BranchDeviceAuthRateLimiterFactory
{
    public static PartitionedRateLimiter<HttpContext> Create(
        int globalPermitLimit,
        int ipPermitLimit,
        int devicePermitLimit,
        TimeSpan window)
    {
        return PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!IsDeviceAuthPath(context))
                {
                    return RateLimitPartition.GetNoLimiter("bypass");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    "device-auth:global",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalPermitLimit,
                        Window = window,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            }),
            PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!IsDeviceAuthPath(context))
                {
                    return RateLimitPartition.GetNoLimiter("bypass");
                }

                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"device-auth:ip:{ip}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = ipPermitLimit,
                        Window = window,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            }),
            PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!IsDeviceAuthPath(context))
                {
                    return RateLimitPartition.GetNoLimiter("bypass");
                }

                var deviceKey = BranchDeviceAuthRateLimitKeys.GetCapturedDeviceId(context);
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"device-auth:device:{deviceKey}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = devicePermitLimit,
                        Window = window,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            }));
    }

    private static bool IsDeviceAuthPath(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        return path.StartsWith("/branch/device-auth/challenges", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/branch/device-auth/tokens", StringComparison.OrdinalIgnoreCase);
    }
}
