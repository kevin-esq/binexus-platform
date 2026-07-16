using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace Binexus.Platform.Branching.Credentials;

public sealed class DevelopmentFileBranchCredentialStore : IBranchCredentialStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _directory;
    private readonly string _sessionPath;
    private readonly string _permanentPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public DevelopmentFileBranchCredentialStore(IHostEnvironment environment)
        : this(environment, null)
    {
    }

    internal DevelopmentFileBranchCredentialStore(IHostEnvironment environment, string? overrideRoot)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "DevelopmentFileBranchCredentialStore is only available in Development.");
        }

        var defaultDirectory = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "binexus",
            "branch-credentials");
        _directory = overrideRoot is null
            ? defaultDirectory
            : Path.GetFullPath(overrideRoot);
        _sessionPath = Path.Join(_directory, "activation-session.json");
        _permanentPath = Path.Join(_directory, "permanent-credentials.json");
    }

    public async Task<BranchActivationSession?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAsync<BranchActivationSession>(_sessionPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveSessionAsync(BranchActivationSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteAtomicAsync(_sessionPath, session, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_sessionPath))
            {
                File.Delete(_sessionPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PermanentBranchCredentials?> GetPermanentAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAsync<PermanentBranchCredentials>(_permanentPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SavePermanentAsync(PermanentBranchCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteAtomicAsync(_permanentPath, credentials, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException($"Credential file '{path}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Credential file '{path}' is corrupt.", exception);
        }
    }

    private async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var tempPath = Path.Join(
            _directory,
            $".{Path.GetFileName(path)}.{Guid.CreateVersion7():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }
}
