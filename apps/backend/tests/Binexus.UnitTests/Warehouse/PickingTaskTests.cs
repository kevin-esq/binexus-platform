using Binexus.Modules.Warehouse.Domain;
using FluentAssertions;

namespace Binexus.UnitTests.Warehouse;

public sealed class PickingTaskTests
{
    [Fact]
    public void Complete_marks_task_and_all_lines_completed()
    {
        var task = Create();
        var actor = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        task.Complete(actor, now, "complete-key");

        task.Status.Should().Be(PickingTaskStatus.Completed);
        task.CompletedByUserId.Should().Be(actor);
        task.CompletedAtUtc.Should().Be(now);
        task.CompletionOperationKey.Should().Be("complete-key");
        task.Lines.Should().OnlyContain(line => line.PickedQuantity == line.Quantity);
    }

    [Fact]
    public void Complete_requires_pending_status_and_actor()
    {
        var task = Create();
        task.Complete(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "complete-key");

        var duplicate = () => task.Complete(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "other-key");
        var missingActor = () => Create().Complete(Guid.Empty, DateTimeOffset.UtcNow, "complete-key");
        var missingOperationKey = () => Create().Complete(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "");

        duplicate.Should().Throw<WarehouseDomainException>().Which.Code.Should().Be(WarehouseError.PickingTaskNotPending);
        missingActor.Should().Throw<WarehouseDomainException>().Which.Code.Should().Be(WarehouseError.InvalidPickingTask);
        missingOperationKey.Should().Throw<WarehouseDomainException>().Which.Code.Should().Be(WarehouseError.InvalidPickingTask);
    }

    [Fact]
    public void Constructor_rejects_empty_lines_and_non_positive_quantity()
    {
        var actEmpty = () => new PickingTask(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.Empty,
            [],
            DateTimeOffset.UtcNow);
        var actQuantity = () => new PickingLine(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "sku",
            0);

        actEmpty.Should().Throw<WarehouseDomainException>().Which.Code.Should().Be(WarehouseError.InvalidPickingTask);
        actQuantity.Should().Throw<WarehouseDomainException>().Which.Code.Should().Be(WarehouseError.InvalidPickingTask);
    }

    private static PickingTask Create()
    {
        var taskId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        return new PickingTask(
            taskId,
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            [
                new PickingLine(Guid.CreateVersion7(), tenantId, taskId, Guid.CreateVersion7(), "sku-1", 2),
                new PickingLine(Guid.CreateVersion7(), tenantId, taskId, Guid.CreateVersion7(), "sku-2", 1),
            ],
            DateTimeOffset.UtcNow);
    }
}
