using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Binexus.Modules.Identity.Application;
using Isopoh.Cryptography.Argon2;

namespace Binexus.Modules.Identity.Infrastructure;

/// <summary>
/// Argon2id hasher compatible with Node <c>argon2@0.41.x</c> PHC strings.
/// Rejects hostile embedded parameters and bounds password length before hashing.
/// </summary>
public sealed partial class Argon2PasswordHasher : IPasswordHasher
{
    public const int MemoryCost = 65536;
    public const int TimeCost = 3;
    public const int Parallelism = 4;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    /// <summary>Upper bounds when verifying stored hashes (DoS protection).</summary>
    public const int MaxAcceptedMemoryCost = 262_144; // 256 MiB

    public const int MaxAcceptedTimeCost = 10;

    public const int MaxAcceptedParallelism = 8;

    private static readonly SemaphoreSlim Gate = new(initialCount: 2, maxCount: 2);

    public async Task<string> HashAsync(string password, CancellationToken cancellationToken = default)
    {
        var passwordBytes = GetBoundedPasswordBytes(password);
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var config = CreateConfig(passwordBytes, RandomNumberGenerator.GetBytes(SaltLength));
            using var argon2 = new Argon2(config);
            using var hash = argon2.Hash();
            return config.EncodeString(hash.Buffer);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<bool> VerifyAsync(string passwordHash, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        if (!TryValidateStoredParameters(passwordHash, out _))
        {
            return false;
        }

        byte[] passwordBytes;
        try
        {
            passwordBytes = GetBoundedPasswordBytes(password);
        }
        catch (ArgumentException)
        {
            return false;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Argon2.Verify(passwordHash, passwordBytes, Parallelism);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    public bool NeedsRehash(string passwordHash)
    {
        if (!TryValidateStoredParameters(passwordHash, out var parameters))
        {
            return true;
        }

        return parameters.MemoryCost != MemoryCost
            || parameters.TimeCost != TimeCost
            || parameters.Parallelism != Parallelism
            || !string.Equals(parameters.Variant, "argon2id", StringComparison.Ordinal);
    }

    public static bool TryValidateStoredParameters(string passwordHash, out Argon2Parameters parameters)
    {
        parameters = default;
        var match = PhcRegex().Match(passwordHash);
        if (!match.Success)
        {
            return false;
        }

        var variant = match.Groups["variant"].Value;
        if (!string.Equals(variant, "argon2id", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(match.Groups["m"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var memory)
            || !int.TryParse(match.Groups["t"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var time)
            || !int.TryParse(match.Groups["p"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parallelism))
        {
            return false;
        }

        if (memory is < 8 or > MaxAcceptedMemoryCost
            || time is < 1 or > MaxAcceptedTimeCost
            || parallelism is < 1 or > MaxAcceptedParallelism)
        {
            return false;
        }

        parameters = new Argon2Parameters(variant, memory, time, parallelism);
        return true;
    }

    private static Argon2Config CreateConfig(byte[] passwordBytes, byte[] salt) =>
        new()
        {
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = TimeCost,
            MemoryCost = MemoryCost,
            Lanes = Parallelism,
            Threads = Parallelism,
            Password = passwordBytes,
            Salt = salt,
            HashLength = HashLength,
        };

    private static byte[] GetBoundedPasswordBytes(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var bytes = Encoding.UTF8.GetBytes(password);
        if (bytes.Length is < IPasswordHasher.MinPasswordUtf8Bytes or > IPasswordHasher.MaxPasswordUtf8Bytes)
        {
            throw new ArgumentException(
                $"Password must be between {IPasswordHasher.MinPasswordUtf8Bytes} and {IPasswordHasher.MaxPasswordUtf8Bytes} UTF-8 bytes.",
                nameof(password));
        }

        return bytes;
    }

    [GeneratedRegex(
        @"^\$(?<variant>argon2(?:id|i|d))\$v=19\$m=(?<m>\d+),t=(?<t>\d+),p=(?<p>\d+)\$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex PhcRegex();
}

public readonly record struct Argon2Parameters(string Variant, int MemoryCost, int TimeCost, int Parallelism);
