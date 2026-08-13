using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho entity BookingNoShowVote (BR-22 + no-show voting flow).
/// Đảm bảo mỗi (BookingId, VoterUserId) chỉ có 1 vote (unique index).
/// </summary>
public class BookingNoShowVoteConfiguration : IEntityTypeConfiguration<BookingNoShowVote>
{
    public void Configure(EntityTypeBuilder<BookingNoShowVote> builder)
    {
        builder.ToTable("BookingNoShowVotes");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.BookingId).IsRequired();
        builder.Property(v => v.VoterUserId).IsRequired();
        builder.Property(v => v.AbsentMemberIdsJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValue("[]");
        builder.Property(v => v.VotedAt).IsRequired();

        // M10: Votes là community moderation signals (BR § IV Exception 2), không cascade với Booking.
        builder.HasOne(v => v.Booking)
            .WithMany()
            .HasForeignKey(v => v.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        // TD-02: Navigation đến Reservation (nullable cho legacy rows)
        builder.HasOne(v => v.Reservation)
            .WithMany()
            .HasForeignKey(v => v.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Voter)
            .WithMany()
            .HasForeignKey(v => v.VoterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // BR-22: mỗi voter chỉ vote 1 lần cho 1 booking hoặc reservation
        builder.HasIndex(v => new { v.ReservationId, v.VoterUserId })
            .IsUnique()
            .HasFilter("\"ReservationId\" IS NOT NULL");
        builder.HasIndex(v => new { v.BookingId, v.VoterUserId }).IsUnique();
        builder.HasIndex(v => v.BookingId);
        builder.HasIndex(v => v.ReservationId);
    }
}

/// <summary>
/// Configuration cho entity BookingRating (cross-rating sau check-out).
/// Đảm bảo mỗi (BookingId, VoterUserId) chỉ submit 1 lần.
/// </summary>
public class BookingRatingConfiguration : IEntityTypeConfiguration<BookingRating>
{
    public void Configure(EntityTypeBuilder<BookingRating> builder)
    {
        builder.ToTable("BookingRatings");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.BookingId).IsRequired();
        builder.Property(r => r.VoterUserId).IsRequired();
        builder.Property(r => r.RatingsJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValue("[]");
        builder.Property(r => r.SubmittedAt).IsRequired();
        builder.Property(r => r.IsAggregated).IsRequired().HasDefaultValue(false);

        // M10: Ratings là community moderation signals, không cascade với Booking.
        builder.HasOne(r => r.Booking)
            .WithMany()
            .HasForeignKey(r => r.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        // TD-02: Navigation đến Reservation (nullable cho legacy rows)
        builder.HasOne(r => r.Reservation)
            .WithMany()
            .HasForeignKey(r => r.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Voter)
            .WithMany()
            .HasForeignKey(r => r.VoterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotent: mỗi voter chỉ submit 1 lần cho 1 booking hoặc reservation
        builder.HasIndex(r => new { r.ReservationId, r.VoterUserId })
            .IsUnique()
            .HasFilter("\"ReservationId\" IS NOT NULL");
        builder.HasIndex(r => new { r.BookingId, r.VoterUserId }).IsUnique();
        builder.HasIndex(r => r.BookingId);
        builder.HasIndex(r => r.ReservationId);
        builder.HasIndex(r => r.IsAggregated);
    }
}