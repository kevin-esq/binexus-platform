using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

namespace Binexus.UnitTests.Branching;

public sealed class DevicePairingStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_locks_after_max_failed_attempts()
    {
        var session = DevicePairingSession.CreateOpen(
            Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), Guid.CreateVersion7(), Now.AddMinutes(5), Now);

        session.RecordFailedAttempt(3, Now, TimeSpan.FromMinutes(15));
        session.RecordFailedAttempt(3, Now, TimeSpan.FromMinutes(15));
        session.IsLocked(Now.AddMinutes(1)).Should().BeFalse();

        session.RecordFailedAttempt(3, Now, TimeSpan.FromMinutes(15));
        session.IsLocked(Now.AddMinutes(1)).Should().BeTrue();
        session.IsLocked(Now.AddMinutes(20)).Should().BeFalse();
    }

    [Fact]
    public void Request_transitions_pending_to_approved_to_completed()
    {
        var request = CreatePendingRequest();

        request.Status.Should().Be(DevicePairingRequest.PendingApprovalStatus);
        var terminalId = Guid.CreateVersion7();
        request.MarkApproved(terminalId, new string('b', 64), Guid.CreateVersion7(), Now);
        request.Status.Should().Be(DevicePairingRequest.ApprovedStatus);
        request.TerminalId.Should().Be(terminalId);
        request.PairingReceiptHash.Should().Be(new string('b', 64));

        request.MarkCompleted(Now.AddMinutes(1));
        request.Status.Should().Be(DevicePairingRequest.CompletedStatus);
        request.CompletedAtUtc.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Status_token_rotation_replaces_hash_and_expiry()
    {
        var request = CreatePendingRequest();
        var firstHash = request.StatusTokenHash;

        request.RotateStatusToken("newhash", Now.AddMinutes(30));

        request.StatusTokenHash.Should().Be("newhash").And.NotBe(firstHash);
        request.StatusTokenExpiresAtUtc.Should().Be(Now.AddMinutes(30));
    }

    [Fact]
    public void Device_and_terminal_activate_and_revoke()
    {
        var deviceId = Guid.CreateVersion7();
        var device = BranchDevice.CreatePendingConfirmation(
            deviceId, Guid.CreateVersion7(), "pk", new string('a', 64), new string('c', 64), Guid.CreateVersion7(), Now);
        var terminal = BranchTerminal.CreatePendingConfirmation(
            Guid.CreateVersion7(), Guid.CreateVersion7(), deviceId, "Caja 1", "caja 1", Now);

        device.MarkActive(Now);
        terminal.MarkActive(Now);
        device.Status.Should().Be(BranchDevice.ActiveStatus);
        terminal.Status.Should().Be(BranchTerminal.ActiveStatus);

        var admin = Guid.CreateVersion7();
        device.Revoke(admin, Now.AddDays(1));
        terminal.Disable();
        device.Status.Should().Be(BranchDevice.RevokedStatus);
        device.RevokedByUserId.Should().Be(admin);
        terminal.Status.Should().Be(BranchTerminal.DisabledStatus);
    }

    [Fact]
    public void Options_validator_rejects_short_or_development_pepper_outside_development()
    {
        var validator = new DevicePairingOptionsValidator(new StubEnvironment("Production"));

        validator.Validate(null, new DevicePairingOptions { CodePepper = "short" })
            .Failed.Should().BeTrue();
        validator.Validate(null, new DevicePairingOptions { CodePepper = DevicePairingOptions.KnownDevelopmentPepper })
            .Failed.Should().BeTrue();
        validator.Validate(null, new DevicePairingOptions { CodePepper = new string('z', 40) })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Options_validator_allows_development_pepper_in_development()
    {
        var validator = new DevicePairingOptionsValidator(new StubEnvironment("Development"));

        validator.Validate(null, new DevicePairingOptions { CodePepper = DevicePairingOptions.KnownDevelopmentPepper })
            .Succeeded.Should().BeTrue();
    }

    private static DevicePairingRequest CreatePendingRequest() =>
        DevicePairingRequest.CreatePending(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "pk",
            new string('a', 64),
            new string('c', 64),
            "Caja 1",
            "caja 1",
            "statushash",
            Now.AddMinutes(15),
            Now,
            Now.AddMinutes(10));

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Binexus.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
