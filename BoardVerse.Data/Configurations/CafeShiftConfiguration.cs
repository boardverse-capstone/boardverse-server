using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// P-03: Shift management entity configuration.
/// Maps CafeShift entity to the CafeShifts table with proper FK relationships.
/// </summary>
public class CafeShiftConfiguration : IEntityTypeConfiguration<CafeShift>
{
    public void Configure(EntityTypeBuilder<CafeShift> builder)
    {
        builder.ToTable("CafeShifts");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // FK: Cafe (Cascade delete — xóa Cafe thì xóa luôn các ca)
        builder.HasOne(s => s.Cafe)
            .WithMany()
            .HasForeignKey(s => s.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK: OpenedByUser (Cascade delete)
        builder.HasOne(s => s.OpenedByUser)
            .WithMany()
            .HasForeignKey(s => s.OpenedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK: ClosedByUser (SetNull — nếu user bị xóa, ClosedByUserId = null)
        builder.HasOne(s => s.ClosedByUser)
            .WithMany()
            .HasForeignKey(s => s.ClosedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Primitive properties
        builder.Property(s => s.CafeId).IsRequired();
        builder.Property(s => s.OpenedByUserId).IsRequired();
        builder.Property(s => s.ClosedByUserId);
        builder.Property(s => s.OpenedAt).IsRequired();
        builder.Property(s => s.ClosedAt);
        builder.Property(s => s.OpeningCashBalance).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(s => s.ClosingCashBalance).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(s => s.TotalRevenue).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(s => s.TotalSessions).IsRequired();
        builder.Property(s => s.Status)
            .HasConversion<int>()
            .IsRequired();

        // Index: một cafe chỉ có 1 ca OPEN tại một thời điểm
        builder.HasIndex(s => new { s.CafeId, s.Status })
            .HasFilter("\"Status\" = 0") // = ShiftStatus.Open
            .HasDatabaseName("IX_CafeShifts_CafeId_Status_Open");
    }
}
