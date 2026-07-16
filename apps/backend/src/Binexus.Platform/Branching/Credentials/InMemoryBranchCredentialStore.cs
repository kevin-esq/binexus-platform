namespace Binexus.Platform.Branching.Credentials;

public sealed class InMemoryBranchCredentialStore : IBranchCredentialStore
{
    private readonly object _gate = new();
    private BranchActivationSession? _session;
    private PermanentBranchCredentials? _permanent;

    public Task<BranchActivationSession?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_session);
        }
    }

    public Task SaveSessionAsync(BranchActivationSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            _session = session;
        }

        return Task.CompletedTask;
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _session = null;
        }

        return Task.CompletedTask;
    }

    public Task<PermanentBranchCredentials?> GetPermanentAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_permanent);
        }
    }

    public Task SavePermanentAsync(PermanentBranchCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        lock (_gate)
        {
            _permanent = credentials;
        }

        return Task.CompletedTask;
    }
}
