using Binexus.Modules.Orders.Domain;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Binexus.Modules.Orders.Infrastructure;

public sealed class OrdersDbContextModelContributor : IDbContextModelContributor
{
    public void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContextModelContributor).Assembly);
}

public static class OrdersPersistedEnums
{
    public static readonly ValueConverter<OrderState, string> StateConverter = new(
        value => ToPersisted(value),
        value => FromPersisted(value));

    public static string ToPersisted(OrderState value) => value switch
    {
        OrderState.Draft => "DRAFT",
        OrderState.Approved => "APPROVED",
        OrderState.Picking => "PICKING",
        OrderState.ReadyForDeliveryRoute => "READY_FOR_DELIVERY_ROUTE",
        OrderState.OutForDelivery => "OUT_FOR_DELIVERY",
        OrderState.DeliveryAttemptFailed => "DELIVERY_ATTEMPT_FAILED",
        OrderState.Delivered => "DELIVERED",
        OrderState.Settled => "SETTLED",
        OrderState.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static OrderState FromPersistedPublic(string value) => FromPersisted(value);

    private static OrderState FromPersisted(string value) => value switch
    {
        "DRAFT" => OrderState.Draft,
        "APPROVED" => OrderState.Approved,
        "PICKING" => OrderState.Picking,
        "READY_FOR_DELIVERY_ROUTE" => OrderState.ReadyForDeliveryRoute,
        "OUT_FOR_DELIVERY" => OrderState.OutForDelivery,
        "DELIVERY_ATTEMPT_FAILED" => OrderState.DeliveryAttemptFailed,
        "DELIVERED" => OrderState.Delivered,
        "SETTLED" => OrderState.Settled,
        "CANCELLED" => OrderState.Cancelled,
        _ => throw new InvalidOperationException($"Invalid persisted order state '{value}'."),
    };
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_total_cents_non_negative", "total_cents >= 0");
            t.HasCheckConstraint("ck_orders_currency_iso3", "currency ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_orders_state", "state IN ('DRAFT','APPROVED','PICKING','READY_FOR_DELIVERY_ROUTE','OUT_FOR_DELIVERY','DELIVERY_ATTEMPT_FAILED','DELIVERED','SETTLED','CANCELLED')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CustomerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.PaymentMethod).HasMaxLength(32).IsRequired();
        builder.Property(x => x.OperationKey).HasMaxLength(512);
        builder.Property(x => x.State).HasConversion(OrdersPersistedEnums.StateConverter).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
        builder.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Transitions).HasField("_transitions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.CreatedAtUtc, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.State });
        builder.HasIndex(x => new { x.TenantId, x.OperationKey }).IsUnique().HasFilter("operation_key IS NOT NULL");
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(Order.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(Order.BranchId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Transitions).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines", t =>
        {
            t.HasCheckConstraint("ck_order_lines_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_order_lines_unit_price_non_negative", "unit_price_cents >= 0");
            t.HasCheckConstraint("ck_order_lines_total_non_negative", "line_total_cents >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(512).IsRequired();
        builder.HasIndex(x => new { x.OrderId, x.Id });
    }
}

public sealed class OrderTransitionConfiguration : IEntityTypeConfiguration<OrderTransition>
{
    public void Configure(EntityTypeBuilder<OrderTransition> builder)
    {
        builder.ToTable("order_transitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FromState)
            .HasConversion(new ValueConverter<OrderState?, string?>(
                value => value == null ? null : OrdersPersistedEnums.ToPersisted(value.Value),
                value => value == null ? null : OrdersPersistedEnums.FromPersistedPublic(value)))
            .HasMaxLength(32);
        builder.Property(x => x.ToState).HasConversion(OrdersPersistedEnums.StateConverter).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(512);
        builder.Property(x => x.OperationKey).HasMaxLength(512);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.CausationId).HasMaxLength(128);
        builder.Property(x => x.Source).HasMaxLength(64);
        builder.HasIndex(x => new { x.TenantId, x.OperationKey }).IsUnique().HasFilter("operation_key IS NOT NULL");
        builder.HasIndex(x => new { x.OrderId, x.OccurredAtUtc, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.OrderId, x.OccurredAtUtc });
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(OrderTransition.TenantId)).OnDelete(DeleteBehavior.Restrict);
    }
}
