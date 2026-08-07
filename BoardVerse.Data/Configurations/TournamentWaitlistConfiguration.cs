using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class TournamentWaitlistConfiguration : IEntityTypeConfiguration<TournamentWaitlist>
{
    public void Configure(EntityTypeBuilder<TournamentWaitlist> builder)
    {
        builder.ToTable("TournamentWaitlists");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.Position).IsRequired();
        builder.Property(w => w.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(w => w.JoinedAt).IsRequired();
        builder.Property(w => w.OfferedAt);
        builder.Property(w => w.OfferExpiresAt);
        builder.Property(w => w.ConfirmedAt);

        // === Relationships ===
        builder.HasOne(w => w.Tournament)
            .WithMany()
            .HasForeignKey(w => w.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.User)
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // === Indexes ===
        builder.HasIndex(w => new { w.TournamentId, w.Status });
        builder.HasIndex(w => new { w.TournamentId, w.Position });
        builder.HasIndex(w => new { w.TournamentId, w.UserId })
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1)"); // Pending or Offered: unique per user
    }
}

public class TournamentSpectatorConfiguration : IEntityTypeConfiguration<TournamentSpectator>
{
    public void Configure(EntityTypeBuilder<TournamentSpectator> builder)
    {
        builder.ToTable("TournamentSpectators");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.JoinedAt).IsRequired();
        builder.Property(s => s.LeftAt);

        // === Relationships ===
        builder.HasOne(s => s.Tournament)
            .WithMany()
            .HasForeignKey(s => s.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // === Indexes ===
        builder.HasIndex(s => new { s.TournamentId, s.UserId }).IsUnique();
        builder.HasIndex(s => s.TournamentId);
    }
}
