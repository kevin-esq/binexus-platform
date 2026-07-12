namespace Binexus.Platform.Ids;

/// <summary>
/// Produces valid UUID v7 values with monotonically advancing timestamps for tests.
/// </summary>
public sealed class SequentialUuidV7IdGenerator(TimeProvider timeProvider) : IIdGenerator
{
    private long _sequence;

    public Guid NewId()
    {
        var timestamp = timeProvider.GetUtcNow().AddTicks(Interlocked.Increment(ref _sequence));
        return Guid.CreateVersion7(timestamp);
    }
}
