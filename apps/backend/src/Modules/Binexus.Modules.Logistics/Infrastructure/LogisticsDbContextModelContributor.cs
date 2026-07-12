using Binexus.Modules.Logistics.Domain;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Binexus.Modules.Logistics.Infrastructure;

public sealed class LogisticsDbContextModelContributor : IDbContextModelContributor
{
    public void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LogisticsDbContextModelContributor).Assembly);
}

public static class LogisticsPersistedEnums
{
    public static readonly ValueConverter<DeliveryRouteCandidateStatus, string> CandidateStatusConverter =
        new(value => ToPersisted(value), value => ParseCandidateStatus(value));

    public static readonly ValueConverter<DeliveryRouteStatus, string> RouteStatusConverter =
        new(value => ToPersisted(value), value => ParseRouteStatus(value));

    public static readonly ValueConverter<DeliveryRouteStopStatus, string> StopStatusConverter =
        new(value => ToPersisted(value), value => ParseStopStatus(value));

    public static readonly ValueConverter<DeliveryFailureReason, string> FailureReasonConverter =
        new(value => ToPersisted(value), value => ParseFailureReason(value));

    public static string ToApi(DeliveryRouteCandidateStatus status) => ToPersisted(status);
    public static string ToApi(DeliveryRouteStatus status) => ToPersisted(status);
    public static string ToApi(DeliveryRouteStopStatus status) => ToPersisted(status);
    public static string ToApi(DeliveryFailureReason status) => ToPersisted(status);

