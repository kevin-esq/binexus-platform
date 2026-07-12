using Binexus.Modules.Inventory.Domain;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Modules.Inventory.Infrastructure;

public sealed class InventoryDbContextModelContributor : IDbContextModelContributor
{
    public void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContextModelContributor).Assembly);
}

public sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items", t => { t.HasCheckConstraint("ck_stock_items_on_hand_non_negative", "on_hand >= 0"); t.HasCheckConstraint("ck_stock_items_reserved_non_negative", "reserved >= 0"); t.HasCheckConstraint("ck_stock_items_reserved_not_above_on_hand", "reserved <= on_hand"); });
        builder.HasKey(x => x.Id); builder.Property(x => x.ProductId).HasMaxLength(256).IsRequired(); builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.ProductId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BranchId });
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(StockItem.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(StockItem.BranchId)).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations", t => { t.HasCheckConstraint("ck_stock_reservations_quantity_positive", "quantity > 0"); t.HasCheckConstraint("ck_stock_reservations_status", "status IN ('ACTIVE','RELEASED','FAILED')"); });
        builder.HasKey(x => x.Id); builder.Property(x => x.ProductId).HasMaxLength(256).IsRequired(); builder.Property(x => x.Status).HasConversion(InventoryPersistedEnums.ReservationStatusConverter).HasMaxLength(16);
        builder.HasIndex(x => new { x.TenantId, x.OrderId, x.OrderLineId }).IsUnique();
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(StockReservation.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(StockReservation.BranchId)).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", t => t.HasCheckConstraint("ck_stock_movements_quantity_nonzero", "quantity <> 0"));
        builder.HasKey(x => x.Id); builder.Property(x => x.ProductId).HasMaxLength(256).IsRequired(); builder.Property(x => x.Type).HasConversion(InventoryPersistedEnums.MovementTypeConverter).HasMaxLength(16); builder.Property(x => x.OperationKey).HasMaxLength(512);
        builder.HasIndex(x => new { x.TenantId, x.OperationKey }).IsUnique().HasFilter("operation_key IS NOT NULL");
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(StockMovement.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(StockMovement.BranchId)).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.ToTable("stock_transfers", t => { t.HasCheckConstraint("ck_stock_transfers_quantity_positive", "quantity > 0"); t.HasCheckConstraint("ck_stock_transfers_branches_distinct", "source_branch_id <> destination_branch_id"); t.HasCheckConstraint("ck_stock_transfers_status", "status IN ('PENDING','RECEIVED','CANCELLED')"); });
        builder.HasKey(x => x.Id); builder.Property(x => x.ProductId).HasMaxLength(256).IsRequired(); builder.Property(x => x.Reason).HasMaxLength(200); builder.Property(x => x.OperationKey).HasMaxLength(512); builder.Property(x => x.Status).HasConversion(InventoryPersistedEnums.TransferStatusConverter).HasMaxLength(16); builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.OperationKey }).IsUnique().HasFilter("operation_key IS NOT NULL");
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(StockTransfer.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(StockTransfer.SourceBranchId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(StockTransfer.DestinationBranchId)).OnDelete(DeleteBehavior.Restrict);
    }
}
