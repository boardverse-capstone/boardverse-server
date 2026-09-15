using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho StaffUnavailableDate — ngày staff không thể làm việc.
/// </summary>
public class StaffUnavailableDateConfiguration : IEntityTypeConfiguration<StaffUnavailableDate>
{
    public void Configure(EntityTypeBuilder<StaffUnavailableDate> builder)
    {
        builder.ToTable("StaffUnavailableDates");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.HasOne(u => u.Cafe)
            .WithMany()
            .HasForeignKey(u => u.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(u => u.User)
            .WithMany()
            .HasForeignKey(u => u.StaffUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(u => u.CafeId).IsRequired();
        builder.Property(u => u.StaffUserId).IsRequired();
        builder.Property(u => u.Date).IsRequired().HasColumnType("date");
        builder.Property(u => u.Reason).HasMaxLength(500);
        builder.Property(u => u.CreatedAt).IsRequired();

        builder.HasIndex(u => new { u.StaffUserId, u.Date })
            .HasDatabaseName("IX_StaffUnavailableDates_Staff_Date");

        // GAP-UNIQUE-08 fix: chặn duplicate (StaffUserId, Date) ở mức DB.
        // Race condition giữa 2 request cùng lúc tạo ngày nghỉ giống nhau sẽ bị
        // Postgres từ chối với SQLSTATE 23505 → service translate sang 409.
        builder.HasIndex(u => new { u.StaffUserId, u.Date })
            .IsUnique()
            .HasDatabaseName("UX_StaffUnavailableDates_Staff_Date");
    }
}