    public static string ToPersisted(DeliveryRouteCandidateStatus status) => status switch
    {
        DeliveryRouteCandidateStatus.Ready => "READY",
        DeliveryRouteCandidateStatus.Assigned => "ASSIGNED",
        DeliveryRouteCandidateStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string ToPersisted(DeliveryRouteStatus status) => status switch
    {
        DeliveryRouteStatus.Planned => "PLANNED",
        DeliveryRouteStatus.Dispatched => "DISPATCHED",
        DeliveryRouteStatus.Completed => "COMPLETED",
        DeliveryRouteStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string ToPersisted(DeliveryRouteStopStatus status) => status switch
    {
        DeliveryRouteStopStatus.Planned => "PLANNED",
        DeliveryRouteStopStatus.Delivered => "DELIVERED",
        DeliveryRouteStopStatus.Failed => "FAILED",
        DeliveryRouteStopStatus.Skipped => "SKIPPED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string ToPersisted(DeliveryFailureReason reason) => reason switch
    {
        DeliveryFailureReason.NoRecipient => "NO_RECIPIENT",
        DeliveryFailureReason.WrongAddress => "WRONG_ADDRESS",
        DeliveryFailureReason.Refused => "REFUSED",
        DeliveryFailureReason.Damaged => "DAMAGED",
        DeliveryFailureReason.Other => "OTHER",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    public static DeliveryRouteCandidateStatus ParseCandidateStatus(string value) => value switch
    {
        "READY" => DeliveryRouteCandidateStatus.Ready,
        "ASSIGNED" => DeliveryRouteCandidateStatus.Assigned,
        "CANCELLED" => DeliveryRouteCandidateStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown persisted candidate status."),
    };

    public static DeliveryRouteStatus ParseRouteStatus(string value) => value switch
    {
        "PLANNED" => DeliveryRouteStatus.Planned,
        "DISPATCHED" => DeliveryRouteStatus.Dispatched,
        "COMPLETED" => DeliveryRouteStatus.Completed,
        "CANCELLED" => DeliveryRouteStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown persisted route status."),
    };

    public static DeliveryRouteStopStatus ParseStopStatus(string value) => value switch
    {
        "PLANNED" => DeliveryRouteStopStatus.Planned,
        "DELIVERED" => DeliveryRouteStopStatus.Delivered,
        "FAILED" => DeliveryRouteStopStatus.Failed,
        "SKIPPED" => DeliveryRouteStopStatus.Skipped,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown persisted stop status."),
    };

    public static DeliveryFailureReason ParseFailureReason(string value) => value switch
    {
        "NO_RECIPIENT" => DeliveryFailureReason.NoRecipient,
        "WRONG_ADDRESS" => DeliveryFailureReason.WrongAddress,
        "REFUSED" => DeliveryFailureReason.Refused,
        "DAMAGED" => DeliveryFailureReason.Damaged,
        "OTHER" => DeliveryFailureReason.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown persisted failure reason."),
    };
}

public sealed class DeliveryRouteCandidateConfiguration : IEntityTypeConfiguration<DeliveryRouteCandidate>
{
    public void Configure(EntityTypeBuilder<DeliveryRouteCandidate> builder)
    {
        builder.ToTable("delivery_route_candidates", table =>
            table.HasCheckConstraint("ck_delivery_route_candidates_status", "status IN ('READY','ASSIGNED','CANCELLED')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion(LogisticsPersistedEnums.CandidateStatusConverter).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.OrderId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CreatedFromEventId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.Status });
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(DeliveryRouteCandidate.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(DeliveryRouteCandidate.BranchId)).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeliveryRouteConfiguration : IEntityTypeConfiguration<DeliveryRoute>
{
    public void Configure(EntityTypeBuilder<DeliveryRoute> builder)
    {
        builder.ToTable("delivery_routes", table =>
            table.HasCheckConstraint("ck_delivery_routes_status", "status IN ('PLANNED','DISPATCHED','COMPLETED','CANCELLED')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion(LogisticsPersistedEnums.RouteStatusConverter).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CreationOperationKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.AssignOperationKey).HasMaxLength(512);
        builder.Property(x => x.DispatchOperationKey).HasMaxLength(512);
        builder.Property(x => x.CompletionOperationKey).HasMaxLength(512);
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
        builder.Navigation(x => x.Stops).HasField("_stops").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.CreationOperationKey }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.CreatedAtUtc, x.Id });
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(DeliveryRoute.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(DeliveryRoute.BranchId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Stops).WithOne().HasForeignKey(x => x.DeliveryRouteId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DeliveryRouteStopConfiguration : IEntityTypeConfiguration<DeliveryRouteStop>
{
    public void Configure(EntityTypeBuilder<DeliveryRouteStop> builder)
    {
        builder.ToTable("delivery_route_stops", table =>
        {
            table.HasCheckConstraint("ck_delivery_route_stops_status", "status IN ('PLANNED','DELIVERED','FAILED','SKIPPED')");
            table.HasCheckConstraint("ck_delivery_route_stops_sequence_positive", "sequence > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion(LogisticsPersistedEnums.StopStatusConverter).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FailureReason).HasConversion(LogisticsPersistedEnums.FailureReasonConverter).HasMaxLength(32);
        builder.Property(x => x.FailureNotes).HasMaxLength(512);
        builder.Property(x => x.CompletionOperationKey).HasMaxLength(512);
        builder.Property(x => x.FailureOperationKey).HasMaxLength(512);
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.DeliveryRouteId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.DeliveryRouteId, x.OrderId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.OrderId });
        builder.HasIndex(x => new { x.TenantId, x.CompletionOperationKey }).IsUnique().HasFilter("completion_operation_key IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.FailureOperationKey }).IsUnique().HasFilter("failure_operation_key IS NOT NULL");
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(DeliveryRouteStop.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(DeliveryRouteStop.BranchId)).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeliveryProofConfiguration : IEntityTypeConfiguration<DeliveryProof>
{
    public void Configure(EntityTypeBuilder<DeliveryProof> builder)
    {
        builder.ToTable("delivery_proofs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PhotoObjectKey).HasMaxLength(512);
        builder.Property(x => x.SignatureObjectKey).HasMaxLength(512);
        builder.Property(x => x.Recipient).HasMaxLength(256);
        builder.Property(x => x.Notes).HasMaxLength(1024);
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.HasIndex(x => new { x.TenantId, x.DeliveryRouteStopId }).IsUnique();
        builder.HasIndex(x => x.PhotoObjectKey).IsUnique().HasFilter("photo_object_key IS NOT NULL");
        builder.HasIndex(x => x.SignatureObjectKey).IsUnique().HasFilter("signature_object_key IS NOT NULL");
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(DeliveryProof.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryRouteStop>().WithOne().HasForeignKey<DeliveryProof>(x => x.DeliveryRouteStopId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DeliveryRouteLiquidationConfiguration : IEntityTypeConfiguration<DeliveryRouteLiquidation>
{
    public void Configure(EntityTypeBuilder<DeliveryRouteLiquidation> builder)
    {
        builder.ToTable("delivery_route_liquidations", table =>
        {
            table.HasCheckConstraint("ck_delivery_route_liquidations_expected_non_negative", "expected_cents >= 0");
            table.HasCheckConstraint("ck_delivery_route_liquidations_declared_non_negative", "declared_cents >= 0");
            table.HasCheckConstraint("ck_delivery_route_liquidations_currency_iso3", "currency ~ '^[A-Z]{3}$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.DiscrepancyReason).HasMaxLength(512);
        builder.Property(x => x.Notes).HasMaxLength(1024);
        builder.Property(x => x.OperationKey).HasMaxLength(512).IsRequired();
        builder.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.DeliveryRouteId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.OperationKey }).IsUnique();
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(DeliveryRouteLiquidation.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DeliveryRouteLiquidationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DeliveryRouteLiquidationLineConfiguration : IEntityTypeConfiguration<DeliveryRouteLiquidationLine>
{
    public void Configure(EntityTypeBuilder<DeliveryRouteLiquidationLine> builder)
    {
        builder.ToTable("delivery_route_liquidation_lines", table =>
        {
            table.HasCheckConstraint("ck_delivery_route_liquidation_lines_expected_non_negative", "expected_cents >= 0");
            table.HasCheckConstraint("ck_delivery_route_liquidation_lines_declared_non_negative", "declared_cents >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PaymentMethod).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Included).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.DeliveryRouteLiquidationId, x.DeliveryRouteStopId }).IsUnique();
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(DeliveryRouteLiquidationLine.TenantId)).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeliveryProofUploadIntentConfiguration : IEntityTypeConfiguration<DeliveryProofUploadIntent>
{
    public void Configure(EntityTypeBuilder<DeliveryProofUploadIntent> builder)
    {
        builder.ToTable("delivery_proof_upload_intents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OperationKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ObjectKey).HasMaxLength(512).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.OperationKey }).IsUnique();
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(DeliveryProofUploadIntent.TenantId)).OnDelete(DeleteBehavior.Restrict);
    }
}
