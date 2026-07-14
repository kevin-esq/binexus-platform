using Binexus.Platform.Runtime;
using FluentAssertions;

namespace Binexus.UnitTests.Runtime;

public sealed class RuntimeModeParserTests
{
    [Theory]
    [InlineData("Cloud", RuntimeMode.Cloud)]
    [InlineData("cloud", RuntimeMode.Cloud)]
    [InlineData("CLOUD", RuntimeMode.Cloud)]
    [InlineData("Branch", RuntimeMode.Branch)]
    [InlineData("branch", RuntimeMode.Branch)]
    [InlineData(" Cloud ", RuntimeMode.Cloud)]
    [InlineData("\tBranch\n", RuntimeMode.Branch)]
    public void TryParse_accepts_known_values_and_trims(string raw, RuntimeMode expected)
    {
        var ok = RuntimeModeParser.TryParse(raw, out var mode, out var error);
        ok.Should().BeTrue(error);
        mode.Should().Be(expected);
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_rejects_null()
    {
        var ok = RuntimeModeParser.TryParse(null, out _, out var error);
        ok.Should().BeFalse();
        error.Should().Contain("required");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_rejects_empty_after_trim(string raw)
    {
        var ok = RuntimeModeParser.TryParse(raw, out _, out var error);
        ok.Should().BeFalse();
        error.Should().Contain("empty");
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("Cl oud")]
    [InlineData("CloudBranch")]
    public void TryParse_rejects_unknown(string raw)
    {
        var ok = RuntimeModeParser.TryParse(raw, out _, out var error);
        ok.Should().BeFalse();
        error.Should().Contain("invalid");
    }

    [Fact]
    public void Descriptor_exposes_mode_only()
    {
        new CloudRuntimeDescriptor().Mode.Should().Be(RuntimeMode.Cloud);
        new BranchRuntimeDescriptor().Mode.Should().Be(RuntimeMode.Branch);
    }
}
