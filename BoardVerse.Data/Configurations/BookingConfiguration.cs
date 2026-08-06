using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        // === Relationships ===
        builder.Property(b => b.LobbyId);
        builder.Property(b => b.CafeId).IsRequired();
        builder.Property(b => b.CafeTableId).IsRequired();

        // === Schedule ===
        builder.Property(b => b.ScheduledStartTime).IsRequired();
        builder.Property(b => b.ScheduleEndTime).IsRequired();

        // === Status ===
        builder.Property(b => b.Status)
            .HasConversion<int>()
            .IsRequired();

        // === Audit ===
        // C17: UpdatedAt as concurrency token for optimistic concurrency on status changes.
        builder.Property(b => b.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(b => b.UpdatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'")
            .IsConcurrencyToken();

        // === Verification QR ===
        builder.Property(b => b.VerificationQRCode).HasMaxLength(500);

        // === Player quantity ===
        builder.Property(b => b.PlayerQuantity).IsRequired().HasDefaultValue(1);

        // === Navigation ===
        builder.HasOne(b => b.Lobby)
            .WithMany()
            .HasForeignKey(b => b.LobbyId)
            // C15: booking is a financial/historical record. Don't auto-delete on Lobby removal.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Cafe)
            .WithMany()
            .HasForeignKey(b => b.CafeId)
            // C15: don't cascade-delete financial records when Cafe is removed.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.CafeTable)
            .WithMany()
            .HasForeignKey(b => b.CafeTableId)
            // C15: keep historical booking records even if table is removed.
            .OnDelete(DeleteBehavior.Restrict);

        // === Indexes ===
        builder.HasIndex(b => b.LobbyId)
            .HasFilter("\"LobbyId\" IS NOT NULL");
        builder.HasIndex(b => b.CafeId);
        builder.HasIndex(b => b.CafeTableId);
        builder.HasIndex(b => b.ScheduledStartTime);
        builder.HasIndex(b => b.Status);
    }
}
