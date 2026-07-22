using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class TournamentConfiguration : IEntityTypeConfiguration<Tournament>
{
    public void Configure(EntityTypeBuilder<Tournament> builder)
    {
        builder.ToTable("Tournaments");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Description)
            .HasMaxLength(2000);

        builder.Property(t => t.StartTime).IsRequired();
        builder.Property(t => t.RegistrationDeadline).IsRequired();
        builder.Property(t => t.RoundDurationMinutes).IsRequired().HasDefaultValue(45);

        builder.Property(t => t.MinParticipants).IsRequired().HasDefaultValue(4);
        builder.Property(t => t.MaxParticipants).IsRequired().HasDefaultValue(32);

        builder.Property(t => t.EntryFee)
            .HasPrecision(18, 2)
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(t => t.TotalRounds).IsRequired().HasDefaultValue(4);
        builder.Property(t => t.PreliminaryRounds).IsRequired().HasDefaultValue(3);
        builder.Property(t => t.FinalistCount).IsRequired().HasDefaultValue(4);
        builder.Property(t => t.CurrentRound).IsRequired().HasDefaultValue(0);

        builder.Property(t => t.MinKarmaRequirement).IsRequired().HasDefaultValue(0);
        builder.Property(t => t.MinEloRequirement).IsRequired().HasDefaultValue(800);
        builder.Property(t => t.MaxEloRequirement).IsRequired().HasDefaultValue(2400);
        // WinnerKarmaBonus / FinalistKarmaBonus: hệ thống tự tính theo rank — không nhập tay.
        // Cột vẫn tồn tại để cache kết quả và đọc nhanh trong query.
        builder.Property(t => t.WinnerKarmaBonus).IsRequired().HasDefaultValue(0);
        builder.Property(t => t.FinalistKarmaBonus).IsRequired().HasDefaultValue(0);
        builder.Property(t => t.NoShowKarmaPenalty).IsRequired().HasDefaultValue(TournamentKarmaPolicy.NoShowPenalty);

        // === Pairing mode ===
        builder.Property(t => t.PairingMode)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(TournamentPairingMode.Auto);

        builder.Property(t => t.Round1PairingsJson).HasColumnType("text");
        builder.Property(t => t.Round2PairingsJson).HasColumnType("text");
        builder.Property(t => t.Round3PairingsJson).HasColumnType("text");
        builder.Property(t => t.FinalPairingsJson).HasColumnType("text");

        builder.Property(t => t.CancellationReason).HasMaxLength(500);
        builder.Property(t => t.CancelledAt);

        builder.Property(t => t.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(t => t.IsFinalEloSynced).IsRequired().HasDefaultValue(false);

        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt);

        // === Relationships ===
        builder.HasOne(t => t.Cafe)
            .WithMany()
            .HasForeignKey(t => t.CafeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.CreatedByManager)
            .WithMany()
            .HasForeignKey(t => t.CreatedByManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.GameTemplate)
            .WithMany()
            .HasForeignKey(t => t.GameTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        // === Indexes ===
        builder.HasIndex(t => t.CafeId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.StartTime);
        builder.HasIndex(t => new { t.CafeId, t.Status });
    }
}

public class TournamentParticipantConfiguration : IEntityTypeConfiguration<TournamentParticipant>
{
    public void Configure(EntityTypeBuilder<TournamentParticipant> builder)
    {
        builder.ToTable("TournamentParticipants");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.RegisteredAt).IsRequired();
        builder.Property(p => p.KarmaAtRegistration).IsRequired();

        builder.Property(p => p.CheckedInAt);
        builder.Property(p => p.CheckedInByStaffId);

