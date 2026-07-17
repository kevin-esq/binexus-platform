using System.Collections.Concurrent;

namespace Binexus.Platform.Branching.Pairing;

/// <summary>
/// Optional one-time, in-process hand-off of the raw pairing receipt from admin approval to the Device's
/// first status poll. PostgreSQL keeps only the receipt hash; the raw value is never persisted or logged.
/// Recovery after a lost poll or API restart uses <c>POST .../receipt/reissue</c> with ECDSA proof-of-possession
/// — the vault is an optimization, not a durability requirement.
/// </summary>
public interface IPairingReceiptVault
{
    void Store(Guid pairingRequestId, string rawReceipt);

    /// <summary>Returns and removes the raw receipt, or <c>null</c> if already consumed / never stored.</summary>
    string? Consume(Guid pairingRequestId);

    void Discard(Guid pairingRequestId);
}

public sealed class InMemoryPairingReceiptVault : IPairingReceiptVault
{
    private readonly ConcurrentDictionary<Guid, string> _receipts = new();

    public void Store(Guid pairingRequestId, string rawReceipt) =>
        _receipts[pairingRequestId] = rawReceipt;

    public string? Consume(Guid pairingRequestId) =>
        _receipts.TryRemove(pairingRequestId, out var receipt) ? receipt : null;

    public void Discard(Guid pairingRequestId) => _receipts.TryRemove(pairingRequestId, out _);
}
