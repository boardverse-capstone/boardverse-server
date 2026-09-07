using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class PlayerBoardGameSaveConfiguration : IEntityTypeConfiguration<PlayerBoardGameSave>
{
    public void Configure(EntityTypeBuilder<PlayerBoardGameSave> builder)
    {
        builder.ToTable("PlayerBoardGameSaves");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SavedAt).IsRequired();

        builder.HasIndex(x => new { x.UserId, x.GameTemplateId }).IsUnique();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.GameTemplate)
            .WithMany()
            .HasForeignKey(x => x.GameTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
