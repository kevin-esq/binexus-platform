using Binexus.Platform.Branching.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Branching.Credentials;

/// <summary>
/// Production/Staging Branch hosts must not start until a secure OS credential provider exists.
/// </summary>
public sealed class BranchCredentialStoreOptionsValidator(
    IHostEnvironment environment) : IValidateOptions<BranchCredentialStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, BranchCredentialStoreOptions options)
    {
        if (environment.IsProduction()
            || string.Equals(environment.EnvironmentName, "Staging", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "BranchCredentialStore: production credential store is not available yet. "
                + "DevelopmentFile and InMemory are forbidden in Production/Staging.");
        }

        if (options.Provider is not ("InMemory" or "DevelopmentFile" or "None"))
        {
            return ValidateOptionsResult.Fail(
                "BranchCredentialStore:Provider must be InMemory, DevelopmentFile, or None.");
        }

        if (environment.IsEnvironment("Testing") && options.Provider is "DevelopmentFile")
        {
            return ValidateOptionsResult.Fail(
                "BranchCredentialStore:Provider DevelopmentFile is not allowed in Testing.");
        }

        return ValidateOptionsResult.Success;
    }
}
