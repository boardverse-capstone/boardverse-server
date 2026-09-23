using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class LobbyMergeRequestConfiguration : IEntityTypeConfiguration<LobbyMergeRequest>
{
    public void Configure(EntityTypeBuilder<LobbyMergeRequest> builder)
    {
        builder.ToTable("LobbyMergeRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.SourceMembersCount).IsRequired();
        builder.Property(r => r.SourceActiveMembersAtRequest).IsRequired();
        builder.Property(r => r.Reason).HasColumnType("text");
        builder.Property(r => r.ReviewNote).HasColumnType("text");
        builder.Property(r => r.ExpiresAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(r => r.IdempotencyKey).HasMaxLength(128);

        // Computed capacity info at time of request creation
        builder.Property(r => r.CombinedCount).IsRequired();
        builder.Property(r => r.SeatCapacity).IsRequired();
        builder.Property(r => r.FitsCapacity).IsRequired();

        // Unique index trên IdempotencyKey để tránh duplicate request
        builder.HasIndex(r => r.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_LobbyMergeRequests_IdempotencyKey")
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        // Index cho query pending requests
        builder.HasIndex(r => new { r.Status, r.ExpiresAt })
            .HasDatabaseName("IX_LobbyMergeRequests_Status_ExpiresAt");

        // Index cho query theo lobby
        builder.HasIndex(r => r.SourceLobbyId)
            .HasDatabaseName("IX_LobbyMergeRequests_SourceLobbyId");
        builder.HasIndex(r => r.TargetLobbyId)
            .HasDatabaseName("IX_LobbyMergeRequests_TargetLobbyId");

        // FK: SourceLobby
        builder.HasOne(r => r.SourceLobby)
            .WithMany()
            .HasForeignKey(r => r.SourceLobbyId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK: TargetLobby
        builder.HasOne(r => r.TargetLobby)
            .WithMany()
            .HasForeignKey(r => r.TargetLobbyId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK: RequestedByUser
        builder.HasOne(r => r.RequestedByUser)
            .WithMany()
            .HasForeignKey(r => r.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK: ReviewedByUser (nullable)
        builder.HasOne(r => r.ReviewedByUser)
            .WithMany()
            .HasForeignKey(r => r.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LobbyMergeAuditLogConfiguration : IEntityTypeConfiguration<LobbyMergeAuditLog>
{
    public void Configure(EntityTypeBuilder<LobbyMergeAuditLog> builder)
    {
        builder.ToTable("LobbyMergeAuditLogs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Action)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.Metadata).HasColumnType("jsonb");
        builder.Property(l => l.ErrorMessage).HasColumnType("text");

        // Index cho query audit trail
        builder.HasIndex(l => l.MergeRequestId)
            .HasDatabaseName("IX_LobbyMergeAuditLogs_MergeRequestId");
        builder.HasIndex(l => l.SourceLobbyId)
            .HasDatabaseName("IX_LobbyMergeAuditLogs_SourceLobbyId");
        builder.HasIndex(l => l.TargetLobbyId)
            .HasDatabaseName("IX_LobbyMergeAuditLogs_TargetLobbyId");
        builder.HasIndex(l => l.PerformedByUserId)
            .HasDatabaseName("IX_LobbyMergeAuditLogs_PerformedByUserId");
        builder.HasIndex(l => l.CreatedAt)
            .HasDatabaseName("IX_LobbyMergeAuditLogs_CreatedAt");

        // FK: MergeRequest (nullable)
        builder.HasOne(l => l.MergeRequest)
            .WithMany()
            .HasForeignKey(l => l.MergeRequestId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK: SourceLobby (nullable)
        builder.HasOne(l => l.SourceLobby)
            .WithMany()
            .HasForeignKey(l => l.SourceLobbyId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK: TargetLobby (nullable)
        builder.HasOne(l => l.TargetLobby)
            .WithMany()
            .HasForeignKey(l => l.TargetLobbyId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK: SourceReservation (nullable)
        builder.HasOne(l => l.SourceReservation)
            .WithMany()
            .HasForeignKey(l => l.SourceReservationId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK: TargetReservation (nullable)
        builder.HasOne(l => l.TargetReservation)
            .WithMany()
            .HasForeignKey(l => l.TargetReservationId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK: SourceActiveSession (nullable)
        builder.HasOne(l => l.SourceActiveSession)
            .WithMany()
            .HasForeignKey(l => l.SourceActiveSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK: TargetActiveSession (nullable)
        builder.HasOne(l => l.TargetActiveSession)
            .WithMany()
            .HasForeignKey(l => l.TargetActiveSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK: PerformedByUser
        builder.HasOne(l => l.PerformedByUser)
            .WithMany()
            .HasForeignKey(l => l.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ActiveSessionLobbySourceConfiguration : IEntityTypeConfiguration<ActiveSessionLobbySource>
{
    public void Configure(EntityTypeBuilder<ActiveSessionLobbySource> builder)
    {
        builder.ToTable("ActiveSessionLobbySources");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.DepositStatusAtMerge)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(s => s.MergedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.SourceDissolvedAt)
            .HasColumnType("timestamp with time zone");

        // Index cho query
        builder.HasIndex(s => s.ActiveSessionId)
            .HasDatabaseName("IX_ActiveSessionLobbySources_ActiveSessionId");
        builder.HasIndex(s => s.LobbyId)
            .HasDatabaseName("IX_ActiveSessionLobbySources_LobbyId");
        builder.HasIndex(s => s.ReservationId)
            .HasDatabaseName("IX_ActiveSessionLobbySources_ReservationId");

        // FK: ActiveSession
        builder.HasOne(s => s.ActiveSession)
            .WithMany()
            .HasForeignKey(s => s.ActiveSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK: Lobby
        builder.HasOne(s => s.Lobby)
            .WithMany()
            .HasForeignKey(s => s.LobbyId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK: Reservation (nullable)
        builder.HasOne(s => s.Reservation)
            .WithMany()
            .HasForeignKey(s => s.ReservationId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK: MergedByUser (nullable)
        builder.HasOne(s => s.MergedByUser)
            .WithMany()
            .HasForeignKey(s => s.MergedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
