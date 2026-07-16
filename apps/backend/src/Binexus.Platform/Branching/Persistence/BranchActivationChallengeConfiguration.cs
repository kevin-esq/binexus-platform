using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Platform.Branching.Persistence;

internal sealed class BranchActivationChallengeConfiguration : IEntityTypeConfiguration<BranchActivationChallenge>
{
    public void Configure(EntityTypeBuilder<BranchActivationChallenge> builder)
    {
        builder.ToTable("branch_activation_challenges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PublicKeyFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.InstallationTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Nonce).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
        builder.HasIndex(x => new { x.BranchInstanceId, x.ExpiresAtUtc });
    }
}
