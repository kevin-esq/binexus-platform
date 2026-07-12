using Binexus.Modules.Warehouse.Domain;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Binexus.Modules.Warehouse.Infrastructure;

public sealed class WarehouseDbContextModelContributor : IDbContextModelContributor
{
    public void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WarehouseDbContextModelContributor).Assembly);
}

public static class WarehousePersistedEnums
{
    public static readonly ValueConverter<PickingTaskStatus, string> PickingTaskStatusConverter =
        new(status => ToPersisted(status), value => ParsePickingTaskStatus(value));

    public static string ToApi(PickingTaskStatus status) => ToPersisted(status);

    public static string ToPersisted(PickingTaskStatus status) => status switch
    {
        PickingTaskStatus.Pending => "PENDING",
        PickingTaskStatus.Completed => "COMPLETED",
        PickingTaskStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static PickingTaskStatus ParsePickingTaskStatus(string value) => value switch
    {
        "PENDING" => PickingTaskStatus.Pending,
        "COMPLETED" => PickingTaskStatus.Completed,
        "CANCELLED" => PickingTaskStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown persisted picking task status."),
    };
}

public sealed class PickingTaskConfiguration : IEntityTypeConfiguration<PickingTask>
{
    public void Configure(EntityTypeBuilder<PickingTask> builder)
    {
        builder.ToTable("picking_tasks", table =>
            table.HasCheckConstraint("ck_picking_tasks_status", "status IN ('PENDING','COMPLETED','CANCELLED')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion(WarehousePersistedEnums.PickingTaskStatusConverter).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CompletionOperationKey).HasMaxLength(512);
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
        builder.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.OrderId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CreatedFromEventId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CompletionOperationKey }).IsUnique().HasFilter("completion_operation_key IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.BranchId });
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(PickingTask.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(PickingTask.BranchId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PickingTaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PickingLineConfiguration : IEntityTypeConfiguration<PickingLine>
{
    public void Configure(EntityTypeBuilder<PickingLine> builder)
    {
        builder.ToTable("picking_lines", table =>
        {
            table.HasCheckConstraint("ck_picking_lines_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_picking_lines_picked_non_negative", "picked_quantity >= 0");
            table.HasCheckConstraint("ck_picking_lines_picked_not_above_quantity", "picked_quantity <= quantity");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductId).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.PickingTaskId, x.OrderLineId }).IsUnique();
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(PickingLine.TenantId)).OnDelete(DeleteBehavior.Restrict);
    }
}
