using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Binexus.Platform.Branching.Application;

/// <summary>
/// Classifies PostgreSQL errors for BranchInstance ensure races.
/// Only <see cref="Persistence.BranchInstance.SingletonKeyUniqueIndexName"/> unique violations are expected races.
/// </summary>
public static class BranchInstancePostgresErrors
{
    public static bool IsExpectedSingletonRace(string? sqlState, string? constraintName) =>
        sqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(
            constraintName,
            Persistence.BranchInstance.SingletonKeyUniqueIndexName,
            StringComparison.Ordinal);

    public static bool IsExpectedSingletonRace(DbUpdateException exception)
    {
        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres
                && IsExpectedSingletonRace(postgres.SqlState, postgres.ConstraintName))
            {
                return true;
            }
        }

        return false;
    }
}
