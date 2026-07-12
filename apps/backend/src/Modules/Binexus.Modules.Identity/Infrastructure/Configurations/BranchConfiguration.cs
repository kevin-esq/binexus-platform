using Binexus.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Modules.Identity.Infrastructure.Configurations;

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        builder.HasOne(x => x.Tenant)
            .WithMany(x => x.Branches)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
