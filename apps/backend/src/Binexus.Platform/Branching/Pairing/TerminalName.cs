namespace Binexus.Platform.Branching.Pairing;

/// <summary>
/// Terminal display-name rules. Uniqueness is enforced on an explicitly stored normalized value
/// (trim + invariant lowercase) rather than an expression index, so the constraint is stable across
/// EF/PostgreSQL. Length matches the existing Sales terminal-label bounds (1..50).
/// </summary>
public static class TerminalName
{
    public const int MinLength = 1;
    public const int MaxLength = 50;

    public static string Validate(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < MinLength or > MaxLength)
        {
            throw new FormatException($"Terminal name must be between {MinLength} and {MaxLength} characters.");
        }

        return trimmed;
    }

    public static string Normalize(string name) => Validate(name).ToLowerInvariant();
}
