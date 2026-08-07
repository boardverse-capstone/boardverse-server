using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class LobbyAtRiskWarningConfiguration : IEntityTypeConfiguration<LobbyAtRiskWarning>
{
    public void Configure(EntityTypeBuilder<LobbyAtRiskWarning> entity)
    {
        entity.ToTable("LobbyAtRiskWarnings");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedNever();

        entity.Property(e => e.WarnedAt)
            .IsRequired();

        entity.HasOne(e => e.Lobby)
            .WithMany(l => l.AtRiskWarnings)
            .HasForeignKey(e => e.LobbyId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.LobbyId)
            .IsUnique();
    }
}
