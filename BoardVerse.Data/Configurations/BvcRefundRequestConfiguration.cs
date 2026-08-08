using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class BvcRefundRequestConfiguration : IEntityTypeConfiguration<BvcRefundRequest>
{
    public void Configure(EntityTypeBuilder<BvcRefundRequest> builder)
    {
        builder.ToTable("BvcRefundRequests");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status)
            .HasConversion<int>();

        builder.Property(e => e.PlayerReason)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(e => e.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.AdminNote)
            .HasMaxLength(2000);

        builder.Property(e => e.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(e => e.UpdatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict); // Append-only; KHÔNG cascade delete.

        // List admin theo status + ngày tạo.
        builder.HasIndex(e => new { e.Status, e.CreatedAt })
            .HasDatabaseName("IX_BvcRefundRequests_Status_CreatedAt")
            .IsDescending(false, true);

        // Lịch sử refund của player.
        builder.HasIndex(e => new { e.UserId, e.CreatedAt })
            .HasDatabaseName("IX_BvcRefundRequests_UserId_CreatedAt")
            .IsDescending(false, true);

        // Lookup ledger entry liên kết.
        builder.HasIndex(e => e.RelatedLedgerEntryId)
            .HasDatabaseName("IX_BvcRefundRequests_RelatedLedgerEntryId");

        // BR § XVII.1: IdempotencyKey UNIQUE.
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_BvcRefundRequests_IdempotencyKey");
    }
}