using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho WalkInBooking entity (§9.4).
/// Indexes: (CafeId, CreatedAt), (WalkInWindowId).
/// </summary>
public class WalkInBookingConfiguration : IEntityTypeConfiguration<WalkInBooking>
{
    public void Configure(EntityTypeBuilder<WalkInBooking> builder)
    {
        builder.ToTable("WalkInBookings");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.WalkInWindowId).IsRequired();
        builder.Property(w => w.CafeId).IsRequired();
        builder.Property(w => w.GuestName)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(w => w.GuestPhone)
            .HasMaxLength(20);
        builder.Property(w => w.StartTime).IsRequired();
        builder.Property(w => w.EndTime).IsRequired();
        builder.Property(w => w.Seats).IsRequired();
        builder.Property(w => w.HourlyRate)
            .IsRequired()
            .HasColumnType("numeric(18,0)");
        builder.Property(w => w.TotalAmount)
            .IsRequired()
            .HasColumnType("numeric(18,0)");
        builder.Property(w => w.PaymentStatus)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(w => w.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(w => w.CreatedAt).IsRequired();

        // Foreign keys
        builder.HasOne(w => w.WalkInWindow)
            .WithMany(w => w.WalkInBookings)
            .HasForeignKey(w => w.WalkInWindowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.Cafe)
            .WithMany()
            .HasForeignKey(w => w.CafeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(w => new { w.CafeId, w.CreatedAt })
            .HasDatabaseName("IX_WalkInBookings_CafeId_CreatedAt");

        builder.HasIndex(w => w.WalkInWindowId)
            .HasDatabaseName("IX_WalkInBookings_WalkInWindowId");
    }
}
