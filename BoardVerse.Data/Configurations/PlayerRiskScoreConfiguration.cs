using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// BR-RISK-01: Configuration cho PlayerRiskScore. PK = UserId (1 user = 1 snapshot).
/// </summary>
public class PlayerRiskScoreConfiguration : IEntityTypeConfiguration<PlayerRiskScore>
{
    public void Configure(EntityTypeBuilder<PlayerRiskScore> b)
    {
        b.ToTable("PlayerRiskScores");
        b.HasKey(x => x.UserId);

        b.Property(x => x.RiskLevel).HasConversion<int>();

        b.Property(x => x.Signals)
            .HasColumnType("jsonb");

        b.Property(x => x.AdminNote).HasMaxLength(2000);

        b.HasIndex(x => new { x.RiskLevel, x.LastUpdated });
        b.HasIndex(x => x.LastUpdated);
    }
}
