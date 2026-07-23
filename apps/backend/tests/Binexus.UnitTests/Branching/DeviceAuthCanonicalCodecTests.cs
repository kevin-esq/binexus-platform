using System.Globalization;
using Binexus.Platform.Branching.DeviceAuth;
using FluentAssertions;

namespace Binexus.UnitTests.Branching;

public sealed class DeviceAuthCanonicalCodecTests
{
    [Fact]
    public void Encode_is_deterministic_for_fixed_inputs()
    {
        var challenge = new CanonicalDeviceAuthChallenge(
            Guid.Parse("0194f0a0-0000-7000-8000-000000000001"),
            Guid.Parse("0194f0a0-0000-7000-8000-000000000002"),
            Guid.Parse("0194f0a0-0000-7000-8000-000000000003"),
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "nonce-value-1",
            DateTimeOffset.Parse("2026-07-18T12:00:00.0000000Z", CultureInfo.InvariantCulture));

        var a = CanonicalDeviceAuthChallengeCodec.Encode(challenge);
        var b = CanonicalDeviceAuthChallengeCodec.Encode(challenge);
        a.Should().Equal(b);
        a.Should().NotBeEmpty();

        // Field 0 = version string with u16 BE length prefix (32 chars → 0x0020).
        a[0].Should().Be(0x00);
        a[1].Should().Be(0x20);
        DeviceAuthCryptoFormats.ChallengeVersion.Length.Should().Be(0x20);
    }
}
