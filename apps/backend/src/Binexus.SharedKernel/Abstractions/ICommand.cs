namespace Binexus.SharedKernel.Abstractions;

/// <summary>Marker for commands handled by the application dispatcher.</summary>
public interface ICommand;

/// <summary>Marker for queries (read-only, no outbox, no transaction by default).</summary>
public interface IQuery<TResult>;

/// <summary>
/// Mutates persisted state and/or writes outbox rows. The dispatcher wraps execution in a DB transaction.
/// </summary>
public interface ITransactionalCommand : ICommand;

/// <summary>
/// Command with no persistence side effects (e.g. presigned URL generation). No automatic transaction.
/// </summary>
public interface INonTransactionalCommand : ICommand;

/// <summary>
/// Command safe to retry; dispatcher may enforce idempotency keys (future Identity/Sales slices).
/// </summary>
public interface IIdempotentCommand : ICommand
{
    string IdempotencyKey { get; }
}
