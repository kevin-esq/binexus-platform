using Binexus.Platform.Branching.Credentials;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Binexus.UnitTests.Branching;

public sealed class DevelopmentFileBranchCredentialStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"binexus-store-{Guid.NewGuid():N}");

    [Fact]
    public async Task Persists_session_and_permanent_credentials_across_instances()
    {
        var session = CreateSession("receipt-a");
        var permanent = CreatePermanent();
        using (var writer = CreateStore())
        {
            await writer.SaveSessionAsync(session);
            await writer.SavePermanentAsync(permanent);
        }

        using var reader = CreateStore();
        (await reader.GetSessionAsync()).Should().BeEquivalentTo(session);
        (await reader.GetPermanentAsync()).Should().BeEquivalentTo(permanent);
    }

    [Fact]
    public async Task Rejects_corrupt_credential_file()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "activation-session.json"), "{ truncated");

        using var store = CreateStore();
        var action = () => store.GetSessionAsync();

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*corrupt*");
    }

    [Fact]
    public async Task Replaces_receipts_and_ignores_abandoned_temp_files()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, ".activation-session.json.abandoned.tmp"), "{}");
        using var store = CreateStore();

        await store.SaveSessionAsync(CreateSession("receipt-a"));
        await store.SaveSessionAsync(CreateSession("receipt-b"));

        (await store.GetSessionAsync())!.Receipt.Should().Be("receipt-b");
    }

    [Fact]
    public async Task Cancelled_write_preserves_previous_session()
    {
        using var store = CreateStore();
        await store.SaveSessionAsync(CreateSession("receipt-a"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => store.SaveSessionAsync(CreateSession("receipt-b"), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetSessionAsync())!.Receipt.Should().Be("receipt-a");
    }

    [Fact]
    public async Task Clear_session_keeps_permanent_credentials()
    {
        var permanent = CreatePermanent();
        using var store = CreateStore();
        await store.SaveSessionAsync(CreateSession("receipt-a"));
        await store.SavePermanentAsync(permanent);

        await store.ClearSessionAsync();

        (await store.GetSessionAsync()).Should().BeNull();
        (await store.GetPermanentAsync()).Should().BeEquivalentTo(permanent);
    }

    [Fact]
    public void Rejects_non_development_environment()
    {
        var action = () => new DevelopmentFileBranchCredentialStore(new TestEnvironment("Production"), _root);

        action.Should().Throw<InvalidOperationException>().WithMessage("*Development*");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private DevelopmentFileBranchCredentialStore CreateStore() =>
        new(new TestEnvironment(Environments.Development), _root);

    private static BranchActivationSession CreateSession(string receipt) =>
        new(
            Guid.CreateVersion7(),
            BranchActivationStage.Reserved,
            Guid.CreateVersion7(),
            "public-key",
            "fingerprint",
            "token-hash",
            "private-key",
            Guid.CreateVersion7(),
            "nonce",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            receipt,
            "token",
            DateTimeOffset.UtcNow);

    private static PermanentBranchCredentials CreatePermanent() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "public-key",
            "fingerprint",
            "token",
            "token-hash",
            "private-key",
            DateTimeOffset.UtcNow);

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Binexus.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
