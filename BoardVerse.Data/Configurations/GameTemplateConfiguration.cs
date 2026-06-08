using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class GameTemplateConfiguration : IEntityTypeConfiguration<GameTemplate>
    {
        public void Configure(EntityTypeBuilder<GameTemplate> builder)
        {
            builder.HasKey(g => g.Id);
            builder.Property(g => g.Id).ValueGeneratedNever();
            builder.Property(g => g.Name).IsRequired().HasMaxLength(200);
            builder.Property(g => g.BggGameId);
            builder.Property(g => g.ThumbnailUrl).HasMaxLength(500);
            builder.Property(g => g.Description).HasMaxLength(2000);
            builder.Property(g => g.MinPlayers).IsRequired();
            builder.Property(g => g.MaxPlayers).IsRequired();
            builder.Property(g => g.PlayTime).IsRequired();
            builder.Property(g => g.CreatedAt).IsRequired();
            builder.Property(g => g.UpdatedAt).IsRequired();

            // Seed data for 5 popular board games with hardcoded IDs
            var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            builder.HasData(
                new GameTemplate
                {
                    Id = new Guid("11111111-1111-1111-1111-111111111111"),
                    BggGameId = 13,
                    Name = "Catan",
                    ThumbnailUrl = "https://example.com/images/catan.jpg",
                    Description = "A strategy board game where players build settlements, roads, and cities by gathering and trading resources.",
                    MinPlayers = 3,
                    MaxPlayers = 4,
                    PlayTime = 60,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new GameTemplate
                {
                    Id = new Guid("22222222-2222-2222-2222-222222222222"),
                    BggGameId = 1406,
                    Name = "Monopoly",
                    ThumbnailUrl = "https://example.com/images/monopoly.jpg",
                    Description = "A classic real estate trading game where players buy, sell, and trade properties to bankrupt their opponents.",
                    MinPlayers = 2,
                    MaxPlayers = 8,
                    PlayTime = 120,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new GameTemplate
                {
                    Id = new Guid("33333333-3333-3333-3333-333333333333"),
                    BggGameId = 2225,
                    Name = "Uno",
                    ThumbnailUrl = "https://example.com/images/uno.jpg",
                    Description = "A fast-paced card game where players match colors and numbers, using action cards to change the game dynamics.",
                    MinPlayers = 2,
                    MaxPlayers = 10,
                    PlayTime = 30,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new GameTemplate
                {
                    Id = new Guid("44444444-4444-4444-4444-444444444444"),
                    BggGameId = 148228,
                    Name = "Splendor",
                    ThumbnailUrl = "https://example.com/images/splendor.jpg",
                    Description = "A strategy game of chip-collecting and card development where players act as Renaissance merchants.",
                    MinPlayers = 2,
                    MaxPlayers = 4,
                    PlayTime = 30,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new GameTemplate
                {
                    Id = new Guid("55555555-5555-5555-5555-555555555555"),
                    BggGameId = 925,
                    Name = "Werewolf Ultimate",
                    ThumbnailUrl = "https://example.com/images/werewolf.jpg",
                    Description = "A social deduction party game where players are assigned secret roles and must identify the werewolves among them.",
                    MinPlayers = 5,
                    MaxPlayers = 20,
                    PlayTime = 45,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                }
            );
        }
    }
}
