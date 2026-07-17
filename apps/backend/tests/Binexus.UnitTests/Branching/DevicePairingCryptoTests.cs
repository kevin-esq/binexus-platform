using System.Security.Cryptography;
using Binexus.Platform.Branching.Crypto;
using Binexus.Platform.Branching.Pairing;
using FluentAssertions;

namespace Binexus.UnitTests.Branching;

public sealed class DevicePairingCryptoTests
{
    [Fact]
    public void Pairing_code_generates_eight_digits_and_normalizes_grouping()
    {
        var code = PairingCode.Generate();

        code.Should().MatchRegex(@"^\d{4}-\d{4}$");
        PairingCode.Normalize(code).Should().HaveLength(PairingCode.DigitCount);
        PairingCode.Normalize(code).Should().MatchRegex(@"^\d{8}$");
    }

    [Theory]
    [InlineData("1234-5678", "12345678")]
    [InlineData("1234 5678", "12345678")]
    [InlineData("12345678", "12345678")]
    public void Pairing_code_normalizes_separators(string input, string expected)
    {
        PairingCode.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("1234-567")]
    [InlineData("1234-567A")]
    [InlineData("123456789")]
    public void Pairing_code_rejects_non_eight_digit_values(string input)
    {
        var act = () => PairingCode.Normalize(input);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Pairing_code_hash_is_deterministic_and_pepper_sensitive()
    {
        const string pepper = "unit-test-branch-pairing-pepper-0000000000";
        var hashA = PairingCode.Hash("1234-5678", pepper);

        hashA.Should().Be(PairingCode.Hash("12345678", pepper));
        hashA.Should().NotBe(PairingCode.Hash("1234-5678", pepper + "x"));
        hashA.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Pairing_secret_is_high_entropy_and_hash_round_trips()
    {
        var secret = PairingSecret.Generate();
        var other = PairingSecret.Generate();

        secret.Should().NotBe(other);
        PairingSecret.FixedTimeEqualsHash(PairingSecret.Hash(secret), PairingSecret.Hash(secret)).Should().BeTrue();
        PairingSecret.FixedTimeEqualsHash(PairingSecret.Hash(secret), PairingSecret.Hash(other)).Should().BeFalse();
    }

    [Fact]
    public void Fingerprint_short_display_is_stable_and_grouped()
    {
        const string full = "a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00";

        DevicePairingFingerprint.ToShortDisplay(full).Should().Be("A1B2-C3D4-E5F6");
    }

    [Fact]
    public void Receipt_reissue_signature_binds_request_without_receipt()
    {
        var keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        try
        {
            var fingerprint = EcdsaP256ActivationCrypto.Fingerprint(keyPair.PublicKey);
            var challenge = new CanonicalDevicePairingReceiptReissueChallenge(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                fingerprint,
                PairingSecret.Hash(PairingSecret.Generate()),
                PairingSecret.Generate(),
                DateTimeOffset.UtcNow.AddMinutes(5));
            var payload = CanonicalDevicePairingChallengeCodec.EncodeReceiptReissue(challenge);
            var signature = EcdsaP256ActivationCrypto.Sign(payload, keyPair.PrivateKeyPkcs8);

            EcdsaP256ActivationCrypto.Verify(payload, keyPair.PublicKey, signature).Should().BeTrue();
            EcdsaP256ActivationCrypto.Verify(payload, keyPair.PublicKey, signature + "x").Should().BeFalse();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyPair.PrivateKeyPkcs8);
        }
    }

    [Fact]
    public void Exchange_signature_binds_every_field()
    {
        var keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        try
        {
            var fingerprint = EcdsaP256ActivationCrypto.Fingerprint(keyPair.PublicKey);
            var challenge = new CanonicalDevicePairingExchangeChallenge(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                fingerprint,
                new string('a', 64),
                PairingSecret.Generate(),
                new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
            var payload = CanonicalDevicePairingChallengeCodec.EncodeExchange(challenge);
            var signature = EcdsaP256ActivationCrypto.Sign(payload, keyPair.PrivateKeyPkcs8);

            EcdsaP256ActivationCrypto.Verify(payload, keyPair.PublicKey, signature).Should().BeTrue();

            var tampered = CanonicalDevicePairingChallengeCodec.EncodeExchange(challenge with { Nonce = "tampered" });
            EcdsaP256ActivationCrypto.Verify(tampered, keyPair.PublicKey, signature).Should().BeFalse();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyPair.PrivateKeyPkcs8);
        }
    }

    [Fact]
    public void Confirmation_signature_binds_terminal_and_receipt_hash()
    {
        var keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        try
        {
            var fingerprint = EcdsaP256ActivationCrypto.Fingerprint(keyPair.PublicKey);
            var challenge = new CanonicalDevicePairingConfirmChallenge(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                fingerprint,
                new string('a', 64),
                PairingSecret.Hash(PairingSecret.Generate()),
                PairingSecret.Generate(),
                new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
            var payload = CanonicalDevicePairingChallengeCodec.EncodeConfirmation(challenge);
            var signature = EcdsaP256ActivationCrypto.Sign(payload, keyPair.PrivateKeyPkcs8);

            EcdsaP256ActivationCrypto.Verify(payload, keyPair.PublicKey, signature).Should().BeTrue();

            var tampered = CanonicalDevicePairingChallengeCodec.EncodeConfirmation(
                challenge with { TerminalId = Guid.CreateVersion7() });
            EcdsaP256ActivationCrypto.Verify(tampered, keyPair.PublicKey, signature).Should().BeFalse();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyPair.PrivateKeyPkcs8);
        }
    }

    [Theory]
    [InlineData("Caja 1", "caja 1")]
    [InlineData("  POS-01  ", "pos-01")]
    public void Terminal_name_normalizes_trim_and_case(string input, string expected)
    {
        TerminalName.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Terminal_name_rejects_blank(string input)
    {
        var act = () => TerminalName.Validate(input);
        act.Should().Throw<FormatException>();
    }
}
