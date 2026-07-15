using Microsoft.Extensions.Options;

namespace Binexus.Platform.Runtime;

public sealed class BinexusRuntimeOptionsValidator : IValidateOptions<BinexusRuntimeOptions>
{
    public ValidateOptionsResult Validate(string? name, BinexusRuntimeOptions options)
    {
        if (!RuntimeModeParser.TryParse(options.RuntimeMode, out _, out var error))
        {
            return ValidateOptionsResult.Fail(error);
        }

        return ValidateOptionsResult.Success;
    }
}
