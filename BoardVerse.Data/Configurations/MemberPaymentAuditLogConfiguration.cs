using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// EF Core configuration cho <see cref="MemberPaymentAuditLog"/>.
/// M2 / Case 2 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C1.3.
/// </summary>
public class MemberPaymentAuditLogConfiguration : IEntityTypeConfiguration<MemberPaymentAuditLog>
{
    public void Configure(EntityTypeBuilder<MemberPaymentAuditLog> builder)
    {
        builder.ToTable("MemberPaymentAuditLogs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.MemberId).IsRequired();
        builder.Property(l => l.UserId);

        builder.Property(l => l.PaymentMethod)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.AmountBvc).IsRequired();
        builder.Property(l => l.AmountCash)
            .HasColumnType("decimal(18, 0)")
            .IsRequired();

        builder.Property(l => l.WalletTxnId);
        builder.Property(l => l.RefundLedgerEntryId);

        // BR § XVII.1: IdempotencyKey UNIQUE.
        builder.Property(l => l.IdempotencyKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(l => l.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_MemberPaymentAuditLogs_IdempotencyKey");

        builder.Property(l => l.CreatedByStaffId);

        builder.Property(l => l.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");

        // Refund tracking (Task C2.8)
        builder.Property(l => l.RefundedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(l => l.RefundReason)
            .HasMaxLength(500)
            .IsRequired(false);

        // ===== Indexes cho query phổ biến =====

        // Query theo member (UI history per session)
        builder.HasIndex(l => l.MemberId)
            .HasDatabaseName("IX_MemberPaymentAuditLogs_MemberId");

        // Query theo user (admin trace, reporting)
        builder.HasIndex(l => l.UserId)
            .HasDatabaseName("IX_MemberPaymentAuditLogs_UserId");

        // Query theo payment method (reporting breakdown — Gap #20)
        builder.HasIndex(l => l.PaymentMethod)
            .HasDatabaseName("IX_MemberPaymentAuditLogs_PaymentMethod");

        // Query theo time range (reporting per month)
        builder.HasIndex(l => l.CreatedAt)
            .HasDatabaseName("IX_MemberPaymentAuditLogs_CreatedAt");

        // Composite index cho reporting phổ biến: (paymentMethod, createdAt DESC)
        builder.HasIndex(l => new { l.PaymentMethod, l.CreatedAt })
            .HasDatabaseName("IX_MemberPaymentAuditLogs_PaymentMethod_CreatedAt");

        // Composite index cho admin trace: (userId, createdAt DESC)
        builder.HasIndex(l => new { l.UserId, l.CreatedAt })
            .HasDatabaseName("IX_MemberPaymentAuditLogs_UserId_CreatedAt");

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

        builder.HasOne(l => l.WalletTxn)
            .WithMany()
            .HasForeignKey(l => l.WalletTxnId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.RefundLedgerEntry)
            .WithMany()
            .HasForeignKey(l => l.RefundLedgerEntryId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.CreatedByStaff)
            .WithMany()
            .HasForeignKey(l => l.CreatedByStaffId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
