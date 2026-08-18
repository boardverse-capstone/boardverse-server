using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.PlayDate).HasColumnType("date").IsRequired();
        builder.Property(r => r.TimeSlot).HasConversion<int>().IsRequired();
        builder.Property(r => r.PreferredStartTime).HasColumnType("time");
        builder.Property(r => r.PreferredEndTime).HasColumnType("time");

        // BR-RESV-02: ScheduledStartTime + ScheduledEndTime = playDate + user-chosen start/end time.
        // Lưu DB để query nhanh (playedRatio, WalkInWindow cleanup, extension flow).
        builder.Property(r => r.ScheduledStartTime).HasColumnType("timestamp with time zone");
        builder.Property(r => r.ScheduledEndTime).HasColumnType("timestamp with time zone");

        // BR-RESV-02: index cho WalkInWindowCleanupJob + NoShowDetectionJob
        // (BackgroundServices chạy theo cutoff).
        builder.HasIndex(r => new { r.ScheduledEndTime, r.Status })
            .HasDatabaseName("IX_Reservations_ScheduledEndTime_Status");
        builder.HasIndex(r => new { r.ScheduledStartTime, r.Status })
            .HasDatabaseName("IX_Reservations_ScheduledStartTime_Status");

        builder.Property(r => r.MinPlayers).IsRequired();
        builder.Property(r => r.MaxPlayers).IsRequired();

        builder.Property(r => r.DepositAmount).IsRequired();
        builder.Property(r => r.MinDepositApplied).IsRequired();
        builder.Property(r => r.RiskMultiplier).HasColumnType("numeric(4,2)").HasDefaultValue(1.0m);

        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.CurrentPlayers).HasDefaultValue(1);

        // BR-EXT-03 §3.5: Extension tracking
        builder.Property(r => r.ExtensionCount).IsRequired().HasDefaultValue(0);
        builder.Property(r => r.ExtendedEndTime).HasColumnType("timestamp with time zone");

        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();

        // BR § III.3 + § XVII.1 — idempotency key UNIQUE.
        builder.HasIndex(r => r.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_Reservations_IdempotencyKey");

        // BR §21A.7: ReservationCode 8 chars alphanumeric uppercase — unique, dùng cho POS scan QR.
        builder.Property(r => r.ReservationCode)
            .HasMaxLength(16)
            .IsRequired();
        builder.HasIndex(r => r.ReservationCode)
            .IsUnique()
            .HasDatabaseName("UX_Reservations_ReservationCode");

        // BR § 9 — query theo host + playDate.
        builder.HasIndex(r => new { r.HostId, r.PlayDate })
            .HasDatabaseName("IX_Reservations_Host_PlayDate");

        // BR § 9 + § VIII — query per-cafe same playDate+timeSlot.
        builder.HasIndex(r => new { r.CafeId, r.PlayDate, r.TimeSlot, r.Status })
            .HasDatabaseName("IX_Reservations_Cafe_PlayDate_TimeSlot_Status");

        builder.HasIndex(r => new { r.Status, r.RecruitmentDeadline })
            .HasDatabaseName("IX_Reservations_Status_Deadline");

        // Stored DepositSnapshot as JSONB column.
        builder.Property(r => r.DepositConfigSnapshot)
            .HasColumnType("jsonb")
            .HasConversion(new DepositSnapshotConverter())
            .IsRequired();

        builder.HasOne(r => r.Host)
            .WithMany()
            .HasForeignKey(r => r.HostId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Cafe)
            .WithMany()
            .HasForeignKey(r => r.CafeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Game)
            .WithMany()
            .HasForeignKey(r => r.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Lobby)
            .WithOne(l => l.Reservation)
            .HasForeignKey<Reservation>(r => r.LobbyId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.SeatInventory)
            .WithMany()
            .HasForeignKey(r => r.SeatInventoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.GameInventory)
            .WithMany()
            .HasForeignKey(r => r.GameInventoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // ===== docs/time-slot-fixed-end-design (1).md §9.1 — session tracking =====

        builder.Property(r => r.CheckedInAt).HasColumnType("timestamp with time zone");
        builder.Property(r => r.ActualEndAt).HasColumnType("timestamp with time zone");
        builder.Property(r => r.PlayedRatio).HasColumnType("numeric(5,4)");
        builder.Property(r => r.EndReason).HasConversion<int>();
        builder.Property(r => r.CancelledBy);
        builder.Property(r => r.CancelReason).HasMaxLength(500);

        builder.HasOne(r => r.WalkInWindow)
            .WithMany()
            .HasForeignKey(r => r.WalkInWindowId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(r => r.WalkInWindowId)
            .HasDatabaseName("IX_Reservations_WalkInWindowId");
        builder.HasIndex(r => new { r.Status, r.CheckedInAt })
            .HasDatabaseName("IX_Reservations_Status_CheckedInAt");

        // TD-02 fix: Explicitly map reverse navigation collections so EF Core
        // does not create shadow FKs (BookingId1/ReservationId1) when two FKs
        // in the child entity (BookingRating, BookingNoShowVote) both share the
        // same CLR type (Guid / Guid?) and target different principals.
        builder.HasMany(r => r.Ratings)
            .WithOne(rating => rating.Reservation)
            .HasForeignKey(rating => rating.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.NoShowVotes)
            .WithOne(vote => vote.Reservation)
            .HasForeignKey(vote => vote.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.ShortPlayRecords)
            .WithOne(record => record.Reservation)
            .HasForeignKey(record => record.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SeatInventoryConfiguration : IEntityTypeConfiguration<SeatInventory>
{
    public void Configure(EntityTypeBuilder<SeatInventory> builder)
    {
        builder.ToTable("SeatInventories");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.PlayDate).HasColumnType("date").IsRequired();
        builder.Property(s => s.ScheduledStartTime).IsRequired();
        builder.Property(s => s.ScheduledEndTime).IsRequired();

        builder.Property(s => s.TotalSeats).IsRequired();
        builder.Property(s => s.HeldSeats).HasDefaultValue(0);
        builder.Property(s => s.InUseSeats).HasDefaultValue(0);

        // BR § XVII.3 — optimistic concurrency token.
        // Production DB dùng bigint, không phải uint/integer.
        builder.Property(s => s.RowVersion)
            .HasColumnType("bigint")
            .IsConcurrencyToken();

        // BR-NEW-15: mỗi cafe có 1 row cho mỗi (playDate, scheduledStartTime, scheduledEndTime).
        builder.HasIndex(s => new { s.CafeId, s.PlayDate, s.ScheduledStartTime, s.ScheduledEndTime })
            .IsUnique()
            .HasDatabaseName("UX_SeatInventories_Cafe_PlayDate_Times");

        builder.HasOne(s => s.Cafe)
            .WithMany()
            .HasForeignKey(s => s.CafeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class GameInventoryConfiguration : IEntityTypeConfiguration<GameInventory>
{
    public void Configure(EntityTypeBuilder<GameInventory> builder)
    {
        builder.ToTable("GameInventories");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.PlayDate).HasColumnType("date").IsRequired();
        builder.Property(g => g.ScheduledStartTime).IsRequired();
        builder.Property(g => g.ScheduledEndTime).IsRequired();

        builder.Property(g => g.TotalCopies).IsRequired();
        builder.Property(g => g.HeldCopies).HasDefaultValue(0);
        builder.Property(g => g.InUseCopies).HasDefaultValue(0);

        builder.Property(g => g.RowVersion)
            .HasColumnType("bigint")
            .IsConcurrencyToken();

        // BR-NEW-15: mỗi cafe-game có 1 row cho mỗi (playDate, scheduledStartTime, scheduledEndTime).
        builder.HasIndex(g => new { g.CafeId, g.GameId, g.PlayDate, g.ScheduledStartTime, g.ScheduledEndTime })
            .IsUnique()
            .HasDatabaseName("UX_GameInventories_Cafe_Game_PlayDate_Times");

        builder.HasOne(g => g.Cafe)
            .WithMany()
            .HasForeignKey(g => g.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(g => g.Game)
            .WithMany()
            .HasForeignKey(g => g.GameId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CafeConfigConfiguration : IEntityTypeConfiguration<CafeConfig>
{
    public void Configure(EntityTypeBuilder<CafeConfig> builder)
    {
        builder.ToTable("CafeConfigs");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Capacity).HasDefaultValue(30);
        builder.Property(c => c.MaxLobbiesPerUserPerDay).HasDefaultValue(1);

        builder.Property(c => c.MaxPlayersPerLobbySameDay).HasDefaultValue(30);
        builder.Property(c => c.MaxPlayersPerLobby1Day).HasDefaultValue(20);
        builder.Property(c => c.MaxPlayersPerLobby2Days).HasDefaultValue(15);
        builder.Property(c => c.MaxPlayersPerLobby3To4Days).HasDefaultValue(10);
        builder.Property(c => c.MaxPlayersPerLobby5To7Days).HasDefaultValue(6);

        builder.Property(c => c.MinDepositSameDay).HasDefaultValue(50L);
        builder.Property(c => c.MinDeposit1Day).HasDefaultValue(50L);
        builder.Property(c => c.MinDeposit2Days).HasDefaultValue(100L);
        builder.Property(c => c.MinDeposit3To4Days).HasDefaultValue(150L);
        builder.Property(c => c.MinDeposit5To7Days).HasDefaultValue(200L);

        builder.Property(c => c.RequireApprovalForDistant).HasDefaultValue(true);
        builder.Property(c => c.DistantThresholdDays).HasDefaultValue(2);
        builder.Property(c => c.ApprovalTimeoutHours).HasDefaultValue(24);
        builder.Property(c => c.MaxTotalDepositPerUser).HasDefaultValue(500_000L);
        builder.Property(c => c.RecruitmentDeadlineBufferMinutes).HasDefaultValue(120);
        builder.Property(c => c.CancellationGraceMinutes).HasDefaultValue(15);
        builder.Property(c => c.DepositRatePerPerson).HasDefaultValue(5L);

        builder.HasIndex(c => c.CafeId)
            .IsUnique()
            .HasDatabaseName("UX_CafeConfigs_CafeId");

        builder.HasOne(c => c.Cafe)
            .WithOne()
            .HasForeignKey<CafeConfig>(c => c.CafeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}