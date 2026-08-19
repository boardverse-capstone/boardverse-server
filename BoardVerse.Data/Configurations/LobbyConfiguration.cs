using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class LobbyConfiguration : IEntityTypeConfiguration<Lobby>
    {
        public void Configure(EntityTypeBuilder<Lobby> builder)
        {
            builder.ToTable("Lobbies");
            builder.HasKey(l => l.Id);
            builder.Property(l => l.Id).ValueGeneratedNever();
            builder.Property(l => l.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            builder.Property(l => l.CreatedAt).IsRequired();
            builder.Property(l => l.UpdatedAt).IsRequired();

            builder.Property(l => l.IsPrivate).HasDefaultValue(false);
            builder.Property(l => l.ShareCode)
                .HasMaxLength(16)
                .IsRequired()
                .HasDefaultValue(string.Empty);
            builder.Property(l => l.Description).HasMaxLength(1000);
            builder.Property(l => l.CoverImageUrl).HasMaxLength(500);
            builder.Property(l => l.Latitude).HasColumnType("double precision");
            builder.Property(l => l.Longitude).HasColumnType("double precision");
            builder.Property(l => l.ClosedReason).HasMaxLength(500);

            // ===== BR-NEW-* §19.1 fields =====
            builder.Property(l => l.PlayDate).HasColumnType("date");
            builder.Property(l => l.TimeSlot).HasConversion<int>();
            builder.Property(l => l.PreferredStartTime).HasColumnType("time");
            builder.Property(l => l.PreferredEndTime).HasColumnType("time");
            builder.Property(l => l.MinDeposit);
            builder.Property(l => l.DepositSnapshot)
                .HasColumnType("jsonb")
                .HasConversion(new NullableDepositSnapshotConverter());
            builder.Property(l => l.CafeRejectionReason).HasMaxLength(500);

            // ===== BR-10: Karma filter =====
            // Nullable: null = không yêu cầu tối thiểu. Range [0, 100] validated ở DTO/runtime.
            builder.Property(l => l.MinKarmaScore);

            builder.HasOne(l => l.GameTemplate)
                .WithMany()
                .HasForeignKey(l => l.GameTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(l => l.Cafe)
                .WithMany()
                .HasForeignKey(l => l.CafeId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(l => l.Booking)
                .WithMany()
                .HasForeignKey(l => l.BookingId)
                .OnDelete(DeleteBehavior.SetNull);

            // 1:1 Lobby↔Reservation — FK is on Reservation (ReservationConfiguration.cs).
            // Do NOT re-declare here to avoid EF seeing the relationship as a cycle
            // and generating INSERTs in the wrong order (LobbyMembers FK fails).
            builder.HasIndex(l => l.ReservationId)
                .HasDatabaseName("IX_Lobbies_ReservationId");

            builder.HasIndex(l => l.PlayDate)
                .HasDatabaseName("IX_Lobbies_PlayDate");

            builder.HasIndex(l => l.RecruitmentDeadline)
                .HasDatabaseName("IX_Lobbies_RecruitmentDeadline");

            builder.HasOne(l => l.ActiveSession)
                .WithMany()
                .HasForeignKey(l => l.ActiveSessionId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(l => l.Status);
            builder.HasIndex(l => l.ShareCode).IsUnique();
            builder.HasIndex(l => l.GameTemplateId);
            builder.HasIndex(l => l.HostUserId);
            builder.HasIndex(l => l.ScheduledStartTime);
            builder.HasIndex(l => new { l.IsPrivate, l.Status, l.ScheduledStartTime });
        }
    }

    public class LobbyMemberConfiguration : IEntityTypeConfiguration<LobbyMember>
    {
        public void Configure(EntityTypeBuilder<LobbyMember> builder)
        {
            builder.ToTable("LobbyMembers");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).ValueGeneratedNever();
builder.Property(m => m.JoinedAt).IsRequired();
            builder.Property(m => m.IsActive).IsRequired().HasDefaultValue(true);
            // BR-REQUIRED: DB column "Status" lưu dạng varchar (đã có data string sẵn).
            // Phải dùng HasConversion<string>() để khớp schema, không dùng int.
            builder.Property(m => m.Status)
                 .HasConversion<string>()
                 .HasMaxLength(20)
                 .IsRequired();

            builder.HasOne(m => m.Lobby)
                .WithMany(l => l.Members)
                .HasForeignKey(m => m.LobbyId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(m => new { m.LobbyId, m.UserId })
                .IsUnique()
                .HasFilter("\"IsActive\" = true");
            builder.HasIndex(m => m.Status);
        }
    }

    public class LobbyInviteConfiguration : IEntityTypeConfiguration<LobbyInvite>
    {
        public void Configure(EntityTypeBuilder<LobbyInvite> builder)
        {
            builder.ToTable("LobbyInvites");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.Id).ValueGeneratedNever();

            builder.Property(i => i.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(i => i.Message).HasMaxLength(500);
            builder.Property(i => i.CreatedAt).IsRequired();
            builder.Property(i => i.ExpiresAt).IsRequired();

            builder.HasOne(i => i.Lobby)
                .WithMany(l => l.Invites)
                .HasForeignKey(i => i.LobbyId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(i => i.Inviter)
                .WithMany()
                .HasForeignKey(i => i.InviterId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(i => i.Invitee)
                .WithMany()
                .HasForeignKey(i => i.InviteeId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(i => i.InviteeId);
            builder.HasIndex(i => i.LobbyId);
            builder.HasIndex(i => i.Status);
            builder.HasIndex(i => new { i.LobbyId, i.InviteeId, i.Status });
            builder.HasIndex(i => i.ExpiresAt);
        }
    }

    public class LobbyMessageConfiguration : IEntityTypeConfiguration<LobbyMessage>
    {
        public void Configure(EntityTypeBuilder<LobbyMessage> builder)
        {
            builder.ToTable("LobbyMessages");
            builder.HasKey(m => m.Id);

            builder.Property(m => m.Content)
                .IsRequired()
                .HasMaxLength(1000);

            builder.Property(m => m.IsSystem).HasDefaultValue(false);
            builder.Property(m => m.CreatedAt).IsRequired();

            builder.HasOne(m => m.Lobby)
                .WithMany(l => l.Messages)
                .HasForeignKey(m => m.LobbyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Sender is nullable for system messages (IsSystem = true)
            builder.HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            builder.HasIndex(m => new { m.LobbyId, m.CreatedAt });
        }
    }

    public class LobbyReportConfiguration : IEntityTypeConfiguration<LobbyReport>
    {
        public void Configure(EntityTypeBuilder<LobbyReport> builder)
        {
            builder.ToTable("LobbyReports");
            builder.HasKey(r => r.Id);

            builder.Property(r => r.Category)
                .HasConversion<string>()
                .HasMaxLength(30);

            builder.Property(r => r.Status).HasMaxLength(20);
            builder.Property(r => r.Reason)
                .IsRequired()
                .HasMaxLength(1000);
            builder.Property(r => r.AdminNote).HasMaxLength(1000);
            builder.Property(r => r.CreatedAt).IsRequired();

            builder.HasOne(r => r.Reporter)
                .WithMany()
                .HasForeignKey(r => r.ReporterId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(r => r.Lobby)
                .WithMany()
                .HasForeignKey(r => r.LobbyId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(r => new { r.ReporterId, r.LobbyId, r.Status });
        }
    }

    public class PlayerKarmaRatingConfiguration : IEntityTypeConfiguration<PlayerKarmaRating>
    {
        public void Configure(EntityTypeBuilder<PlayerKarmaRating> builder)
        {
            builder.ToTable("PlayerKarmaRatings");
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Id).ValueGeneratedNever();
            builder.Property(r => r.TagsJson).HasMaxLength(500).IsRequired();
            builder.Property(r => r.KarmaDeltaApplied).HasPrecision(6, 2);
            builder.Property(r => r.CreatedAt).IsRequired();

            builder.HasOne(r => r.Lobby)
                .WithMany()
                .HasForeignKey(r => r.LobbyId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(r => r.RaterUser)
                .WithMany()
                .HasForeignKey(r => r.RaterUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(r => r.TargetUser)
                .WithMany()
                .HasForeignKey(r => r.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(r => new { r.LobbyId, r.RaterUserId, r.TargetUserId })
                .IsUnique();
        }
    }
}
