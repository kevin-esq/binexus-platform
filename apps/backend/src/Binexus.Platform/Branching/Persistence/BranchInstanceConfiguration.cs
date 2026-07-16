using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Platform.Branching.Persistence;

internal sealed class BranchInstanceConfiguration : IEntityTypeConfiguration<BranchInstance>
{
    public void Configure(EntityTypeBuilder<BranchInstance> builder)
    {
        builder.ToTable("branch_instances", table =>
        {
            table.HasCheckConstraint(
                "ck_branch_instances_singleton_key_local",
                $"singleton_key = '{BranchInstance.LocalSingletonKey}'");
            table.HasCheckConstraint(
                "ck_branch_instances_status",
                $"status IN ('{BranchInstance.ReadyForActivationStatus}', '{BranchInstance.ActiveStatus}')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SingletonKey)
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(x => x.SingletonKey)
            .IsUnique()
            .HasDatabaseName(BranchInstance.SingletonKeyUniqueIndexName);

        builder.Property(x => x.Status)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("xmin")
            .IsRowVersion();
    }
}
