using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class ComponentLossReportConfiguration : IEntityTypeConfiguration<ComponentLossReport>
{
    public void Configure(EntityTypeBuilder<ComponentLossReport> builder)
    {
        builder.ToTable("ComponentLossReports");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.LossDescription)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.TotalPenaltyAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.Notes)
            .HasMaxLength(1000);

        builder.HasOne(x => x.Cafe)
            .WithMany(c => c.ComponentLossReports)
            .HasForeignKey(x => x.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ActiveSession)
            .WithMany()
            .HasForeignKey(x => x.ActiveSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CafeInventoryBox)
            .WithMany()
            .HasForeignKey(x => x.CafeInventoryBoxId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
