using System.Text;

namespace Binexus.Modules.Identity.Infrastructure;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; set; }

    public TimeSpan RefreshTokenLifetime { get; set; }

    public TimeSpan ClockSkew { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer)
            || string.IsNullOrWhiteSpace(Audience)
            || Encoding.UTF8.GetByteCount(SigningKey) < 32
            || AccessTokenLifetime <= TimeSpan.Zero
            || RefreshTokenLifetime <= TimeSpan.Zero
            || ClockSkew < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Jwt requires Issuer, Audience, a SigningKey of at least 32 bytes, positive token lifetimes, and non-negative ClockSkew.");
        }
    }
}
