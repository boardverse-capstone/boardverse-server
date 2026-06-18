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
            builder.Property(g => g.NameSearchKey).IsRequired().HasMaxLength(200);
            builder.Property(g => g.SearchAliases).HasMaxLength(500);
            builder.Property(g => g.SearchAliasesKey).IsRequired().HasMaxLength(500);
            builder.Property(g => g.ThumbnailUrl).HasMaxLength(500);
            builder.Property(g => g.Description).HasMaxLength(2000);
            builder.Property(g => g.MinPlayers).IsRequired();
            builder.Property(g => g.MaxPlayers).IsRequired();
            builder.Property(g => g.PlayTime).IsRequired();
            builder.Property(g => g.IsActive).IsRequired().HasDefaultValue(true);
            builder.Property(g => g.BggId);
            builder.Property(g => g.BggSyncedAt);
            builder.HasIndex(g => g.BggId).IsUnique().HasFilter("\"BggId\" IS NOT NULL");
            builder.Property(g => g.CreatedAt).IsRequired();
            builder.Property(g => g.UpdatedAt).IsRequired();

            builder.HasIndex(g => g.NameSearchKey);
            builder.HasIndex(g => g.SearchAliasesKey);

            // Seed data for 5 popular board games with hardcoded IDs
            var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            builder.HasData(
                new GameTemplate
                {
                    Id = new Guid("11111111-1111-1111-1111-111111111111"),
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
                    Name = "Werewolf Ultimate",
                    SearchAliases = "Ma Sói, Ma Soi, Werewolf, Ma Sói Ultimate",
                    ThumbnailUrl = "https://example.com/images/werewolf.jpg",
                    Description = "Trò chơi suy luận vai trò: phe Dân làng phải tìm ra Ma Sói trước khi bị loại hết.",
                    MinPlayers = 5,
                    MaxPlayers = 20,
                    PlayTime = 45,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new GameTemplate
                {
                    Id = new Guid("66666666-6666-6666-6666-666666666666"),
                    Name = "The Resistance: Avalon",
                    ThumbnailUrl = "https://example.com/images/avalon.jpg",
                    Description = "Phe Hiệp sĩ phải hoàn thành 3 nhiệm vụ thành công, trong khi phe Phản bội âm thầm phá hoại. Mỗi vòng bỏ phiếu và suy luận vai trò quyết định thắng thua.",
                    MinPlayers = 5,
                    MaxPlayers = 10,
                    PlayTime = 30,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new GameTemplate
                {
                    Id = new Guid("77777777-7777-7777-7777-777777777777"),
                    Name = "Codenames",
                    ThumbnailUrl = "https://example.com/images/codenames.jpg",
                    Description = "Hai đội tranh đấu để tìm mật danh của đồng đội qua gợi ý một từ duy nhất. Trò chơi party nhanh, dễ học.",
                    MinPlayers = 4,
                    MaxPlayers = 8,
                    PlayTime = 15,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new GameTemplate
                {
                    Id = new Guid("88888888-8888-8888-8888-888888888888"),
                    Name = "Pandemic",
                    ThumbnailUrl = "https://example.com/images/pandemic.jpg",
                    Description = "Người chơi hợp tác với tư cách đội phản ứng dịch bệnh toàn cầu, chữa bệnh và ngăn dịch bùng phát.",
                    MinPlayers = 2,
                    MaxPlayers = 4,
                    PlayTime = 45,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                }
            );
        }
    }
}
