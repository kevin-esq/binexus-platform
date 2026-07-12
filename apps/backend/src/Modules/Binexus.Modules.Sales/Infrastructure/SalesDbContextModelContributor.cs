using Binexus.Modules.Sales.Domain;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Binexus.Modules.Sales.Infrastructure;

public sealed class SalesDbContextModelContributor : IDbContextModelContributor
{
    public void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalesDbContextModelContributor).Assembly);
}

public static class SalesPersistedEnums
{
    public static readonly ValueConverter<SalesSessionStatus, string> SessionStatusConverter =
        new(status => ToPersisted(status), value => ParseSessionStatus(value));

    public static readonly ValueConverter<SaleStatus, string> SaleStatusConverter =
        new(status => ToPersisted(status), value => ParseSaleStatus(value));

    public static readonly ValueConverter<PaymentCaptureMethod, string> PaymentMethodConverter =
        new(method => ToPersisted(method), value => ParsePaymentMethod(value));

    public static string ToApi(SalesSessionStatus status) => ToPersisted(status);

    public static string ToApi(SaleStatus status) => ToPersisted(status);

    public static string ToApi(PaymentCaptureMethod method) => ToPersisted(method);

    public static string ToPersisted(SalesSessionStatus status) => status switch
    {
        SalesSessionStatus.Open => "OPEN",
        SalesSessionStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string ToPersisted(SaleStatus status) => status switch
    {
        SaleStatus.Completed => "COMPLETED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string ToPersisted(PaymentCaptureMethod method) => method switch
    {
        PaymentCaptureMethod.Cash => "CASH",
        PaymentCaptureMethod.Card => "CARD",
        PaymentCaptureMethod.Transfer => "TRANSFER",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    public static SalesSessionStatus ParseSessionStatus(string value) => value switch
    {
        "OPEN" => SalesSessionStatus.Open,
        "CLOSED" => SalesSessionStatus.Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown sales session status."),
    };

    public static SaleStatus ParseSaleStatus(string value) => value switch
    {
        "COMPLETED" => SaleStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown sale status."),
    };

    public static PaymentCaptureMethod ParsePaymentMethod(string value) => value switch
    {
        "CASH" => PaymentCaptureMethod.Cash,
        "CARD" => PaymentCaptureMethod.Card,
        "TRANSFER" => PaymentCaptureMethod.Transfer,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown payment method."),
    };
}

public sealed class SalesSessionConfiguration : IEntityTypeConfiguration<SalesSession>
{
    public void Configure(EntityTypeBuilder<SalesSession> builder)
    {
        builder.ToTable("sales_sessions", table =>
        {
            table.HasCheckConstraint("ck_sales_sessions_status", "status IN ('OPEN','CLOSED')");
            table.HasCheckConstraint("ck_sales_sessions_opening_float_non_negative", "opening_float_cents >= 0");
            table.HasCheckConstraint("ck_sales_sessions_currency_iso3", "char_length(currency) = 3");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("ak_sales_sessions_tenant_id");
        builder.Property(x => x.TerminalId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion(SalesPersistedEnums.SessionStatusConverter).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.DiscrepancyReason).HasMaxLength(512);
        builder.Property(x => x.CloseNotes).HasMaxLength(2000);
        builder.Property(x => x.OpenOperationKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.CloseOperationKey).HasMaxLength(512);
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.TerminalId })
            .IsUnique()
            .HasFilter("status = 'OPEN'")
            .HasDatabaseName("ix_sales_sessions_open_terminal_unique");
        builder.HasIndex(x => new { x.TenantId, x.OpenOperationKey }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CloseOperationKey }).IsUnique().HasFilter("close_operation_key IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.TerminalId, x.Status });
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(SalesSession.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(SalesSession.BranchId)).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales", table =>
        {
            table.HasCheckConstraint("ck_sales_status", "status IN ('COMPLETED')");
            table.HasCheckConstraint("ck_sales_total_non_negative", "total_cents >= 0");
            table.HasCheckConstraint("ck_sales_currency_iso3", "char_length(currency) = 3");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id, x.SessionId })
            .HasName("ak_sales_tenant_id_session");
        builder.Property(x => x.TerminalId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CustomerLabel).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion(SalesPersistedEnums.SaleStatusConverter).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.OperationKey).HasMaxLength(512);
        builder.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.PaymentCaptures).HasField("_paymentCaptures").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.SessionId });
        builder.HasIndex(x => new { x.TenantId, x.OperationKey }).IsUnique().HasFilter("operation_key IS NOT NULL");
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(Sale.TenantId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne("Binexus.Modules.Identity.Domain.Branch", null).WithMany().HasForeignKey(nameof(Sale.BranchId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesSession>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SessionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.PaymentCaptures)
            .WithOne()
            .HasForeignKey(x => new { x.TenantId, x.SaleId, x.SessionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id, x.SessionId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.ToTable("sale_lines", table =>
        {
            table.HasCheckConstraint("ck_sale_lines_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_sale_lines_unit_price_non_negative", "unit_price_cents >= 0");
            table.HasCheckConstraint("ck_sale_lines_line_total_non_negative", "line_total_cents >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.SaleId });
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(SaleLine.TenantId)).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentCaptureConfiguration : IEntityTypeConfiguration<PaymentCapture>
{
    public void Configure(EntityTypeBuilder<PaymentCapture> builder)
    {
        builder.ToTable("payment_captures", table =>
        {
            table.HasCheckConstraint("ck_payment_captures_method", "method IN ('CASH','CARD','TRANSFER')");
            table.HasCheckConstraint("ck_payment_captures_amount_positive", "amount_cents > 0");
            table.HasCheckConstraint("ck_payment_captures_currency_iso3", "char_length(currency) = 3");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Method).HasConversion(SalesPersistedEnums.PaymentMethodConverter).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.SessionId, x.Method });
        builder.HasIndex(x => new { x.TenantId, x.SaleId });
        builder.HasOne("Binexus.Modules.Identity.Domain.Tenant", null).WithMany().HasForeignKey(nameof(PaymentCapture.TenantId)).OnDelete(DeleteBehavior.Restrict);
    }
}
