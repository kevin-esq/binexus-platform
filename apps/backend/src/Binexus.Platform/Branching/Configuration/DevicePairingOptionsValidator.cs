using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Branching.Configuration;

public sealed class DevicePairingOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<DevicePairingOptions>
{
    public ValidateOptionsResult Validate(string? name, DevicePairingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CodePepper) || options.CodePepper.Length < 32)
        {
            return ValidateOptionsResult.Fail("BranchPairing:CodePepper must contain at least 32 characters.");
        }

        if (!environment.IsDevelopment()
            && string.Equals(
                options.CodePepper,
                DevicePairingOptions.KnownDevelopmentPepper,
                StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "BranchPairing:CodePepper cannot use the known development pepper outside Development.");
        }

        if (options.CodeTtl <= TimeSpan.Zero || options.RequestTtl <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("BranchPairing TTLs must be positive.");
        }

        return ValidateOptionsResult.Success;
    }
}
