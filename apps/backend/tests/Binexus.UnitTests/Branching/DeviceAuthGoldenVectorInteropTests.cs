using System.Globalization;
using System.Text.Json;
using Binexus.Platform.Branching.Crypto;
using Binexus.Platform.Branching.DeviceAuth;
using FluentAssertions;

namespace Binexus.UnitTests.Branching;

public sealed class DeviceAuthGoldenVectorInteropTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Canonical_bytes_and_shared_rust_signature_verify_in_csharp()
    {
        var golden = LoadFixture();
        var challenge = golden.Challenge;
        var canonical = CanonicalDeviceAuthChallengeCodec.Encode(new CanonicalDeviceAuthChallenge(
            Guid.Parse(challenge.ChallengeId),
            Guid.Parse(challenge.BranchInstanceId),
            Guid.Parse(challenge.DeviceId),
            challenge.PublicKeyFingerprint,
            challenge.CredentialHash,
            challenge.Nonce,
            DateTimeOffset.Parse(challenge.ExpiresAtUtc, CultureInfo.InvariantCulture)));

        canonical.Should().Equal(Convert.FromHexString(challenge.CanonicalPayloadHex));
        EcdsaP256ActivationCrypto.Verify(canonical, golden.PublicKeyBase64Url, challenge.SignatureBase64Url)
            .Should().BeTrue();
    }

    [Fact]
    public void Noncanonical_or_mutated_inputs_do_not_match_golden_bytes_or_signature()
    {
        var golden = LoadFixture();
        var challenge = golden.Challenge;
        var expected = Convert.FromHexString(challenge.CanonicalPayloadHex);

        Encode(challenge with { Nonce = "changed-nonce" }).Should().NotEqual(expected);
        Encode(challenge with { BranchInstanceId = "0194f0a0-0000-7000-8000-000000000004" }).Should().NotEqual(expected);
        Encode(challenge with { CredentialHash = challenge.CredentialHash.ToUpperInvariant() }).Should().NotEqual(expected);
        Encode(challenge with { ExpiresAtUtc = "2026-07-18T12:00:01.0000000Z" }).Should().NotEqual(expected);

        // Canonical UUIDs and the documented field order are part of the signed wire contract.
        Guid.Parse(challenge.DeviceId).ToString("D").Should().Be(challenge.DeviceId);
        var wrongFieldOrder = CanonicalDeviceAuthChallengeCodec.Encode(new CanonicalDeviceAuthChallenge(
            Guid.Parse(challenge.ChallengeId),
            Guid.Parse(challenge.DeviceId),
            Guid.Parse(challenge.BranchInstanceId),
            challenge.PublicKeyFingerprint,
            challenge.CredentialHash,
            challenge.Nonce,
            DateTimeOffset.Parse(challenge.ExpiresAtUtc, CultureInfo.InvariantCulture)));
        wrongFieldOrder.Should().NotEqual(expected);
    }

    private static byte[] Encode(GoldenChallenge challenge) =>
        CanonicalDeviceAuthChallengeCodec.Encode(new CanonicalDeviceAuthChallenge(
            Guid.Parse(challenge.ChallengeId),
            Guid.Parse(challenge.BranchInstanceId),
            Guid.Parse(challenge.DeviceId),
            challenge.PublicKeyFingerprint,
            challenge.CredentialHash,
            challenge.Nonce,
            DateTimeOffset.Parse(challenge.ExpiresAtUtc, CultureInfo.InvariantCulture)));

    private static GoldenVector LoadFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Join(directory.FullName, "apps", "desktop", "spikes", "fixtures", "device-auth-crypto-golden-v1.json");
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<GoldenVector>(
                    File.ReadAllText(path),
                    JsonOptions)
                    ?? throw new InvalidOperationException("Could not parse device-auth golden fixture.");
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate device-auth golden fixture.");
    }

    private sealed record GoldenVector(string PublicKeyBase64Url, string PrivateKeyPkcs8Base64, GoldenChallenge Challenge);

    private sealed record GoldenChallenge(
        string ChallengeId,
        string Nonce,
        string DeviceId,
        string BranchInstanceId,
        string CredentialHash,
        string PublicKeyFingerprint,
        string ExpiresAtUtc,
        string CanonicalPayloadHex,
        string SignatureBase64Url);
}
