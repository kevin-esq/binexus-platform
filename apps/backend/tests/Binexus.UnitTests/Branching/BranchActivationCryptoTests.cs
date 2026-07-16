using Binexus.Platform.Branching.Crypto;
using FluentAssertions;

namespace Binexus.UnitTests.Branching;

public sealed class BranchActivationCryptoTests
{
    [Fact]
    public void Ecdsa_round_trip_binds_payload_to_public_key()
    {
        var keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        try
        {
            var payload = CanonicalActivationChallengeCodec.Encode(new CanonicalActivationChallenge(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                EcdsaP256ActivationCrypto.Fingerprint(keyPair.PublicKey),
                new string('a', 64),
                "nonce",
                new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)));

            var signature = EcdsaP256ActivationCrypto.Sign(payload, keyPair.PrivateKeyPkcs8);

            EcdsaP256ActivationCrypto.Verify(payload, keyPair.PublicKey, signature).Should().BeTrue();
            EcdsaP256ActivationCrypto.Verify([1, 2, 3], keyPair.PublicKey, signature).Should().BeFalse();
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyPair.PrivateKeyPkcs8);
        }
    }

    [Fact]
    public void Canonical_payload_round_trips_all_bound_fields()
    {
        var challenge = new CanonicalActivationChallenge(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new string('b', 64),
            new string('c', 64),
            "nonce",
            DateTimeOffset.UtcNow);

        CanonicalActivationChallengeCodec.Decode(CanonicalActivationChallengeCodec.Encode(challenge))
            .Should()
            .BeEquivalentTo(challenge);
    }

    [Theory]
    [InlineData("BNX-ABCDE-12345")]
    [InlineData("abcde 12345")]
    public void Activation_code_normalizes_only_valid_crockford_values(string value)
    {
        BranchActivationCode.Normalize(value).Should().Be("ABCDE12345");
    }

    [Theory]
    [InlineData("BNX-ABCDE-IOU00")]
    [InlineData("BNX-ABCDE-1234!")]
    public void Activation_code_rejects_ambiguous_or_invalid_characters(string value)
    {
        var action = () => BranchActivationCode.Normalize(value);
        action.Should().Throw<FormatException>();
    }
}
