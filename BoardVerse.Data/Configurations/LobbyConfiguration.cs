using BoardVerse.Core.Entities;
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

            builder.HasOne(l => l.GameTemplate)
                .WithMany()
                .HasForeignKey(l => l.GameTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(l => l.ActiveSession)
                .WithMany()
                .HasForeignKey(l => l.ActiveSessionId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(l => l.Status);
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
