using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho ShiftAttendance — check-in/out ca làm việc của staff.
///
/// FIX H7: FK từ Schedule → Attendance đổi từ <c>Cascade</c> sang <c>Restrict</c>.
/// Lý do: attendance là dữ liệu lịch sử (historical data) — xóa schedule không được
/// kéo theo xóa luôn các bản ghi điểm danh. Nếu manager xóa nhầm schedule,
/// toàn bộ attendance record bị mất vĩnh viễn (vi phạm audit trail).
/// Migration <c>20260915113350_RestrictAttendanceScheduleDelete</c> apply thay đổi này
/// (cùng schema với raw SQL <c>staff_schedule_migration.sql</c>; SQL ghi cả 2 migration ID
/// vào <c>__EFMigrationsHistory</c> để EF không re-apply).
/// </summary>
public class ShiftAttendanceConfiguration : IEntityTypeConfiguration<ShiftAttendance>
{
    public void Configure(EntityTypeBuilder<ShiftAttendance> builder)
    {
        builder.ToTable("ShiftAttendances");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.HasOne(a => a.Schedule)
            .WithMany()
            .HasForeignKey(a => a.ScheduleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.StaffUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(a => a.ScheduleId).IsRequired();
        builder.Property(a => a.StaffUserId).IsRequired();
        builder.Property(a => a.Date).IsRequired().HasColumnType("date");
        builder.Property(a => a.CheckInTime);
        builder.Property(a => a.CheckOutTime);
        builder.Property(a => a.LateMinutes).IsRequired().HasDefaultValue(0);
        builder.Property(a => a.EarlyLeaveMinutes).IsRequired().HasDefaultValue(0);
        builder.Property(a => a.Status).HasMaxLength(50);
        builder.Property(a => a.Note).HasMaxLength(500);
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();

        // Mỗi (ScheduleId, Date) chỉ có 1 attendance record
        builder.HasIndex(a => new { a.ScheduleId, a.Date })
            .IsUnique()
            .HasDatabaseName("IX_ShiftAttendances_Schedule_Date_Unique");

        builder.HasIndex(a => new { a.StaffUserId, a.Date })
            .HasDatabaseName("IX_ShiftAttendances_Staff_Date");
    }
}
