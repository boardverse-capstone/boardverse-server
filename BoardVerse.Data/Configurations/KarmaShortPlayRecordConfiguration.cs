using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho entity KarmaShortPlayRecord (§9.6).
/// Mỗi (ReservationId, UserId) chỉ có 1 record — unique index.
/// </summary>
public class KarmaShortPlayRecordConfiguration : IEntityTypeConfiguration<KarmaShortPlayRecord>
{
    public void Configure(EntityTypeBuilder<KarmaShortPlayRecord> builder)
    {
        builder.ToTable("KarmaShortPlayRecords");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.ReservationId).IsRequired();
        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.PlayedMinutes).IsRequired();
        builder.Property(r => r.ScheduledMinutes).IsRequired();
        builder.Property(r => r.PlayedRatio).IsRequired().HasPrecision(5, 4);
        builder.Property(r => r.KarmaDelta).IsRequired();
        builder.Property(r => r.KarmaPointsAdded).IsRequired().HasPrecision(18, 4);
        builder.Property(r => r.TotalKarmaScore).IsRequired();
        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();

        // FK: không cascade khi Reservation bị xóa — record là audit trail vĩnh viễn
        builder.HasOne(r => r.Reservation)
            .WithMany(res => res.ShortPlayRecords)
            .HasForeignKey(r => r.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotent: mỗi user chỉ có 1 short-play record per reservation
        builder.HasIndex(r => new { r.ReservationId, r.UserId }).IsUnique();
        builder.HasIndex(r => r.ReservationId);
        builder.HasIndex(r => r.UserId);
    }
}
