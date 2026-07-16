using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Platform.Branching.Persistence;

internal sealed class CloudBranchInstanceConfiguration : IEntityTypeConfiguration<CloudBranchInstance>
{
    public void Configure(EntityTypeBuilder<CloudBranchInstance> builder)
    {
        builder.ToTable("cloud_branch_instances", table => table.HasCheckConstraint(
            "ck_cloud_branch_instances_status",
            $"status IN ('{CloudBranchInstance.ActivatingStatus}', '{CloudBranchInstance.ActiveStatus}')"));
        builder.HasKey(x => x.BranchInstanceId);
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.InstallationTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PublicKey).HasColumnType("text").IsRequired();
        builder.Property(x => x.PublicKeyFingerprint).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.BranchId })
            .HasFilter($"status IN ('{CloudBranchInstance.ActivatingStatus}', '{CloudBranchInstance.ActiveStatus}')")
            .IsUnique();
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
    }
}
