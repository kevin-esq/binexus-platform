using Binexus.Platform.Logging;
using Binexus.Platform.Persistence;
using Binexus.SharedKernel.Abstractions;
using Binexus.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Binexus.Platform.Dispatching;

public interface ICommandDispatcher
{
    Task<Result> DispatchAsync(ICommand command, CancellationToken cancellationToken = default);
}

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryDispatcher
{
    Task<Result<T>> DispatchAsync<T>(IQuery<T> query, CancellationToken cancellationToken = default);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Dispatches commands. Opens a DB transaction only for <see cref="ITransactionalCommand"/>.
/// </summary>
public sealed class CommandDispatcher(
    IServiceProvider serviceProvider,
    BinexusDbContext dbContext,
    ILogger<CommandDispatcher> logger) : ICommandDispatcher
{
    public async Task<Result> DispatchAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());
        var handler = serviceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No handler registered for {command.GetType().Name}");

        var handleMethod = handlerType.GetMethod(nameof(ICommandHandler<ICommand>.HandleAsync))!;

        if (command is ITransactionalCommand)
        {
            if (dbContext.Database.CurrentTransaction is not null)
            {
                try
                {
                    var result = await InvokeHandler(handleMethod, handler, command, cancellationToken);
                    if (result.IsFailure)
                    {
                        return result;
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    return result;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    PlatformLog.TransactionalCommandFailed(logger, ex, command.GetType().Name);
                    if (IsConcurrencyWriteConflict(ex))
                    {
                        return Result.Failure(ConcurrencyConflict());
                    }

                    throw;
                }
            }

            var strategy = dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var result = await InvokeHandler(handleMethod, handler, command, cancellationToken);
                    if (result.IsFailure)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return result;
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    PlatformLog.TransactionalCommandFailed(logger, ex, command.GetType().Name);
                    await transaction.RollbackAsync(cancellationToken);
                    if (IsConcurrencyWriteConflict(ex))
                    {
                        return Result.Failure(ConcurrencyConflict());
                    }

                    throw;
                }
            });
        }

        return await InvokeHandler(handleMethod, handler, command, cancellationToken);
    }

    private static DomainError ConcurrencyConflict() =>
        DomainError.Conflict(
            "CONCURRENCY_CONFLICT",
            "The resource was modified concurrently. Retry the operation.");

    private static bool IsConcurrencyWriteConflict(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || ex is DbUpdateException { InnerException: { } inner } && IsPostgresUniqueViolation(inner);

    private static bool IsPostgresUniqueViolation(Exception inner) =>
        inner.GetType().Name == "PostgresException"
        && string.Equals(
            inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string,
            "23505",
            StringComparison.Ordinal);

    private static async Task<Result> InvokeHandler(
        System.Reflection.MethodInfo handleMethod,
        object handler,
        ICommand command,
        CancellationToken cancellationToken)
    {
        var task = (Task<Result>)handleMethod.Invoke(handler, [command, cancellationToken])!;
        return await task;
    }
}

public sealed class QueryDispatcher(IServiceProvider serviceProvider) : IQueryDispatcher
{
    public async Task<Result<T>> DispatchAsync<T>(IQuery<T> query, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(T));
        var handler = serviceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No handler registered for {query.GetType().Name}");

        var handleMethod = handlerType.GetMethod(nameof(IQueryHandler<IQuery<T>, T>.HandleAsync))!;
        var task = (Task<Result<T>>)handleMethod.Invoke(handler, [query, cancellationToken])!;
        return await task;
    }
}
