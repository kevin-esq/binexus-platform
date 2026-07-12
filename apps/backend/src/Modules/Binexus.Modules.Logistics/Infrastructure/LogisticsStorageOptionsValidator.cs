using Binexus.Modules.Logistics.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Binexus.Modules.Logistics.Infrastructure;

/// <summary>
/// Validates Logistics:Storage. Production/Staging require MinIO + credentials; Local never selected via empty-creds fallback.
/// </summary>
public sealed class LogisticsStorageOptionsValidator(IHostEnvironment environment) : IValidateOptions<LogisticsStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, LogisticsStorageOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("Logistics:Storage options are required.");
        }

        if (!options.IsLocal && !options.IsMinIO)
        {
            return ValidateOptionsResult.Fail(
                "Logistics:Storage:Provider must be 'Local' or 'MinIO' (explicit; never inferred from empty credentials).");
        }

        if (options.MinPresignTtl <= TimeSpan.Zero || options.MaxPresignTtl < options.MinPresignTtl)
        {
            return ValidateOptionsResult.Fail("Logistics:Storage MinPresignTtl/MaxPresignTtl are invalid.");
        }

        if (options.MaxProofBytes <= 0)
        {
            return ValidateOptionsResult.Fail("Logistics:Storage:MaxProofBytes must be positive.");
        }

        // OpenAPI / EF design-time hosts may default to Production without secrets.
        if (IsDesignTimeHost())
        {
            return ValidateOptionsResult.Success;
        }

        var productionLike = environment.IsProduction()
            || environment.IsStaging()
            || (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"));

        if (productionLike)
        {
            if (!options.IsMinIO)
            {
                return ValidateOptionsResult.Fail(
                    "Production/Staging require Logistics:Storage:Provider=MinIO (Local is not allowed).");
            }

            return RequireMinIoCredentials(options);
        }

        if (options.IsMinIO)
        {
            return RequireMinIoCredentials(options);
        }

        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult RequireMinIoCredentials(LogisticsStorageOptions options)
    {
        var hasEndpoint = !string.IsNullOrWhiteSpace(options.Endpoint)
            || !string.IsNullOrWhiteSpace(options.InternalEndpoint);
        if (!hasEndpoint
            || string.IsNullOrWhiteSpace(options.Bucket)
            || string.IsNullOrWhiteSpace(options.AccessKey)
            || string.IsNullOrWhiteSpace(options.SecretKey))
        {
            return ValidateOptionsResult.Fail(
                "MinIO storage requires Endpoint or InternalEndpoint, plus Bucket, AccessKey, and SecretKey.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsDesignTimeHost()
    {
        var entry = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
        return entry.Contains("GetDocument", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("dotnet-getdocument", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry, "ef", StringComparison.OrdinalIgnoreCase);
    }
}
