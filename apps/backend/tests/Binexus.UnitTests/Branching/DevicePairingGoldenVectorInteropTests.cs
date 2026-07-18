using System.Text.Json;
using Binexus.Platform.Branching.Crypto;
using FluentAssertions;

namespace Binexus.UnitTests.Branching;

/// <summary>
/// Cross-language verification: C# golden fixture signatures verified in prior tests;
/// Rust-generated signatures (from crypto-interop-spike) must verify in C#.
/// </summary>
public sealed class DevicePairingGoldenVectorInteropTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Fixture_csharp_signatures_verify_with_public_key_from_fixture()
    {
        var golden = LoadGoldenFixture();
        var publicKey = golden.PublicKeyBase64Url;

        VerifyCase(golden.Exchange, publicKey);
        VerifyCase(golden.Confirm, publicKey);
        VerifyCase(golden.ReceiptReissue, publicKey);
    }

    [Fact]
    public void Fixture_rust_signatures_verify_in_csharp()
    {
        var golden = LoadGoldenFixture();
        var rust = LoadRustSignaturesFixture();
        var publicKey = golden.PublicKeyBase64Url;

        foreach (var rustSig in rust.Signatures)
        {
            var caseName = rustSig.Name switch
            {
                "exchange" => golden.Exchange,
                "confirm" => golden.Confirm,
                "receipt-reissue" => golden.ReceiptReissue,
                _ => throw new InvalidOperationException($"Unknown rust signature case: {rustSig.Name}"),
            };

            var payload = Convert.FromHexString(caseName.CanonicalPayloadHex);
            EcdsaP256ActivationCrypto.Verify(payload, publicKey, rustSig.SignatureBase64Url)
                .Should()
                .BeTrue($"Rust signature for {rustSig.Name} must verify in C#");
        }
    }

    private static void VerifyCase(GoldenVectorCase @case, string publicKey)
    {
        var payload = Convert.FromHexString(@case.CanonicalPayloadHex);
        EcdsaP256ActivationCrypto.Verify(payload, publicKey, @case.SignatureBase64Url)
            .Should()
            .BeTrue($"{@case.Name} C# signature must verify");
    }

    private static GoldenVectorDocument LoadGoldenFixture()
    {
        var path = Path.Join(FixturesDirectory(), "pairing-crypto-golden-v1.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GoldenVectorDocument>(json, Json)
            ?? throw new InvalidOperationException("Could not parse golden fixture.");
    }

    private static RustSignaturesDocument LoadRustSignaturesFixture()
    {
        var path = Path.Join(FixturesDirectory(), "pairing-crypto-rust-signatures-v1.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RustSignaturesDocument>(json, Json)
            ?? throw new InvalidOperationException("Could not parse rust signatures fixture.");
    }

    private static string FixturesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fixturesDir = Path.Join(dir.FullName, "apps", "desktop", "spikes", "fixtures");
            if (Directory.Exists(fixturesDir))
            {
                return fixturesDir;
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

    private sealed record RustSignaturesDocument(string Source, string Toolchain, IReadOnlyList<RustSignatureCase> Signatures);

    private sealed record RustSignatureCase(string Name, string SignatureBase64Url);
}
