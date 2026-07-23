using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Platform.Branching.Persistence;

internal sealed class DeviceAuthChallengeConfiguration : IEntityTypeConfiguration<DeviceAuthChallenge>
{
    public void Configure(EntityTypeBuilder<DeviceAuthChallenge> builder)
    {
        builder.ToTable("device_auth_challenges", table => table.HasCheckConstraint(
            "ck_device_auth_challenges_status",
            $"status IN ('{DeviceAuthChallenge.OpenStatus}', '{DeviceAuthChallenge.ConsumedStatus}')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Nonce).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.BranchInstanceId, x.DeviceId, x.Status });
        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
    }
}
