using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class GameComponentTemplateConfiguration : IEntityTypeConfiguration<GameComponentTemplate>
    {
        public void Configure(EntityTypeBuilder<GameComponentTemplate> builder)
        {
            builder.HasKey(gc => gc.Id);
            builder.Property(gc => gc.Id).ValueGeneratedNever();
            builder.Property(gc => gc.GameTemplateId).IsRequired();
            builder.Property(gc => gc.ComponentName).IsRequired().HasMaxLength(200);
            builder.Property(gc => gc.DefaultQuantity).IsRequired();
            builder.Property(gc => gc.CreatedAt).IsRequired();

            // One-to-Many relationship: GameTemplate -> GameComponentTemplate
            builder.HasOne(gc => gc.GameTemplate)
                .WithMany(g => g.Components)
                .HasForeignKey(gc => gc.GameTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            // Seed data for components of each game
            var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            builder.HasData(
                // Catan Components (GameTemplateId: 11111111-1111-1111-1111-111111111111)
                new GameComponentTemplate
                {
                    Id = new Guid("a1111111-1111-1111-1111-111111111111"),
                    GameTemplateId = new Guid("11111111-1111-1111-1111-111111111111"),
                    ComponentName = "Wood Hexagon Tiles",
                    DefaultQuantity = 4,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a1111111-1111-1111-1111-111111111112"),
                    GameTemplateId = new Guid("11111111-1111-1111-1111-111111111111"),
                    ComponentName = "Brick Hexagon Tiles",
                    DefaultQuantity = 3,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a1111111-1111-1111-1111-111111111113"),
                    GameTemplateId = new Guid("11111111-1111-1111-1111-111111111111"),
                    ComponentName = "Sheep Resource Cards",
                    DefaultQuantity = 19,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a1111111-1111-1111-1111-111111111114"),
                    GameTemplateId = new Guid("11111111-1111-1111-1111-111111111111"),
                    ComponentName = "Wheat Resource Cards",
                    DefaultQuantity = 19,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a1111111-1111-1111-1111-111111111115"),
                    GameTemplateId = new Guid("11111111-1111-1111-1111-111111111111"),
                    ComponentName = "Ore Resource Cards",
                    DefaultQuantity = 19,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a1111111-1111-1111-1111-111111111116"),
                    GameTemplateId = new Guid("11111111-1111-1111-1111-111111111111"),
                    ComponentName = "Settlement Pieces",
                    DefaultQuantity = 20,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a1111111-1111-1111-1111-111111111117"),
                    GameTemplateId = new Guid("11111111-1111-1111-1111-111111111111"),
                    ComponentName = "Road Pieces",
                    DefaultQuantity = 30,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a1111111-1111-1111-1111-111111111118"),
                    GameTemplateId = new Guid("11111111-1111-1111-1111-111111111111"),
                    ComponentName = "City Pieces",
                    DefaultQuantity = 16,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a1111111-1111-1111-1111-111111111119"),
                    GameTemplateId = new Guid("11111111-1111-1111-1111-111111111111"),
                    ComponentName = "Dice (2 pieces)",
                    DefaultQuantity = 2,
                    CreatedAt = seedDate
                },

                // Monopoly Components (GameTemplateId: 22222222-2222-2222-2222-222222222222)
                new GameComponentTemplate
                {
                    Id = new Guid("a2222222-2222-2222-2222-222222222221"),
                    GameTemplateId = new Guid("22222222-2222-2222-2222-222222222222"),
                    ComponentName = "Gameboard",
                    DefaultQuantity = 1,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a2222222-2222-2222-2222-222222222222"),
                    GameTemplateId = new Guid("22222222-2222-2222-2222-222222222222"),
                    ComponentName = "Player Tokens (8 pieces)",
                    DefaultQuantity = 8,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a2222222-2222-2222-2222-222222222223"),
                    GameTemplateId = new Guid("22222222-2222-2222-2222-222222222222"),
                    ComponentName = "Title Deed Cards (28 cards)",
                    DefaultQuantity = 28,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a2222222-2222-2222-2222-222222222224"),
                    GameTemplateId = new Guid("22222222-2222-2222-2222-222222222222"),
                    ComponentName = "Chance Cards (16 cards)",
                    DefaultQuantity = 16,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a2222222-2222-2222-2222-222222222225"),
                    GameTemplateId = new Guid("22222222-2222-2222-2222-222222222222"),
                    ComponentName = "Community Chest Cards (16 cards)",
                    DefaultQuantity = 16,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a2222222-2222-2222-2222-222222222226"),
                    GameTemplateId = new Guid("22222222-2222-2222-2222-222222222222"),
                    ComponentName = "Houses (32 pieces)",
                    DefaultQuantity = 32,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a2222222-2222-2222-2222-222222222227"),
                    GameTemplateId = new Guid("22222222-2222-2222-2222-222222222222"),
                    ComponentName = "Hotels (12 pieces)",
                    DefaultQuantity = 12,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a2222222-2222-2222-2222-222222222228"),
                    GameTemplateId = new Guid("22222222-2222-2222-2222-222222222222"),
                    ComponentName = "Dice (2 pieces)",
                    DefaultQuantity = 2,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a2222222-2222-2222-2222-222222222229"),
                    GameTemplateId = new Guid("22222222-2222-2222-2222-222222222222"),
                    ComponentName = "Monopoly Money",
                    DefaultQuantity = 1,
                    CreatedAt = seedDate
                },

                // Uno Components (GameTemplateId: 33333333-3333-3333-3333-333333333333)
                new GameComponentTemplate
                {
                    Id = new Guid("a3333333-3333-3333-3333-333333333331"),
                    GameTemplateId = new Guid("33333333-3333-3333-3333-333333333333"),
                    ComponentName = "Number Cards (Red)",
                    DefaultQuantity = 19,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a3333333-3333-3333-3333-333333333332"),
                    GameTemplateId = new Guid("33333333-3333-3333-3333-333333333333"),
                    ComponentName = "Number Cards (Blue)",
                    DefaultQuantity = 19,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a3333333-3333-3333-3333-333333333333"),
                    GameTemplateId = new Guid("33333333-3333-3333-3333-333333333333"),
                    ComponentName = "Number Cards (Green)",
                    DefaultQuantity = 19,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a3333333-3333-3333-3333-333333333334"),
                    GameTemplateId = new Guid("33333333-3333-3333-3333-333333333333"),
                    ComponentName = "Number Cards (Yellow)",
                    DefaultQuantity = 19,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a3333333-3333-3333-3333-333333333335"),
                    GameTemplateId = new Guid("33333333-3333-3333-3333-333333333333"),
                    ComponentName = "Skip Cards",
                    DefaultQuantity = 8,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a3333333-3333-3333-3333-333333333336"),
                    GameTemplateId = new Guid("33333333-3333-3333-3333-333333333333"),
                    ComponentName = "Reverse Cards",
                    DefaultQuantity = 8,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a3333333-3333-3333-3333-333333333337"),
                    GameTemplateId = new Guid("33333333-3333-3333-3333-333333333333"),
                    ComponentName = "Draw Two Cards",
                    DefaultQuantity = 8,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a3333333-3333-3333-3333-333333333338"),
                    GameTemplateId = new Guid("33333333-3333-3333-3333-333333333333"),
                    ComponentName = "Wild Cards",
                    DefaultQuantity = 4,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a3333333-3333-3333-3333-333333333339"),
                    GameTemplateId = new Guid("33333333-3333-3333-3333-333333333333"),
                    ComponentName = "Wild Draw Four Cards",
                    DefaultQuantity = 4,
                    CreatedAt = seedDate
                },

                // Splendor Components (GameTemplateId: 44444444-4444-4444-4444-444444444444)
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-444444444441"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Ruby Gem Tokens",
                    DefaultQuantity = 7,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-444444444442"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Sapphire Gem Tokens",
                    DefaultQuantity = 7,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-444444444443"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Emerald Gem Tokens",
                    DefaultQuantity = 7,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-444444444444"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Onyx Gem Tokens",
                    DefaultQuantity = 7,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-444444444445"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Diamond Gem Tokens",
                    DefaultQuantity = 7,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-444444444446"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Gold Joker Tokens",
                    DefaultQuantity = 5,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-444444444447"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Development Cards (Tier 1)",
                    DefaultQuantity = 40,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-444444444448"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Development Cards (Tier 2)",
                    DefaultQuantity = 30,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-444444444449"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Development Cards (Tier 3)",
                    DefaultQuantity = 20,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a4444444-4444-4444-4444-44444444444a"),
                    GameTemplateId = new Guid("44444444-4444-4444-4444-444444444444"),
                    ComponentName = "Noble Tiles",
                    DefaultQuantity = 10,
                    CreatedAt = seedDate
                },

                // Werewolf Ultimate Components (GameTemplateId: 55555555-5555-5555-5555-555555555555)
                new GameComponentTemplate
                {
                    Id = new Guid("a5555555-5555-5555-5555-555555555551"),
                    GameTemplateId = new Guid("55555555-5555-5555-5555-555555555555"),
                    ComponentName = "Villager Role Cards",
                    DefaultQuantity = 10,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a5555555-5555-5555-5555-555555555552"),
                    GameTemplateId = new Guid("55555555-5555-5555-5555-555555555555"),
                    ComponentName = "Werewolf Role Cards",
                    DefaultQuantity = 4,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a5555555-5555-5555-5555-555555555553"),
                    GameTemplateId = new Guid("55555555-5555-5555-5555-555555555555"),
                    ComponentName = "Seer Role Card",
                    DefaultQuantity = 1,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a5555555-5555-5555-5555-555555555554"),
                    GameTemplateId = new Guid("55555555-5555-5555-5555-555555555555"),
                    ComponentName = "Doctor Role Card",
                    DefaultQuantity = 1,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a5555555-5555-5555-5555-555555555555"),
                    GameTemplateId = new Guid("55555555-5555-5555-5555-555555555555"),
                    ComponentName = "Witch Role Card",
                    DefaultQuantity = 1,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a5555555-5555-5555-5555-555555555556"),
                    GameTemplateId = new Guid("55555555-5555-5555-5555-555555555555"),
                    ComponentName = "Hunter Role Card",
                    DefaultQuantity = 1,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a5555555-5555-5555-5555-555555555557"),
                    GameTemplateId = new Guid("55555555-5555-5555-5555-555555555555"),
                    ComponentName = "Moderator Script",
                    DefaultQuantity = 1,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a5555555-5555-5555-5555-555555555558"),
                    GameTemplateId = new Guid("55555555-5555-5555-5555-555555555555"),
                    ComponentName = "Night Phase Marker",
                    DefaultQuantity = 1,
                    CreatedAt = seedDate
                },
                new GameComponentTemplate
                {
                    Id = new Guid("a5555555-5555-5555-5555-555555555559"),
                    GameTemplateId = new Guid("55555555-5555-5555-5555-555555555555"),
                    ComponentName = "Day Phase Marker",
                    DefaultQuantity = 1,
                    CreatedAt = seedDate
                }
            );
        }
    }
}
