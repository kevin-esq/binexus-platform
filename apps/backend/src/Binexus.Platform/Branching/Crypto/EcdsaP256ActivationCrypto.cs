using System.Security.Cryptography;

namespace Binexus.Platform.Branching.Crypto;

public sealed record ActivationKeyPair(string PublicKey, byte[] PrivateKeyPkcs8);

public static class EcdsaP256ActivationCrypto
{
    public static ActivationKeyPair GenerateKeyPair()
    {
        using var algorithm = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = algorithm.ExportPkcs8PrivateKey();
        var publicKey = Base64Url.Encode(algorithm.ExportSubjectPublicKeyInfo());
        return new ActivationKeyPair(publicKey, privateKey);
    }

    public static string Sign(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> privateKeyPkcs8)
    {
        using var algorithm = ECDsa.Create();
        algorithm.ImportPkcs8PrivateKey(privateKeyPkcs8, out var bytesRead);
        if (bytesRead != privateKeyPkcs8.Length)
        {
            throw new CryptographicException("Private key contains trailing bytes.");
        }

        return Base64Url.Encode(algorithm.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    public static bool Verify(ReadOnlySpan<byte> payload, string publicKey, string signature)
    {
        try
        {
            using var algorithm = ImportPublicKey(publicKey);
            return algorithm.VerifyData(
                payload,
                Base64Url.Decode(signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Fingerprint(string publicKey)
    {
        using var algorithm = ImportPublicKey(publicKey);
        return Convert.ToHexString(SHA256.HashData(algorithm.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    }

    public static ECDsa ImportPublicKey(string publicKey)
    {
        var encoded = Base64Url.Decode(publicKey);
        try
        {
            var algorithm = ECDsa.Create();
            algorithm.ImportSubjectPublicKeyInfo(encoded, out var bytesRead);
            if (bytesRead != encoded.Length)
            {
                algorithm.Dispose();
                throw new CryptographicException("Public key contains trailing bytes.");
            }

            return algorithm;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }
}
