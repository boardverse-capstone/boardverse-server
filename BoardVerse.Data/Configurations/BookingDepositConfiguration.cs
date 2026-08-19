using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class BookingDepositConfiguration : IEntityTypeConfiguration<BookingDeposit>
{
    public void Configure(EntityTypeBuilder<BookingDeposit> builder)
    {
        builder.ToTable("BookingDeposits");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.OrderId).IsRequired().HasMaxLength(50);
        builder.Property(d => d.ActiveSessionId);
        builder.Property(d => d.UserId).IsRequired();
        builder.Property(d => d.CafeId).IsRequired();
        builder.Property(d => d.CafeManagerId).IsRequired();
        builder.Property(d => d.Amount).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(d => d.Status).HasConversion<int>().IsRequired();
        builder.Property(d => d.RefundPolicy).HasConversion<int>().IsRequired();
        builder.Property(d => d.TransferContent).HasMaxLength(100);
        builder.Property(d => d.SePayTransactionId).HasMaxLength(100);
        builder.Property(d => d.SePayTransferId).HasMaxLength(100);
        builder.Property(d => d.CreatedAt).IsRequired();
        builder.Property(d => d.ScheduledAt);

        // C17: UpdatedAt as concurrency token for optimistic concurrency on deposit status changes.
        builder.Property(d => d.UpdatedAt).IsConcurrencyToken();
        builder.Property(d => d.QrUrl).HasMaxLength(2000);
        builder.Property(d => d.QrExpiresAt);

        builder.HasOne(d => d.Cafe)
            .WithMany()
            .HasForeignKey(d => d.CafeId)
            // C14: cascade delete would erase financial records; restrict instead.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            // C14: cascade delete would erase financial records; restrict instead.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.ActiveSession)
            .WithMany()
            .HasForeignKey(d => d.ActiveSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        // BR-05: Liên kết với Booking (nullable cho walk-in deposit)
        builder.HasOne(d => d.Booking)
            .WithOne(b => b.BookingDeposit)
            .HasForeignKey<BookingDeposit>(d => d.BookingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => d.OrderId).IsUnique();
        builder.HasIndex(d => d.ActiveSessionId).HasFilter("\"ActiveSessionId\" IS NOT NULL");
        builder.HasIndex(d => d.BookingId).HasFilter("\"BookingId\" IS NOT NULL");
        builder.HasIndex(d => new { d.CafeId, d.Status });
        builder.HasIndex(d => d.SePayTransactionId).HasFilter("\"SePayTransactionId\" IS NOT NULL");
        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.CafeManagerId).HasDatabaseName("IX_BookingDeposits_CafeManagerId"); // L4
    }
}
