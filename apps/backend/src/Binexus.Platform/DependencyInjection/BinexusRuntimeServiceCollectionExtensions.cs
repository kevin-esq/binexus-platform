using Binexus.Platform.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.DependencyInjection;

public static class BinexusRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates <see cref="BinexusRuntimeOptions"/>, selects Cloud or Branch composition,
    /// and registers a single <see cref="IRuntimeDescriptor"/>. Does not call <c>BuildServiceProvider</c>.
    /// </summary>
    public static IServiceCollection AddBinexusRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<BinexusRuntimeOptions>, BinexusRuntimeOptionsValidator>();
        services.AddOptions<BinexusRuntimeOptions>()
            .Bind(configuration.GetSection(BinexusRuntimeOptions.SectionName))
            .PostConfigure(options =>
            {
                // Build-time OpenAPI GetDocument host only — not a production image/runtime default.
                if (string.IsNullOrWhiteSpace(options.RuntimeMode) && IsOpenApiDocumentGenerationHost())
                {
                    options.RuntimeMode = nameof(RuntimeMode.Cloud);
                }
            })
            .ValidateOnStart();

        var raw = configuration[$"{BinexusRuntimeOptions.SectionName}:RuntimeMode"];
        if (string.IsNullOrWhiteSpace(raw) && IsOpenApiDocumentGenerationHost())
        {
            raw = nameof(RuntimeMode.Cloud);
        }

        var mode = RuntimeModeParser.ParseRequired(raw);

        switch (mode)
        {
            case RuntimeMode.Cloud:
                services.AddCloudRuntime(configuration);
                break;
            case RuntimeMode.Branch:
                services.AddBranchRuntime(configuration);
                break;
            default:
                throw new InvalidOperationException("Unsupported Binexus runtime mode.");
        }

        return services;
    }

    internal static bool IsOpenApiDocumentGenerationHost()
    {
        var entry = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
        if (entry.Contains("GetDocument", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("dotnet-getdocument", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("ApiDescription", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Some hosts leave entry as the app assembly; detect via command-line args.
        var args = Environment.GetCommandLineArgs();
        return args.Any(a =>
            a.Contains("getdocument", StringComparison.OrdinalIgnoreCase)
            || a.Contains("ApiDescription", StringComparison.OrdinalIgnoreCase));
    }
}
