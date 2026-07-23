using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Branching.Configuration;

public sealed class BranchDeviceAuthOptionsValidator(
    IHostEnvironment environment,
    IConfiguration configuration) : IValidateOptions<BranchDeviceAuthOptions>
{
    private static readonly string[] LabKeyMarkers =
    [
        "development-only",
        "integration-test",
        "test-only",
        "lab-only",
    ];

    public ValidateOptionsResult Validate(string? name, BranchDeviceAuthOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CurrentKeyId))
        {
            failures.Add("BranchDeviceAuth:CurrentKeyId is required.");
        }

        if (options.SigningKeys is null || options.SigningKeys.Count == 0)
        {
            failures.Add("BranchDeviceAuth:SigningKeys must contain at least one key.");
        }

        // Prefer explicit IpPermitLimit; fall back to MachinePermitLimit when Ip left at default and Machine differs.
        if (options.MachinePermitLimit is >= 1 and <= 1000
            && options.IpPermitLimit == 30
            && options.MachinePermitLimit != 30)
        {
            options.IpPermitLimit = options.MachinePermitLimit;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        BranchDeviceAuthSigningKey? current = null;
        if (options.SigningKeys is not null)
        {
            foreach (var key in options.SigningKeys)
            {
                if (string.IsNullOrWhiteSpace(key.KeyId) || string.IsNullOrWhiteSpace(key.Key))
                {
                    failures.Add("BranchDeviceAuth signing keys require KeyId and Key.");
                    continue;
                }

                var keyBytes = Encoding.UTF8.GetByteCount(key.Key);
                if (key.Key.Length < 32 || keyBytes < 32)
                {
                    failures.Add($"BranchDeviceAuth key '{key.KeyId}' must be at least 32 UTF-8 bytes.");
                }

                if (!ids.Add(key.KeyId))
                {
                    failures.Add($"BranchDeviceAuth duplicate KeyId '{key.KeyId}'.");
                }

                if (string.Equals(key.KeyId, options.CurrentKeyId, StringComparison.Ordinal))
                {
                    current = key;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(options.CurrentKeyId) && current is null)
        {
            failures.Add(
                $"BranchDeviceAuth:CurrentKeyId '{options.CurrentKeyId}' is not present in SigningKeys.");
        }

        if (options.TokenLifetimeSeconds is < 60 or > 3600)
        {
            failures.Add("BranchDeviceAuth:TokenLifetimeSeconds must be between 60 and 3600.");
        }

        if (options.ClockSkewSeconds is < 0 or > 120)
        {
            failures.Add("BranchDeviceAuth:ClockSkewSeconds must be between 0 and 120.");
        }

        if (options.StatusCacheSeconds is < 1 or > 120)
        {
            failures.Add("BranchDeviceAuth:StatusCacheSeconds must be between 1 and 120.");
        }

        if (options.ChallengeTtlSeconds is < 15 or > 300)
        {
            failures.Add("BranchDeviceAuth:ChallengeTtlSeconds must be between 15 and 300.");
        }

        if (options.IpPermitLimit is < 1 or > 1000
            || options.DevicePermitLimit is < 1 or > 1000
            || options.GlobalPermitLimit is < 1 or > 5000
            || options.RateLimitWindowSeconds is < 1 or > 600)
        {
            failures.Add("BranchDeviceAuth rate-limit settings are out of range.");
        }

        var jwtSigningKey = configuration["Jwt:SigningKey"];
        if (!string.IsNullOrEmpty(jwtSigningKey) && options.SigningKeys is not null)
        {
            var jwtBytes = Encoding.UTF8.GetBytes(jwtSigningKey);
            foreach (var key in options.SigningKeys)
            {
                var datBytes = Encoding.UTF8.GetBytes(key.Key);
                if (CryptographicOperations.FixedTimeEquals(
                        SHA256.HashData(jwtBytes),
                        SHA256.HashData(datBytes))
                    || FixedTimeEqualsUtf8(jwtBytes, datBytes))
                {
                    failures.Add("BranchDeviceAuth signing keys must be distinct from Jwt:SigningKey.");
                    break;
                }
            }
        }

        var isProdLike = !environment.IsDevelopment()
            && !environment.IsEnvironment("Testing");

        if (isProdLike && current is not null)
        {
            if (current.LabOnly || LooksLikeLabKey(current.Key) || LooksLikeLabKey(current.KeyId))
            {
                failures.Add(
                    "BranchDeviceAuth current signing key is marked or detected as lab/dev and cannot be used outside Development/Testing.");
            }
        }

        if (isProdLike && options.AllowInsecureBranchTransport)
        {
            // Explicit opt-in outside Dev/Testing is allowed but warned at boot; not a hard fail.
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool LooksLikeLabKey(string value) =>
        LabKeyMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool FixedTimeEqualsUtf8(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
