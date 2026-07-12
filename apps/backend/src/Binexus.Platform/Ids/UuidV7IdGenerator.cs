namespace Binexus.Platform.Ids;

/// <summary>
/// Production UUID v7 generator using <see cref="TimeProvider"/> for timestamp ordering.
/// </summary>
public sealed class UuidV7IdGenerator(TimeProvider timeProvider) : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7(timeProvider.GetUtcNow());
}
