using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho WalkInWindow entity (§9.3).
/// - OCC version column (uint → xmin shadow property via HasConcurrencyToken).
/// - Indexes: (CafeId, WindowEnd), (Status, ExpiresAt).
/// </summary>
public class WalkInWindowConfiguration : IEntityTypeConfiguration<WalkInWindow>
{
    public void Configure(EntityTypeBuilder<WalkInWindow> builder)
    {
        builder.ToTable("WalkInWindows");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.SourceReservationId);
        builder.Property(w => w.CafeId).IsRequired();
        builder.Property(w => w.WindowStart).IsRequired();
        builder.Property(w => w.WindowEnd).IsRequired();
        builder.Property(w => w.TotalSeats).IsRequired();
        builder.Property(w => w.AvailableSeats).IsRequired();
        builder.Property(w => w.HeldSeats).IsRequired();
        builder.Property(w => w.InUseSeats).IsRequired();

        // OCC version — sử dụng column thật "Version bigint" trong DB (KHÔNG map sang xmin system column).
        // Trước đây map sang xmin, gây conflict với column thật Version và OCC không hoạt động.
        // Value không auto-generated — application tự tăng Version mỗi UPDATE (raw SQL OCC).
        builder.Property(w => w.Version)
            .IsRequired()
            .HasColumnType("bigint")
            .IsConcurrencyToken();

        builder.Property(w => w.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(w => w.CreatedAt).IsRequired();
        builder.Property(w => w.ExpiresAt).IsRequired();

        // Foreign keys
        builder.HasOne(w => w.SourceReservation)
            .WithMany()
            .HasForeignKey(w => w.SourceReservationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(w => w.Cafe)
            .WithMany()
            .HasForeignKey(w => w.CafeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes — come after relationships
        builder.HasIndex(w => new { w.CafeId, w.WindowEnd })
            .HasDatabaseName("IX_WalkInWindows_CafeId_WindowEnd");

        builder.HasIndex(w => new { w.Status, w.ExpiresAt })
            .HasDatabaseName("IX_WalkInWindows_Status_ExpiresAt");

        builder.HasIndex(w => w.SourceReservationId)
            .HasDatabaseName("IX_WalkInWindows_SourceReservationId");
    }
}
