using System.Security.Cryptography;
using System.Text.Json;
using Binexus.Platform.Branching.Crypto;
using FluentAssertions;

namespace Binexus.UnitTests.Branching;

/// <summary>
/// Deterministic golden vectors for PR5 Rust↔C# crypto interoperability spike.
/// Export: <c>$env:BINEXUS_EXPORT_GOLDEN_VECTORS=1; dotnet test ... --filter FullyQualifiedName~DevicePairingGoldenVectorTests</c>
/// </summary>
public sealed class DevicePairingGoldenVectorTests
{
    private static readonly JsonSerializerOptions ExportJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly Guid ChallengeId = Guid.Parse("0197a1b0-c3d4-7890-abcd-ef1234567890");
    private static readonly Guid BranchInstanceId = Guid.Parse("0197a1b0-c3d4-7890-abcd-ef1234567891");
    private static readonly Guid PairingSessionId = Guid.Parse("0197a1b0-c3d4-7890-abcd-ef1234567892");
    private static readonly Guid DeviceId = Guid.Parse("0197a1b0-c3d4-7890-abcd-ef1234567893");
    private static readonly Guid PairingRequestId = Guid.Parse("0197a1b0-c3d4-7890-abcd-ef1234567894");
    private static readonly Guid TerminalId = Guid.Parse("0197a1b0-c3d4-7890-abcd-ef1234567895");
    private static readonly Guid ConfirmationChallengeId = Guid.Parse("0197a1b0-c3d4-7890-abcd-ef1234567896");
    private static readonly Guid ReissueChallengeId = Guid.Parse("0197a1b0-c3d4-7890-abcd-ef1234567897");

    private const string CredentialHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NonceExchange = "exchange-nonce-0123456789abcdef";
    private const string NonceConfirm = "confirm-nonce-0123456789abcdef";
    private const string NonceReissue = "reissue-nonce-0123456789abcdef";
    private const string PairingReceipt = "receipt-secret-base64url-example-vector-not-production";
    private static readonly DateTimeOffset ExpiresAtUtc = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] PrivateKeyPkcs8 = CreateDeterministicPrivateKeyPkcs8();

    [Fact]
    public void Golden_vectors_match_expected_codec_and_signatures()
    {
        var publicKey = ExportPublicKeyBase64Url(PrivateKeyPkcs8);
        var fingerprint = EcdsaP256ActivationCrypto.Fingerprint(publicKey);

        var exchangePayload = CanonicalDevicePairingChallengeCodec.EncodeExchange(
            new CanonicalDevicePairingExchangeChallenge(
                ChallengeId,
                BranchInstanceId,
                PairingSessionId,
                DeviceId,
                fingerprint,
                CredentialHash,
                NonceExchange,
                ExpiresAtUtc));
        var exchangeSignature = EcdsaP256ActivationCrypto.Sign(exchangePayload, PrivateKeyPkcs8);

        var confirmPayload = CanonicalDevicePairingChallengeCodec.EncodeConfirmation(
            new CanonicalDevicePairingConfirmChallenge(
                ConfirmationChallengeId,
                PairingRequestId,
                BranchInstanceId,
                DeviceId,
                TerminalId,
                fingerprint,
                CredentialHash,
                PairingSecret.Hash(PairingReceipt),
                NonceConfirm,
                ExpiresAtUtc));
        var confirmSignature = EcdsaP256ActivationCrypto.Sign(confirmPayload, PrivateKeyPkcs8);

        var reissuePayload = CanonicalDevicePairingChallengeCodec.EncodeReceiptReissue(
            new CanonicalDevicePairingReceiptReissueChallenge(
                ReissueChallengeId,
                PairingRequestId,
                BranchInstanceId,
                DeviceId,
                fingerprint,
                CredentialHash,
                NonceReissue,
                ExpiresAtUtc));
        var reissueSignature = EcdsaP256ActivationCrypto.Sign(reissuePayload, PrivateKeyPkcs8);

        EcdsaP256ActivationCrypto.Verify(exchangePayload, publicKey, exchangeSignature).Should().BeTrue();
        EcdsaP256ActivationCrypto.Verify(confirmPayload, publicKey, confirmSignature).Should().BeTrue();
        EcdsaP256ActivationCrypto.Verify(reissuePayload, publicKey, reissueSignature).Should().BeTrue();

        var document = new GoldenVectorDocument(
            DevicePairingCryptoFormats.ExchangeChallengeVersion,
            DevicePairingCryptoFormats.ConfirmationChallengeVersion,
            DevicePairingCryptoFormats.ReceiptReissueChallengeVersion,
            publicKey,
            fingerprint,
            DevicePairingFingerprint.ToShortDisplay(fingerprint),
            Convert.ToBase64String(PrivateKeyPkcs8),
            new GoldenVectorCase("exchange", ToHex(exchangePayload), exchangeSignature),
            new GoldenVectorCase("confirm", ToHex(confirmPayload), confirmSignature),
            new GoldenVectorCase("receipt-reissue", ToHex(reissuePayload), reissueSignature));

        if (string.Equals(Environment.GetEnvironmentVariable("BINEXUS_EXPORT_GOLDEN_VECTORS"), "1", StringComparison.Ordinal))
        {
            Export(document);
        }
    }

    private static byte[] CreateDeterministicPrivateKeyPkcs8()
    {
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Enumerable.Range(1, 32).Select(static i => (byte)i).ToArray(),
        };

        using var algorithm = ECDsa.Create();
        algorithm.ImportParameters(parameters);
        return algorithm.ExportPkcs8PrivateKey();
    }

    private static string ExportPublicKeyBase64Url(ReadOnlySpan<byte> privateKeyPkcs8)
    {
        using var algorithm = ECDsa.Create();
        algorithm.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        return Base64Url.Encode(algorithm.ExportSubjectPublicKeyInfo());
    }

    private static string ToHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static void Export(GoldenVectorDocument document)
    {
        var path = FindFixturePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(document, ExportJson);
        File.WriteAllText(path, json);
    }

    private static string FindFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fixturesDir = Path.Join(dir.FullName, "apps", "desktop", "spikes", "fixtures");
            if (Directory.Exists(Path.Join(dir.FullName, "apps", "desktop")))
            {
                Directory.CreateDirectory(fixturesDir);
                return Path.Join(fixturesDir, "pairing-crypto-golden-v1.json");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate apps/desktop/spikes/fixtures directory.");
    }

    private sealed record GoldenVectorDocument(
        string ExchangeVersion,
        string ConfirmVersion,
        string ReceiptReissueVersion,
        string PublicKeyBase64Url,
        string PublicKeyFingerprintSha256Hex,
        string FingerprintShortDisplay,
        string PrivateKeyPkcs8Base64,
        GoldenVectorCase Exchange,
        GoldenVectorCase Confirm,
        GoldenVectorCase ReceiptReissue);

    private sealed record GoldenVectorCase(string Name, string CanonicalPayloadHex, string SignatureBase64Url);
}
