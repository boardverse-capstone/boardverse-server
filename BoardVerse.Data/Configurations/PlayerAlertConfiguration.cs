using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// R-01 (BR-RISK-02): Configuration cho PlayerAlert.
/// </summary>
public class PlayerAlertConfiguration : IEntityTypeConfiguration<PlayerAlert>
{
    public void Configure(EntityTypeBuilder<PlayerAlert> b)
    {
        b.ToTable("PlayerAlerts");
        b.HasKey(x => x.Id);

        b.Property(x => x.AlertType).HasConversion<int>();
        b.Property(x => x.Severity).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();

        b.Property(x => x.Signals)
            .HasColumnType("jsonb");

        b.Property(x => x.ResolutionNote).HasMaxLength(2000);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.UserId, x.Status });
        b.HasIndex(x => new { x.Severity, x.Status });
        b.HasIndex(x => x.CreatedAt);
    }
}
