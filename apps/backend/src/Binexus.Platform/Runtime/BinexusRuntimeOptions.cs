namespace Binexus.Platform.Runtime;

/// <summary>
/// Bound from configuration section <c>Binexus</c>.
/// Uses a string so a missing key is not silently treated as <see cref="RuntimeMode.Cloud"/> (enum default 0).
/// </summary>
public sealed class BinexusRuntimeOptions
{
    public const string SectionName = "Binexus";

    /// <summary>Raw <c>Binexus:RuntimeMode</c> value. Null means the key was absent.</summary>
    public string? RuntimeMode { get; set; }
}
