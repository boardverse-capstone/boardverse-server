using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class MatchResultConfiguration : IEntityTypeConfiguration<MatchResult>
    {
        public void Configure(EntityTypeBuilder<MatchResult> builder)
        {
            builder.ToTable("MatchResults");
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Id).ValueGeneratedNever();
            builder.Property(r => r.Outcome)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            builder.Property(r => r.SubmittedAt).IsRequired();
            builder.Property(r => r.UpdatedAt).IsRequired();

            builder.HasOne(r => r.Lobby)
                .WithMany()
                .HasForeignKey(r => r.LobbyId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(r => new { r.LobbyId, r.UserId })
                .IsUnique();
        }
    }

    public class MatchHistoryConfiguration : IEntityTypeConfiguration<MatchHistory>
    {
        public void Configure(EntityTypeBuilder<MatchHistory> builder)
        {
            builder.ToTable("MatchHistories");
            builder.HasKey(h => h.Id);
            builder.Property(h => h.Id).ValueGeneratedNever();
            builder.Property(h => h.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            builder.Property(h => h.FinalizedAt).IsRequired();

            builder.HasOne(h => h.Lobby)
                .WithMany()
                .HasForeignKey(h => h.LobbyId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(h => h.GameTemplate)
                .WithMany()
                .HasForeignKey(h => h.GameTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(h => h.WinnerUser)
                .WithMany()
                .HasForeignKey(h => h.WinnerUserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(h => h.LobbyId)
                .IsUnique();
        }
    }

    public class MatchHistoryParticipantConfiguration : IEntityTypeConfiguration<MatchHistoryParticipant>
    {
        public void Configure(EntityTypeBuilder<MatchHistoryParticipant> builder)
        {
            builder.ToTable("MatchHistoryParticipants");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.Property(p => p.ReportedOutcome)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            builder.HasOne(p => p.MatchHistory)
                .WithMany(h => h.Participants)
                .HasForeignKey(p => p.MatchHistoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(p => new { p.MatchHistoryId, p.UserId })
                .IsUnique();
        }
    }
}
