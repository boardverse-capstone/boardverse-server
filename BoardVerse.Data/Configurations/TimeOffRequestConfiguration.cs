using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho TimeOffRequest — yêu cầu nghỉ phép của staff.
/// </summary>
public class TimeOffRequestConfiguration : IEntityTypeConfiguration<TimeOffRequest>
{
    public void Configure(EntityTypeBuilder<TimeOffRequest> builder)
    {
        builder.ToTable("TimeOffRequests");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.HasOne(t => t.Cafe)
            .WithMany()
            .HasForeignKey(t => t.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Staff)
            .WithMany()
            .HasForeignKey(t => t.StaffUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.ReviewedByUser)
            .WithMany()
            .HasForeignKey(t => t.ReviewedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(t => t.CafeId).IsRequired();
        builder.Property(t => t.StaffUserId).IsRequired();
        builder.Property(t => t.StartDate).IsRequired().HasColumnType("date");
        builder.Property(t => t.EndDate).IsRequired().HasColumnType("date");
        builder.Property(t => t.Reason).HasMaxLength(1000);
        builder.Property(t => t.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(t => t.ReviewNote).HasMaxLength(1000);
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasIndex(t => new { t.CafeId, t.Status })
            .HasDatabaseName("IX_TimeOffRequests_Cafe_Status");

        builder.HasIndex(t => new { t.StaffUserId, t.StartDate })
            .HasDatabaseName("IX_TimeOffRequests_Staff_StartDate");
    }
}
