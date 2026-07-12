using Binexus.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Platform.Persistence.Configurations;

internal sealed class EventHandlerDeliveryConfiguration : IEntityTypeConfiguration<EventHandlerDelivery>
{
    public void Configure(EntityTypeBuilder<EventHandlerDelivery> builder)
    {
        builder.ToTable("event_handler_deliveries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.HandlerKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.LastErrorCode).HasMaxLength(64);
        builder.Property(x => x.LastErrorMessage).HasMaxLength(512);
        builder.Property(x => x.LockedBy).HasMaxLength(128);

        builder.HasIndex(x => new { x.TenantId, x.EventId, x.HandlerKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => new { x.Status, x.LockedUntilUtc });
    }
}
