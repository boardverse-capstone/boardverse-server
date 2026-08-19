using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class LobbyNotificationSentConfiguration : IEntityTypeConfiguration<LobbyNotificationSent>
{
    public void Configure(EntityTypeBuilder<LobbyNotificationSent> entity)
    {
        entity.ToTable("LobbyNotificationSents");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedNever();

        entity.Property(e => e.Milestone)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(e => e.SentAt)
            .IsRequired();

        entity.HasOne(e => e.Lobby)
            .WithMany(l => l.NotificationSents)
            .HasForeignKey(e => e.LobbyId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => new { e.LobbyId, e.Milestone })
            .IsUnique();
    }
}
