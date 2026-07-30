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

        // === Cafe & Users ===
        builder.Property(b => b.CafeId).IsRequired();
        builder.Property(b => b.UserId).IsRequired();

        // === BookingDeposit relationship ===
        builder.Property(b => b.BookingDepositId);

        // === Schedule ===
        builder.Property(b => b.BookingDate).IsRequired();
        builder.Property(b => b.StartTime).IsRequired();
        builder.Property(b => b.EndTime).IsRequired();
        builder.Property(b => b.ActualStartTime);
        builder.Property(b => b.ActualEndTime);

        // === Status ===
        builder.Property(b => b.Status)
            .HasConversion<int>()
            .IsRequired();

        // === Slot & Table ===
        builder.Property(b => b.TotalSlot).IsRequired();
        builder.Property(b => b.TableNumber);
        builder.Property(b => b.TableCode).HasMaxLength(50);

        // === Notes & Reason ===
        builder.Property(b => b.SpecialRequest).HasMaxLength(1000);
        builder.Property(b => b.CancellationReason).HasMaxLength(1000);

        // === Audit ===
        builder.Property(b => b.CreatedAt).IsRequired();
        builder.Property(b => b.UpdatedAt);

        // === Relationships ===
        builder.HasOne(b => b.Cafe)
            .WithMany()
            .HasForeignKey(b => b.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.BookingDeposit)
            .WithOne(d => d.Booking)
            .HasForeignKey<Booking>(b => b.BookingDepositId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(b => b.Lobby)
            .WithMany()
            .HasForeignKey(b => b.LobbyId)
            .OnDelete(DeleteBehavior.SetNull);

        // === Indexes ===
        builder.HasIndex(b => b.CafeId);
        builder.HasIndex(b => b.UserId);
        builder.HasIndex(b => b.BookingDate);
        builder.HasIndex(b => b.Status);
        builder.HasIndex(b => new { b.CafeId, b.BookingDate });
        builder.HasIndex(b => b.BookingDepositId).HasFilter("\"BookingDepositId\" IS NOT NULL");
        builder.HasIndex(b => b.LobbyId).HasFilter("\"LobbyId\" IS NOT NULL");
    }
}
