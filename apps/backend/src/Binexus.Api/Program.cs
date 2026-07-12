using System.Globalization;
using System.Threading.RateLimiting;
using Binexus.Api.Features.Internal;
using Binexus.Api.Health;
using Binexus.Modules.Identity;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Infrastructure;
using Binexus.Modules.Inventory;
using Binexus.Modules.Logistics;
using Binexus.Modules.Orders;
using Binexus.Modules.Sales;
using Binexus.Modules.Warehouse;
using Binexus.Platform.Configuration;
using Binexus.Platform.DependencyInjection;
using Binexus.Platform.Hosting;
using Binexus.Platform.Tenancy;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddBinexusSerilog();
builder.Services.AddBinexusForwardedHeaders(builder.Configuration);
builder.Services.AddBinexusPlatform(builder.Configuration);
builder.Services.AddBinexusDispatching();
builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);
builder.Services.AddInventoryModule();
builder.Services.AddOrdersModule();
builder.Services.AddWarehouseModule();
builder.Services.AddLogisticsModule(builder.Configuration);
builder.Services.AddSalesModule();

if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddTenantProbeFeature();
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                type = "https://httpstatuses.com/429",
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
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<AuthenticatedTenantMiddleware>();
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseMiddleware<DevelopmentTenantOverrideMiddleware>();
}
app.UseAuthorization();

if (app.Environment.IsDevelopment())
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

await app.RunAsync();

public partial class Program;
