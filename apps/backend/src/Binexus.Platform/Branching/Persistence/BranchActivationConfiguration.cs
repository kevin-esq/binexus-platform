using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Platform.Branching.Persistence;

internal sealed class BranchActivationConfiguration : IEntityTypeConfiguration<BranchActivation>
{
    public void Configure(EntityTypeBuilder<BranchActivation> builder)
    {
        builder.ToTable("branch_activations", table => table.HasCheckConstraint(
            "ck_branch_activations_status",
            $"status IN ('{BranchActivation.OpenStatus}', '{BranchActivation.ReservedStatus}', '{BranchActivation.ConsumedStatus}', '{BranchActivation.ExpiredStatus}')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.CodeHash).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BranchId })
            .HasFilter($"status IN ('{BranchActivation.OpenStatus}', '{BranchActivation.ReservedStatus}')")
            .IsUnique();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.PublicKeyFingerprint).HasMaxLength(64);
        builder.Property(x => x.InstallationTokenHash).HasMaxLength(64);
        builder.Property(x => x.ActivationReceiptHash).HasMaxLength(64);
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
    }
}
