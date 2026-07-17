namespace Binexus.Platform.Branching.Crypto;

/// <summary>
/// Display-only short fingerprint for the human approval step. Derived from the full 64-char
/// SHA-256 SPKI fingerprint. Never used for cryptographic decisions — Branch always compares the
/// full 64 characters. The same format is shown on the admin screen and (in PR 5) the Tauri client.
/// </summary>
public static class DevicePairingFingerprint
{
    public const int ShortHexLength = 12;

    /// <summary>Formats the first 12 hex characters as <c>A1B2-C3D4-E5F6</c> for visual comparison.</summary>
    public static string ToShortDisplay(string publicKeyFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyFingerprint);
        if (publicKeyFingerprint.Length < ShortHexLength)
        {
            throw new FormatException("Fingerprint is shorter than the required short-display length.");
        }

        var head = publicKeyFingerprint[..ShortHexLength].ToUpperInvariant();
        return $"{head[..4]}-{head[4..8]}-{head[8..12]}";
    }
}
