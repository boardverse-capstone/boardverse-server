using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// BR-RISK-11: Configuration cho RiskScoreHistory. Partition concept (chưa apply vật lý).
/// </summary>
public class RiskScoreHistoryConfiguration : IEntityTypeConfiguration<RiskScoreHistory>
{
    public void Configure(EntityTypeBuilder<RiskScoreHistory> b)
    {
        b.ToTable("RiskScoreHistories");
        b.HasKey(x => x.Id);

        b.Property(x => x.RiskLevel).HasConversion<int>();
        b.Property(x => x.Signals)
            .HasColumnType("jsonb");

        b.HasIndex(x => new { x.UserId, x.SnapshotDate });
        b.HasIndex(x => x.SnapshotDate);
    }
}
