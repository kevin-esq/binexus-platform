using Binexus.Platform.Ids;
using FluentAssertions;

namespace Binexus.UnitTests.Ids;

public sealed class UuidV7IdGeneratorTests
{
    [Fact]
    public void NewId_produces_version_7_and_rfc_variant()
    {
        var generator = new UuidV7IdGenerator(TimeProvider.System);
        var id = generator.NewId();

        id.Should().NotBe(Guid.Empty);
        id.ToString()[14].Should().Be('7');
        var bytes = id.ToByteArray();
        (bytes[8] >> 6).Should().Be(2, "RFC 4122 variant");
    }

    [Fact]
    public void Sequential_generator_produces_valid_v7_at_same_timestamp()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        var generator = new SequentialUuidV7IdGenerator(clock);

        var ids = Enumerable.Range(0, 5).Select(_ => generator.NewId()).ToArray();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().AllSatisfy(id => id.ToString()[14].Should().Be('7'));
    }

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _current = start;

        public override DateTimeOffset GetUtcNow() => _current;

        public void Advance(TimeSpan delta) => _current = _current.Add(delta);
    }
}
