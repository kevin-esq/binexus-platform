using Binexus.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Platform.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ApplicableHandlerKeys)
            .HasColumnType("jsonb")
            .HasConversion(
                keys => System.Text.Json.JsonSerializer.Serialize(keys, (System.Text.Json.JsonSerializerOptions?)null),
                json => System.Text.Json.JsonSerializer.Deserialize<string[]>(json, (System.Text.Json.JsonSerializerOptions?)null)
                    ?? Array.Empty<string>());
        builder.Property(x => x.LastErrorCode).HasMaxLength(64);
        builder.Property(x => x.LastErrorMessage).HasMaxLength(512);
        builder.Property(x => x.LockedBy).HasMaxLength(128);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.CausationId).HasMaxLength(128);
        builder.Property(x => x.InitializedAtUtc);

        builder.HasIndex(x => new { x.TenantId, x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => new { x.Status, x.LockedUntilUtc });

        builder.HasMany(x => x.Deliveries)
            .WithOne(x => x.OutboxMessage)
            .HasForeignKey(x => x.EventId)
            .HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
