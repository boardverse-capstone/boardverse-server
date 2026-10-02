using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// EF Core configuration cho <see cref="MemberDepositAuditLog"/>.
/// M1 / Option A - docs/design/host-deposit-discount-and-bvc-payment-design.md §B1.5.
/// </summary>
public class MemberDepositAuditLogConfiguration : IEntityTypeConfiguration<MemberDepositAuditLog>
{
    public void Configure(EntityTypeBuilder<MemberDepositAuditLog> builder)
    {
        builder.ToTable("MemberDepositAuditLogs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.MemberId).IsRequired();
        builder.Property(l => l.UserId);

        builder.Property(l => l.Action)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.AmountBvc).IsRequired();

        builder.Property(l => l.DepositId);
        builder.Property(l => l.FromLobbyId);
        builder.Property(l => l.FromSessionId);
        builder.Property(l => l.ToLobbyId);
        builder.Property(l => l.ToSessionId);

        builder.Property(l => l.MergedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(l => l.Reason)
            .HasMaxLength(500)
            .IsRequired(false);

        // BR § XVII.1: IdempotencyKey UNIQUE.
        builder.Property(l => l.IdempotencyKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(l => l.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_MemberDepositAuditLogs_IdempotencyKey");

        builder.Property(l => l.LedgerEntryId);
        builder.Property(l => l.CreatedByUserId);
        builder.Property(l => l.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");

        // ===== Indexes cho query phổ biến =====

        // Query theo member (UI history)
        builder.HasIndex(l => l.MemberId)
            .HasDatabaseName("IX_MemberDepositAuditLogs_MemberId");

        // Query theo user snapshot (admin trace)
        builder.HasIndex(l => l.UserId)
            .HasDatabaseName("IX_MemberDepositAuditLogs_UserId");

        // Query theo action (reporting)
        builder.HasIndex(l => l.Action)
            .HasDatabaseName("IX_MemberDepositAuditLogs_Action");

        // Query theo time range (background job, dashboard)
        builder.HasIndex(l => l.CreatedAt)
            .HasDatabaseName("IX_MemberDepositAuditLogs_CreatedAt");

        // Composite index cho query phổ biến nhất: (userId, action, createdAt DESC)
        builder.HasIndex(l => new { l.UserId, l.Action, l.CreatedAt })
            .HasDatabaseName("IX_MemberDepositAuditLogs_User_Action_CreatedAt");

        // ===== Relationships =====

        builder.HasOne(l => l.Member)
            .WithMany()
            .HasForeignKey(l => l.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}