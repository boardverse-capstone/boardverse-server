using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho ShiftSwapRequest — yêu cầu đổi ca giữa 2 staff.
/// </summary>
public class ShiftSwapRequestConfiguration : IEntityTypeConfiguration<ShiftSwapRequest>
{
    public void Configure(EntityTypeBuilder<ShiftSwapRequest> builder)
    {
        builder.ToTable("ShiftSwapRequests");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.HasOne(s => s.Cafe)
            .WithMany()
            .HasForeignKey(s => s.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Requester)
            .WithMany()
            .HasForeignKey(s => s.RequesterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.TargetStaff)
            .WithMany()
            .HasForeignKey(s => s.TargetStaffId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.CafeId).IsRequired();
        builder.Property(s => s.RequesterId).IsRequired();
        builder.Property(s => s.TargetStaffId).IsRequired();
        builder.Property(s => s.RequesterScheduleId).IsRequired();
        builder.Property(s => s.TargetScheduleId).IsRequired();
        builder.Property(s => s.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(s => s.ReviewNote).HasMaxLength(1000);
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.HasIndex(s => new { s.CafeId, s.Status })
            .HasDatabaseName("IX_ShiftSwapRequests_Cafe_Status");

        builder.HasIndex(s => new { s.TargetStaffId, s.Status })
            .HasDatabaseName("IX_ShiftSwapRequests_Target_Status");
    }
}
