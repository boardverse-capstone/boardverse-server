using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class TournamentMatchEloContributionConfiguration : IEntityTypeConfiguration<TournamentMatchEloContribution>
{
    public void Configure(EntityTypeBuilder<TournamentMatchEloContribution> builder)
    {
        builder.ToTable("TournamentMatchEloContributions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnType("uuid");
        builder.Property(x => x.MatchId).HasColumnType("uuid").IsRequired();
        builder.Property(x => x.ParticipantId).HasColumnType("uuid").IsRequired();
        builder.Property(x => x.EloDelta).HasColumnType("integer").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();

        // FK
        builder.HasOne(x => x.Match)
            .WithMany()
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Participant)
            .WithMany()
            .HasForeignKey(x => x.ParticipantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index: matchId để revert nhanh
        builder.HasIndex(x => x.MatchId);
        builder.HasIndex(x => x.ParticipantId);
    }
}
