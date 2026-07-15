using System.Text.Json;
using Binexus.Platform.Branching.Application;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Ids;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Binexus.UnitTests.Branching;

public sealed class BranchInstanceUnitTests
{
    [Fact]
    public void UuidV7_generator_produces_version_7()
    {
        var generator = new UuidV7IdGenerator(TimeProvider.System);
        var id = generator.NewId();
        id.Version.Should().Be(7);
    }

    [Fact]
    public void Initial_persisted_status_is_only_ReadyForActivation()
    {
        Enum.GetValues<BranchServerStatus>().Should().Equal(BranchServerStatus.ReadyForActivation);
        BranchInstance.ReadyForActivationStatus.Should().Be("ReadyForActivation");
        BranchInstance.LocalSingletonKey.Should().Be("local");
        BranchInstance.SingletonKeyUniqueIndexName.Should().Be("ix_branch_instances_singleton_key");
    }

    [Fact]
    public void CreateLocal_forces_singleton_key_and_status()
    {
        var entity = BranchInstance.CreateLocal(Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        entity.SingletonKey.Should().Be("local");
        entity.Status.Should().Be("ReadyForActivation");
    }

    [Fact]
    public async Task Accessor_returns_immutable_info_from_memory_store()
    {
        var store = new BranchInstanceMemoryStore();
        var id = Guid.CreateVersion7();
        store.Publish(new BranchInstanceInfo(id, BranchServerStatus.ReadyForActivation));

        var accessor = new BranchInstanceAccessor(store);
        var info = await accessor.GetAsync();

        info.Id.Should().Be(id);
        info.Status.Should().Be(BranchServerStatus.ReadyForActivation);
        info.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Accessor_throws_before_initialization()
    {
        var accessor = new BranchInstanceAccessor(new BranchInstanceMemoryStore());
        var act = () => accessor.GetAsync().AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not been initialized*");
    }

    [Fact]
    public void Second_publish_keeps_same_identity_and_rejects_different_id()
    {
        var store = new BranchInstanceMemoryStore();
        var first = new BranchInstanceInfo(Guid.CreateVersion7(), BranchServerStatus.ReadyForActivation);
        store.Publish(first).Should().Be(first);
        store.Publish(first).Should().BeSameAs(store.GetRequired());

        var act = () => store.Publish(
            new BranchInstanceInfo(Guid.CreateVersion7(), BranchServerStatus.ReadyForActivation));
        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be replaced*");
    }

    [Fact]
    public void Only_singleton_unique_index_is_expected_race()
    {
        BranchInstancePostgresErrors.IsExpectedSingletonRace(
                PostgresErrorCodes.UniqueViolation,
                BranchInstance.SingletonKeyUniqueIndexName)
            .Should().BeTrue();

        BranchInstancePostgresErrors.IsExpectedSingletonRace(
                PostgresErrorCodes.UniqueViolation,
                "pk_branch_instances")
            .Should().BeFalse();

        BranchInstancePostgresErrors.IsExpectedSingletonRace(
                PostgresErrorCodes.CheckViolation,
                "ck_branch_instances_singleton_key_local")
            .Should().BeFalse();

        BranchInstancePostgresErrors.IsExpectedSingletonRace(
                Wrap(new InvalidOperationException("not postgres")))
            .Should().BeFalse();
    }

    [Fact]
    public void Health_payload_serializes_stable_json_shape()
    {
        var id = Guid.Parse("0190f1e0-0000-7000-8000-000000000001");
        var payload = new
        {
            status = BranchServerStatus.ReadyForActivation.ToString(),
            branchInstanceId = id.ToString("D"),
        };

        var json = JsonSerializer.Serialize(payload);
        json.Should().Be(
            """{"status":"ReadyForActivation","branchInstanceId":"0190f1e0-0000-7000-8000-000000000001"}""");
    }

    private static DbUpdateException Wrap(Exception inner) =>
        new("save failed", inner);
}
