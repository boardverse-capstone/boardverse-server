using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// EF mapping cho CafeScheduleOverride.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public class CafeScheduleOverrideConfiguration : IEntityTypeConfiguration<CafeScheduleOverride>
{
    public void Configure(EntityTypeBuilder<CafeScheduleOverride> builder)
    {
        builder.ToTable("CafeScheduleOverrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.CafeId).IsRequired();
        builder.Property(o => o.ApplyDate).HasColumnType("date").IsRequired();

        builder.Property(o => o.OpenTime).HasColumnType("time");
        builder.Property(o => o.CloseTime).HasColumnType("time");

        builder.Property(o => o.IsClosed).IsRequired().HasDefaultValue(false);

        // UNIQUE constraint (cafe, applyDate) - mỗi cafe chỉ có tối đa 1 override cho 1 ngày.
        builder.HasIndex(o => new { o.CafeId, o.ApplyDate })
            .IsUnique()
            .HasDatabaseName("IX_CafeScheduleOverrides_Cafe_ApplyDate_Unique");

        builder.HasOne(o => o.Cafe)
            .WithMany()
            .HasForeignKey(o => o.CafeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
