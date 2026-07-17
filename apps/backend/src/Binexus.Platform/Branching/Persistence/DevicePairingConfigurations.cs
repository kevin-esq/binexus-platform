using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Binexus.Platform.Branching.Persistence;

internal sealed class DevicePairingSessionConfiguration : IEntityTypeConfiguration<DevicePairingSession>
{
    public void Configure(EntityTypeBuilder<DevicePairingSession> builder)
    {
        builder.ToTable("device_pairing_sessions", table => table.HasCheckConstraint(
            "ck_device_pairing_sessions_status",
            $"status IN ('{DevicePairingSession.OpenStatus}', '{DevicePairingSession.ConsumedStatus}', '{DevicePairingSession.ExpiredStatus}')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.CodeHash).IsUnique();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.BranchInstanceId, x.Status });
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
    }
}

internal sealed class DevicePairingChallengeConfiguration : IEntityTypeConfiguration<DevicePairingChallenge>
{
    public void Configure(EntityTypeBuilder<DevicePairingChallenge> builder)
    {
        builder.ToTable("device_pairing_challenges", table =>
        {
            table.HasCheckConstraint(
                "ck_device_pairing_challenges_phase",
                $"phase IN ('{DevicePairingChallenge.ExchangePhase}', '{DevicePairingChallenge.ConfirmationPhase}', '{DevicePairingChallenge.ReceiptReissuePhase}')");
            table.HasCheckConstraint(
                "ck_device_pairing_challenges_phase_targets",
                $"(phase = '{DevicePairingChallenge.ExchangePhase}' AND pairing_session_id IS NOT NULL) "
                + $"OR (phase = '{DevicePairingChallenge.ConfirmationPhase}' AND pairing_request_id IS NOT NULL AND pairing_receipt_hash IS NOT NULL) "
                + $"OR (phase = '{DevicePairingChallenge.ReceiptReissuePhase}' AND pairing_request_id IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Phase).HasMaxLength(16).IsRequired();
        builder.Property(x => x.PublicKeyFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CredentialHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PairingReceiptHash).HasMaxLength(64);
        builder.Property(x => x.Nonce).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.BranchInstanceId, x.ExpiresAtUtc });
        builder.HasIndex(x => x.PairingRequestId);
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
    }
}

internal sealed class DevicePairingRequestConfiguration : IEntityTypeConfiguration<DevicePairingRequest>
{
    public void Configure(EntityTypeBuilder<DevicePairingRequest> builder)
    {
        builder.ToTable("device_pairing_requests", table => table.HasCheckConstraint(
            "ck_device_pairing_requests_status",
            $"status IN ('{DevicePairingRequest.PendingApprovalStatus}', '{DevicePairingRequest.ApprovedStatus}', "
            + $"'{DevicePairingRequest.RejectedStatus}', '{DevicePairingRequest.ExpiredStatus}', '{DevicePairingRequest.CompletedStatus}')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PublicKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.PublicKeyFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CredentialHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RequestedTerminalName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RequestedTerminalNameNormalized).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.StatusTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PairingReceiptHash).HasMaxLength(64);
        builder.HasIndex(x => new { x.PairingSessionId, x.DeviceId }).IsUnique();
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
    }
}

internal sealed class BranchDeviceConfiguration : IEntityTypeConfiguration<BranchDevice>
{
    public void Configure(EntityTypeBuilder<BranchDevice> builder)
    {
        builder.ToTable("branch_devices", table => table.HasCheckConstraint(
            "ck_branch_devices_status",
            $"status IN ('{BranchDevice.PendingConfirmationStatus}', '{BranchDevice.ActiveStatus}', '{BranchDevice.RevokedStatus}')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PublicKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.PublicKeyFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CredentialHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();

        // No-reuse policy: fingerprint and credential hash are globally unique per Branch instance,
        // including Revoked rows. Revocation is terminal for that cryptographic material.
        builder.HasIndex(x => new { x.BranchInstanceId, x.PublicKeyFingerprint }).IsUnique();
        builder.HasIndex(x => new { x.BranchInstanceId, x.CredentialHash }).IsUnique();
        builder.HasIndex(x => x.PairingRequestId).IsUnique();
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
    }
}

internal sealed class BranchTerminalConfiguration : IEntityTypeConfiguration<BranchTerminal>
{
    public void Configure(EntityTypeBuilder<BranchTerminal> builder)
    {
        builder.ToTable("branch_terminals", table => table.HasCheckConstraint(
            "ck_branch_terminals_status",
            $"status IN ('{BranchTerminal.PendingConfirmationStatus}', '{BranchTerminal.ActiveStatus}', '{BranchTerminal.DisabledStatus}')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.HasIndex(x => x.DeviceId).IsUnique();

        // Live terminals (PendingConfirmation or Active) must not share a name. Disabled frees the name.
        builder.HasIndex(x => new { x.BranchInstanceId, x.NormalizedName })
            .HasFilter($"status IN ('{BranchTerminal.PendingConfirmationStatus}', '{BranchTerminal.ActiveStatus}')")
            .IsUnique();
        builder.Property(x => x.Version).HasColumnName("xmin").IsRowVersion();
    }
}