        // === Walk-in fields ===
        builder.Property(p => p.IsWalkIn).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.WalkInDisplayName).HasMaxLength(100);
        builder.Property(p => p.WalkInPhoneNumber).HasMaxLength(20);
        builder.Property(p => p.RegisteredByStaffId);
        builder.Property(p => p.JoinedRoundNumber).IsRequired().HasDefaultValue(1);

        builder.Property(p => p.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.TotalPrestigePoints).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.TotalCardsBought).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.FinalRank);

        // === Elo fields (BR-10) ===
        builder.Property(p => p.InitialElo).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.SwissWins).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.SwissDraws).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.SwissLosses).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.EloDelta).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.FinalElo).IsRequired().HasDefaultValue(0);

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt);

        // === Relationships ===
        builder.HasOne(p => p.Tournament)
            .WithMany(t => t.Participants)
            .HasForeignKey(p => p.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.CheckedInByStaff)
            .WithMany()
            .HasForeignKey(p => p.CheckedInByStaffId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.RegisteredByStaff)
            .WithMany()
            .HasForeignKey(p => p.RegisteredByStaffId)
            .OnDelete(DeleteBehavior.SetNull);

        // === Indexes ===
        // Online registration: mỗi user chỉ đăng ký 1 lần / tournament.
        // Partial unique index — chỉ áp dụng khi UserId IS NOT NULL (walk-in có UserId=null).
        builder.HasIndex(p => new { p.TournamentId, p.UserId })
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");

        builder.HasIndex(p => new { p.TournamentId, p.Status });
        builder.HasIndex(p => new { p.TournamentId, p.TotalPrestigePoints });
        builder.HasIndex(p => p.UserId);

        // Walk-in audit
        builder.HasIndex(p => new { p.TournamentId, p.WalkInDisplayName })
            .HasFilter("\"IsWalkIn\" = true");
    }
}

public class TournamentMatchBracketConfiguration : IEntityTypeConfiguration<TournamentMatchBracket>
{
    public void Configure(EntityTypeBuilder<TournamentMatchBracket> builder)
    {
        builder.ToTable("TournamentMatchBrackets");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.RoundNumber).IsRequired();
        builder.Property(m => m.MatchNumber).IsRequired();
        builder.Property(m => m.IsFinal).IsRequired().HasDefaultValue(false);

        builder.Property(m => m.Player1Id);
        builder.Property(m => m.Player2Id);
        builder.Property(m => m.Player3Id);
        builder.Property(m => m.Player4Id);

        builder.Property(m => m.Player1Score);
        builder.Property(m => m.Player2Score);
        builder.Property(m => m.Player3Score);
        builder.Property(m => m.Player4Score);

        builder.Property(m => m.Player1CardsBought);
        builder.Property(m => m.Player2CardsBought);
        builder.Property(m => m.Player3CardsBought);
        builder.Property(m => m.Player4CardsBought);

        builder.Property(m => m.WinnerPlayerId);

        // === Elo aggregation tracking ===
        builder.Property(m => m.EloApplied).IsRequired().HasDefaultValue(false);
        builder.Property(m => m.EloKFactorUsed).IsRequired().HasDefaultValue(32);

        builder.Property(m => m.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(m => m.ScheduledStartTime);
        builder.Property(m => m.ActualStartTime);
        builder.Property(m => m.ActualEndTime);

        builder.Property(m => m.Notes).HasMaxLength(500);

        builder.Property(m => m.CreatedAt).IsRequired();
        builder.Property(m => m.UpdatedAt);

        // === Relationships ===
        builder.HasOne(m => m.Tournament)
            .WithMany(t => t.Matches)
            .HasForeignKey(m => m.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Player1)
            .WithMany()
            .HasForeignKey(m => m.Player1Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Player2)
            .WithMany()
            .HasForeignKey(m => m.Player2Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Player3)
            .WithMany()
            .HasForeignKey(m => m.Player3Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Player4)
            .WithMany()
            .HasForeignKey(m => m.Player4Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.WinnerPlayer)
            .WithMany()
            .HasForeignKey(m => m.WinnerPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.RecordedByStaff)
            .WithMany()
            .HasForeignKey(m => m.RecordedByStaffId)
            .OnDelete(DeleteBehavior.SetNull);

        // === Indexes ===
        builder.HasIndex(m => new { m.TournamentId, m.RoundNumber, m.MatchNumber }).IsUnique();
        builder.HasIndex(m => new { m.TournamentId, m.RoundNumber });
        builder.HasIndex(m => new { m.TournamentId, m.Status });
        builder.HasIndex(m => m.WinnerPlayerId).HasFilter("\"WinnerPlayerId\" IS NOT NULL");

        // F20 Fix: Partial index cho Final match lookup.
        builder.HasIndex(m => m.TournamentId)
            .HasFilter("\"IsFinal\" = true");
    }
}
