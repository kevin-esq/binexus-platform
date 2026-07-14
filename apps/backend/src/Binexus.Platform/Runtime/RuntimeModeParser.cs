namespace Binexus.Platform.Runtime;

/// <summary>
/// Parses <c>Binexus:RuntimeMode</c>. Leading/trailing whitespace is trimmed; empty after trim is invalid.
/// Matching is case-insensitive. Internal whitespace (e.g. <c>Cl oud</c>) is rejected.
/// </summary>
public static class RuntimeModeParser
{
    public static bool TryParse(string? raw, out RuntimeMode mode, out string error)
    {
        mode = default;

        if (raw is null)
        {
            error = "Binexus:RuntimeMode is required. Set Cloud or Branch (for example Binexus__RuntimeMode=Cloud).";
            return false;
        }

        var value = raw.Trim();
        if (value.Length == 0)
        {
            error = "Binexus:RuntimeMode is empty. Set Cloud or Branch.";
            return false;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out mode) || !Enum.IsDefined(mode))
        {
            error =
                $"Binexus:RuntimeMode value '{value}' is invalid. Allowed values: Cloud, Branch.";
            mode = default;
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static RuntimeMode ParseRequired(string? raw)
    {
        if (!TryParse(raw, out var mode, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return mode;
    }
}
