using System.Globalization;
using System.Threading.RateLimiting;
using Binexus.Api.Features.Internal;
using Binexus.Api.Health;
using Binexus.Api.OpenApi;
using Binexus.Api.RateLimiting;
using Binexus.Composition;
using Binexus.Modules.Identity;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Infrastructure;
using Binexus.Modules.Inventory;
using Binexus.Modules.Logistics;
using Binexus.Modules.Orders;
using Binexus.Modules.Sales;
using Binexus.Modules.Warehouse;
using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Configuration;
using Binexus.Platform.DependencyInjection;
using Binexus.Platform.Hosting;
using Binexus.Platform.Tenancy;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddBinexusSerilog();
builder.Services.AddBinexusForwardedHeaders(builder.Configuration);
builder.Services.AddBinexusCore(builder.Configuration, builder.Environment);
builder.Services.AddBinexusRuntime(builder.Configuration);

if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddTenantProbeFeature();
}

builder.Services.AddProblemDetails();

var isBranchRuntime = string.Equals(
    builder.Configuration["Binexus:RuntimeMode"],
    "Branch",
    StringComparison.OrdinalIgnoreCase);

// Default document: Cloud artifact stays free of DeviceAuth. Branch runtime stamps Dev+User AND composition.
builder.Services.AddOpenApi(options =>
{
    if (isBranchRuntime)
    {
        options.AddDocumentTransformer<BranchDeviceAuthOpenApiDocumentTransformer>();
    }
});

// Separate Branch machine/admin OpenAPI document (pairing). Registered only for Branch runtime so the
// Cloud build-time artifact (binexus-v1.json) is never affected. Served at /openapi/branch-v1.json.
if (isBranchRuntime)
{
    builder.Services.AddOpenApi(BranchDevicePairingEndpointExtensions.BranchDocumentGroup, options =>
    {
        // Pairing-only surface. Without this, ungrouped endpoints (e.g. /health) leak into every named
        // document because the default predicate is `GroupName == null || GroupName == documentName`.
        options.ShouldInclude = api =>
            string.Equals(api.GroupName, BranchDevicePairingEndpointExtensions.BranchDocumentGroup, StringComparison.Ordinal);
        options.AddDocumentTransformer<BranchDeviceAuthOpenApiDocumentTransformer>();
    });
}

var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()
    ?? new CorsOptions { AllowedOrigins = ["http://localhost:3000"] };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOptions.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var maxBody = builder.Configuration.GetSection(SecurityOptions.SectionName)
    .Get<SecurityOptions>()?.MaxRequestBodyBytes ?? 1_048_576;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxBody);

var deviceAuthRate = builder.Configuration.GetSection(BranchDeviceAuthOptions.SectionName);
var deviceAuthIpLimit = deviceAuthRate.GetValue("IpPermitLimit", 0);
if (deviceAuthIpLimit <= 0)
{
    deviceAuthIpLimit = deviceAuthRate.GetValue("MachinePermitLimit", 30);
}

