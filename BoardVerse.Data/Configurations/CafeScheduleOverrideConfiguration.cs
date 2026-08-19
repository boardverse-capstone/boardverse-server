using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// EF mapping cho <see cref="CafeScheduleOverride"/>.
/// Cho phép cafe bật/tắt từng <see cref="TimeSlot"/> hoặc đổi giờ override
/// mà không phải sửa enum <see cref="TimeSlot"/>.
/// </summary>
public class CafeScheduleOverrideConfiguration : IEntityTypeConfiguration<CafeScheduleOverride>
{
    public void Configure(EntityTypeBuilder<CafeScheduleOverride> builder)
    {
        builder.ToTable("CafeScheduleOverrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.CafeId).IsRequired();
        builder.Property(o => o.TimeSlot).HasConversion<int>().IsRequired();

        builder.Property(o => o.StartTime).HasColumnType("time");
        builder.Property(o => o.EndTime).HasColumnType("time");

        builder.Property(o => o.IsClosed).IsRequired().HasDefaultValue(false);

        builder.Property(o => o.EffectiveFrom).HasColumnType("date");
        builder.Property(o => o.EffectiveTo).HasColumnType("date");

        // UNIQUE constraint (cafe, slot) — mỗi cafe chỉ có tối đa 1 override cho 1 slot tại 1 thời điểm.
        // Tránh duplicate rows khi CafeScheduleService.Upsert (dùng GetActiveAsync filter ngày) và
        // TimeSlotService.Create (dùng GetByCafeAndSlotAsync không filter) cùng chạy.
        builder.HasIndex(o => new { o.CafeId, o.TimeSlot })
            .IsUnique()
            .HasDatabaseName("IX_CafeScheduleOverrides_Cafe_TimeSlot_Unique");

        builder.HasOne(o => o.Cafe)
            .WithMany()
            .HasForeignKey(o => o.CafeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
