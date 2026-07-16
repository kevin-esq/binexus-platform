using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Branching.Configuration;

public sealed class CloudActivationOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<CloudActivationOptions>
{
    public ValidateOptionsResult Validate(string? name, CloudActivationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CodePepper) || options.CodePepper.Length < 32)
        {
            return ValidateOptionsResult.Fail("CloudActivation:CodePepper must contain at least 32 characters.");
        }

        if (!environment.IsDevelopment()
            && string.Equals(
                options.CodePepper,
                CloudActivationOptions.KnownDevelopmentPepper,
                StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "CloudActivation:CodePepper cannot use the known development pepper outside Development.");
        }

        return ValidateOptionsResult.Success;
    }
}
