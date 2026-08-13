using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class RefundTransactionConfiguration : IEntityTypeConfiguration<RefundTransaction>
{
    public void Configure(EntityTypeBuilder<RefundTransaction> builder)
    {
        builder.ToTable("RefundTransactions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.OriginalDeposit).IsRequired();
        builder.Property(r => r.RefundAmount).IsRequired();
        builder.Property(r => r.ForfeitAmount).IsRequired();
        builder.Property(r => r.PlayedRatio).HasColumnType("numeric(5,4)");
        builder.Property(r => r.Reason).HasConversion<int>().IsRequired();
        builder.Property(r => r.IsOverridden).IsRequired().HasDefaultValue(false);
        builder.Property(r => r.OverriddenBy);
        builder.Property(r => r.OverrideReason).HasMaxLength(500);
        builder.Property(r => r.Status).HasConversion<int>().IsRequired().HasDefaultValue(RefundStatus.Pending);

        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.HasIndex(r => r.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_RefundTransactions_IdempotencyKey");

        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.CompletedAt).HasColumnType("timestamp with time zone");

        builder.HasIndex(r => r.ReservationId)
            .HasDatabaseName("IX_RefundTransactions_ReservationId");
        builder.HasIndex(r => new { r.Status, r.CreatedAt })
            .HasDatabaseName("IX_RefundTransactions_Status_CreatedAt");

        builder.HasOne(r => r.Reservation)
            .WithMany()
            .HasForeignKey(r => r.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}