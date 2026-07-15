using Binexus.Composition;
using Binexus.Platform.DependencyInjection;
using Binexus.Platform.Hosting;

namespace Binexus.Workers.Hosting;

/// <summary>
/// Shared Workers composition entry so tests can apply configuration before services register.
/// </summary>
public static class WorkersHost
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);
        return builder;
    }

    /// <summary>
    /// Test helper: configuration is applied before Core/Runtime registration.
    /// </summary>
    public static WebApplicationBuilder CreateBuilder(
        IEnumerable<KeyValuePair<string, string?>> configuration,
        string environmentName = "Testing")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            Args = [],
        });
        builder.Configuration.AddInMemoryCollection(configuration);
        ConfigureServices(builder);
        return builder;
    }

    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddBinexusCore(builder.Configuration, builder.Environment);
        builder.Services.AddBinexusRuntime(builder.Configuration);
    }

    public static void MapOperationalEndpoints(WebApplication app)
    {
        var liveness = () => Results.Ok(new { status = "ok" });
        app.MapGet("/health", liveness);
        app.MapGet("/health/live", liveness);
        app.MapRuntimeHealth();
        app.MapBranchHealth();
    }

    /// <summary>
    /// Maps operational endpoints and completes Branch identity ensure before the host accepts work.
    /// </summary>
    public static async Task InitializeAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        MapOperationalEndpoints(app);
        await app.Services.EnsureBranchRuntimeInitializedAsync(cancellationToken);
    }
}
