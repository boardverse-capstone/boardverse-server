using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho StaffSchedule — lịch làm việc theo ngày trong tuần.
/// Cho phép nhiều ca/ngày (không unique trên DayOfWeek), ca qua đêm (EndTime &lt; StartTime).
/// </summary>
public class StaffScheduleConfiguration : IEntityTypeConfiguration<StaffSchedule>
{
    public void Configure(EntityTypeBuilder<StaffSchedule> builder)
    {
        builder.ToTable("StaffSchedules");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // FK: Cafe (Cascade)
        builder.HasOne(s => s.Cafe)
            .WithMany()
            .HasForeignKey(s => s.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK: Staff User (Cascade — nếu staff bị xóa, lịch cũng xóa)
        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.StaffUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.CafeId).IsRequired();
        builder.Property(s => s.StaffUserId).IsRequired();
        builder.Property(s => s.DayOfWeek).IsRequired().HasConversion<int>();
        builder.Property(s => s.StartTime).IsRequired();
        builder.Property(s => s.EndTime).IsRequired();
        builder.Property(s => s.ShiftType)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(s => s.IsRecurring).IsRequired().HasDefaultValue(false);
        builder.Property(s => s.Note).HasMaxLength(500);
        builder.Property(s => s.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        // Indexes
        builder.HasIndex(s => new { s.CafeId, s.StaffUserId, s.DayOfWeek })
            .HasDatabaseName("IX_StaffSchedules_Cafe_Staff_DayOfWeek");

        builder.HasIndex(s => new { s.CafeId, s.DayOfWeek, s.Status })
            .HasDatabaseName("IX_StaffSchedules_Cafe_DayOfWeek_Status");
    }
}