var deviceAuthDeviceLimit = deviceAuthRate.GetValue("DevicePermitLimit", 20);
var deviceAuthGlobalLimit = deviceAuthRate.GetValue("GlobalPermitLimit", 120);
var deviceAuthWindowSeconds = deviceAuthRate.GetValue("RateLimitWindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Global + IP + DeviceId for device-auth; other paths use endpoint policies only.
    options.GlobalLimiter = BranchDeviceAuthRateLimiterFactory.Create(
        deviceAuthGlobalLimit,
        deviceAuthIpLimit,
        deviceAuthDeviceLimit,
        TimeSpan.FromSeconds(deviceAuthWindowSeconds));
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        // Generic Problem Details only — never reveal device existence or status.
        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                type = "https://binexus.dev/problems/rate-limited",
                title = "Too Many Requests",
                status = StatusCodes.Status429TooManyRequests,
                detail = "Authentication rate limit exceeded. Retry later.",
                code = "RATE_LIMITED",
            },
            cancellationToken);
    };

    // Partition by trusted remote IP + path so a single account cannot be locked via email/slug alone.
    options.AddPolicy("auth", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = httpContext.Request.Path.Value ?? "/auth";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{ip}:{path}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    options.AddPolicy("branch-activation-generate", httpContext =>
    {
        var tenantId = httpContext.User.FindFirst("tenantId")?.Value;
        var userId = httpContext.User.FindFirst("sub")?.Value;
        var partition = !string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(userId)
            ? $"tenant:{tenantId}:user:{userId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        var permitLimit = builder.Configuration.GetValue("CloudActivation:GeneratePermitLimit", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            partition,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    options.AddPolicy("branch-activation-machine", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    options.AddPolicy("branch-pairing-admin", httpContext =>
    {
        var tenantId = httpContext.User.FindFirst("tenantId")?.Value;
        var userId = httpContext.User.FindFirst("sub")?.Value;
        var partition = !string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(userId)
            ? $"tenant:{tenantId}:user:{userId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        var permitLimit = builder.Configuration.GetValue("BranchPairing:AdminPermitLimit", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            partition,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    options.AddPolicy("branch-pairing-machine", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var permitLimit = builder.Configuration.GetValue("BranchPairing:MachinePermitLimit", 30);
        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    // Device-auth limits are enforced by GlobalLimiter (IP + DeviceId + global).
    // Keep the named policy so endpoints retain RequireRateLimiting without double-counting.
    options.AddPolicy("branch-device-auth", _ =>
        RateLimitPartition.GetNoLimiter("device-auth-delegated-to-global"));
});

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>();
if (databaseOptions is not null)
{
    // Ready = Postgres reachable + EF migrations applied. MinIO is NOT required for readiness
    // (Logistics proof fails at call time if object storage is down).
    builder.Services.AddHealthChecks()
        .AddNpgSql(databaseOptions.ConnectionString, name: "postgresql", tags: ["ready"])
        .AddCheck<EfMigrationsHealthCheck>("ef-migrations", tags: ["ready"]);
}

var app = builder.Build();

var seedOnly = args.Any(a => string.Equals(a, "--seed", StringComparison.OrdinalIgnoreCase));
if (seedOnly)
{
    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException("Identity demo seed is only available in Development or Testing.");
    }

    await using var seedScope = app.Services.CreateAsyncScope();
    var seeder = seedScope.ServiceProvider.GetRequiredService<DevelopmentIdentitySeeder>();
    await seeder.SeedAsync();
    return;
}

// ForwardedHeaders → HTTPS → Routing → CORS → RateLimiter → AuthN → AuthZ → endpoints
app.UseBinexusSecurityDefaults();
app.UseSerilogRequestLogging();
app.UseBinexusProblemDetails();
app.UseRouting();
app.UseCors();
// Testing-only: allow deterministic IP partition keys for rate-limit integration tests.
if (app.Environment.IsEnvironment("Testing"))
{
    app.Use(async (httpContext, next) =>
    {
        if (httpContext.Request.Headers.TryGetValue("X-Binexus-Test-Remote-Ip", out var ipHeader)
            && System.Net.IPAddress.TryParse(ipHeader.ToString(), out var parsed))
        {
            httpContext.Connection.RemoteIpAddress = parsed;
        }

        await next();
    });
}
// Buffer + capture deviceId before RateLimiter so partition keys stay sync/IO-free.
app.Use(async (httpContext, next) =>
{
    var path = httpContext.Request.Path.Value ?? string.Empty;
    if (HttpMethods.IsPost(httpContext.Request.Method)
        && (path.StartsWith("/branch/device-auth/challenges", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/branch/device-auth/tokens", StringComparison.OrdinalIgnoreCase)))
    {
        await BranchDeviceAuthRateLimitKeys.BufferAndCaptureDeviceIdAsync(httpContext, httpContext.RequestAborted);
    }

    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<AuthenticatedTenantMiddleware>();
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseMiddleware<DevelopmentTenantOverrideMiddleware>();
}
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
}

var liveness = () => Results.Ok(new { status = "ok" });
app.MapGet("/health", liveness)
    .WithName("Health")
    .WithTags("Health")
    .Produces<object>(StatusCodes.Status200OK);
app.MapGet("/health/live", liveness)
    .WithName("HealthLive")
    .WithTags("Health")
    .Produces<object>(StatusCodes.Status200OK);
app.MapRuntimeHealth();
app.MapBranchHealth();
app.MapCloudBranchActivationEndpoints();
app.MapBranchActivationEndpoints();
app.MapBranchDevicePairingEndpoints();
app.MapBranchDeviceAuthEndpoints();

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapTenantProbeEndpoints();
    app.MapLogisticsDevStorageEndpoints();
}

app.MapIdentityEndpoints();
app.MapInventoryEndpoints();
app.MapOrdersEndpoints();
app.MapWarehouseEndpoints();
app.MapLogisticsEndpoints();
app.MapSalesEndpoints();

// DX: Development seed on start when password is set. Compose smoke can also use profile `seed`.
var seedOnStart = !string.Equals(
    app.Configuration["SEED_ON_START"] ?? Environment.GetEnvironmentVariable("SEED_ON_START"),
    "0",
    StringComparison.Ordinal);
if (seedOnStart && app.Environment.IsDevelopment())
{
    var seedPassword = app.Configuration[$"{IdentitySeedOptions.SectionName}:AdminPassword"];
    if (!string.IsNullOrWhiteSpace(seedPassword))
    {
        await using var seedScope = app.Services.CreateAsyncScope();
        var seeder = seedScope.ServiceProvider.GetService<DevelopmentIdentitySeeder>();
        if (seeder is not null)
        {
            await seeder.SeedAsync();
        }
    }
}

await app.Services.EnsureBranchRuntimeInitializedAsync();
await app.RunAsync();

public partial class Program;
