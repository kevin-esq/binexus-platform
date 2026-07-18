using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string Service = "io.binexus.desktop.spike";
var account = $"pr5-spike-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
var envelope = SampleEnvelope();
var json = JsonSerializer.Serialize(envelope);
var reports = new List<object>();

reports.Add(Report("envelope", "serialize_v1", true, $"bytes={Encoding.UTF8.GetByteCount(json)}", 0));
reports.AddRange(RunCredentialManager($"{Service}/{account}", json));
reports.AddRange(RunDpapi(json));

Console.WriteLine(JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
if (reports.Any(static r => !(bool)r.GetType().GetProperty("ok")!.GetValue(r)!))
{
    Environment.Exit(1);
}

static object Report(string provider, string scenario, bool ok, string detail, long elapsedMs) =>
    new { provider, scenario, ok, detail, elapsed_ms = elapsedMs };

static EnvelopeV1 SampleEnvelope()
{
    var pkcs8 = new byte[121];
    RandomNumberGenerator.Fill(pkcs8);
    var credential = new byte[32];
    RandomNumberGenerator.Fill(credential);
    return new EnvelopeV1(
        1,
        "0197a1b0-c3d4-7890-abcd-ef1234567893",
        Convert.ToBase64String(pkcs8),
        Base64Url(credential),
        new PairingAttempt(
            "0197a1b0-c3d4-7890-abcd-ef1234567894",
            "status-token-spike-value-not-production",
            "receipt-spike-value-not-production"));
}

static string Base64Url(ReadOnlySpan<byte> bytes) =>
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static IEnumerable<object> RunCredentialManager(string target, string payload)
{
    yield return Timed("wcm", "create", () =>
    {
        WinCred.Write(target, payload);
        return "stored";
    });

    yield return Timed("wcm", "read", () =>
    {
        var value = WinCred.Read(target);
        return $"len={value.Length}";
    });

    yield return Timed("wcm", "overwrite", () =>
    {
        WinCred.Write(target, payload + "-v2");
        return $"len={WinCred.Read(target).Length}";
    });

    yield return Timed("wcm", "delete", () =>
    {
        WinCred.Delete(target);
        return "deleted";
    });

    yield return Timed("wcm", "missing_entry", () =>
    {
        try
        {
            _ = WinCred.Read(target);
            throw new InvalidOperationException("expected missing entry");
        }
        catch (WinCredException ex) when (ex.ErrorCode == 1168)
        {
            return "missing";
        }
    });
}

static IEnumerable<object> RunDpapi(string payload)
{
    yield return Timed("dpapi", "protect_unprotect", () =>
    {
        var plain = Encoding.UTF8.GetBytes(payload);
        var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        var roundtrip = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        if (!plain.SequenceEqual(roundtrip))
        {
            throw new InvalidOperationException("roundtrip mismatch");
        }

        return $"cipher_bytes={protectedBytes.Length}";
    });
}

static object Timed(string provider, string scenario, Func<string> action)
{
    var sw = Stopwatch.StartNew();
    try
    {
        return Report(provider, scenario, true, action(), sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        return Report(provider, scenario, false, ex.Message, sw.ElapsedMilliseconds);
    }
}

static class WinCred
{
    public static void Write(string target, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var credential = new NativeCredential
        {
            Type = 1,
            TargetName = Marshal.StringToCoTaskMemUni(target),
            CredentialBlobSize = (uint)bytes.Length,
            CredentialBlob = Marshal.AllocCoTaskMem(bytes.Length),
            Persist = 2,
            UserName = Marshal.StringToCoTaskMemUni(Environment.UserName),
        };
        Marshal.Copy(bytes, 0, credential.CredentialBlob, bytes.Length);
        if (!NativeMethods.CredWrite(ref credential, 0))
        {
            throw new WinCredException(Marshal.GetLastWin32Error());
        }

        Marshal.FreeCoTaskMem(credential.TargetName);
        Marshal.FreeCoTaskMem(credential.CredentialBlob);
        Marshal.FreeCoTaskMem(credential.UserName);
    }

    public static string Read(string target)
    {
        if (!NativeMethods.CredRead(target, 1, 0, out var credPtr))
        {
            throw new WinCredException(Marshal.GetLastWin32Error());
        }

        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
            var blob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            NativeMethods.CredFree(credPtr);
        }
    }

    public static void Delete(string target)
    {
        if (!NativeMethods.CredDelete(target, 1, 0))
        {
            throw new WinCredException(Marshal.GetLastWin32Error());
        }
    }
}

sealed class WinCredException(int errorCode) : Exception($"WinCred error {errorCode}")
{
    public int ErrorCode { get; } = errorCode;
}

static class NativeMethods
{
    [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32", SetLastError = true)]
    public static extern bool CredFree(IntPtr cred);

    [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CredDelete(string target, int type, int flags);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct NativeCredential
{
    public uint Flags;
    public int Type;
    public IntPtr TargetName;
    public IntPtr Comment;
    public global::System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
    public uint CredentialBlobSize;
    public IntPtr CredentialBlob;
    public uint Persist;
    public uint AttributeCount;
    public IntPtr Attributes;
    public IntPtr TargetAlias;
    public IntPtr UserName;
}

record EnvelopeV1(
    int SchemaVersion,
    string DeviceId,
    string PrivateKeyPkcs8Base64,
    string DeviceCredentialBase64Url,
    PairingAttempt Pairing);

record PairingAttempt(string? RequestId, string? StatusToken, string? Receipt);
