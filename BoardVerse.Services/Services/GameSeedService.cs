using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Helpers;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services
{
    public class GameSeedService : IGameSeedService
    {
        private readonly BoardVerseDbContext _context;

        public GameSeedService(BoardVerseDbContext context)
        {
            _context = context;
        }

        public async Task SeedGamesFromCatalogAsync(List<string>? slugs = null)
        {
            var targetSlugs = slugs ?? GameCatalog.PopularGameSlugs.ToList();
            Console.WriteLine($"Seeding {targetSlugs.Count} games from master catalog...");

            foreach (var slug in targetSlugs)
            {
                var catalogEntry = GameCatalog.GetBySlug(slug);
                if (catalogEntry == null)
                {
                    Console.WriteLine($"  ✗ No catalog entry for slug '{slug}'");
                    continue;
                }

                await UpsertGameAsync(MapCatalogToDto(catalogEntry));
            }

            Console.WriteLine("Catalog seeding completed!");
        }

        public async Task SeedSingleGameAsync(string slug)
        {
            var catalogEntry = GameCatalog.GetBySlug(slug);
            if (catalogEntry == null)
            {
                Console.WriteLine($"  ✗ No catalog entry for slug '{slug}'");
                return;
            }

            await UpsertGameAsync(MapCatalogToDto(catalogEntry));
        }

        private static GameCatalogSeedDto MapCatalogToDto(GameCatalogEntry entry) =>
            new()
            {
                Slug = entry.Slug,
                Name = entry.Name,
                Description = entry.Description,
                MinPlayers = entry.MinPlayers,
                MaxPlayers = entry.MaxPlayers,
                PlayTime = entry.PlayTime,
                BggId = entry.BggId,
                Components = entry.Components
                    .Select(c => new GameCatalogComponentDto { Name = c.Name, Quantity = c.Quantity })
                    .ToList()
            };

        private async Task UpsertGameAsync(GameCatalogSeedDto game)
        {
            var existing = await _context.GameTemplates
                .Include(g => g.Components)
                .Include(g => g.Categories)
                .FirstOrDefaultAsync(g => g.Name == game.Name);

            if (existing != null)
            {
                ApplyGameFields(existing, game);

                if (existing.Components.Count == 0 && game.Components.Count > 0)
                    AddComponents(existing, game.Components);

                await EnsureCategoriesAndAliasesAsync(existing);
                await _context.SaveChangesAsync();
                Console.WriteLine($"  ↻ Updated: {existing.Name} ({existing.Components.Count} components)");
                return;
            }

            var minPlayers = game.MinPlayers > 0 ? game.MinPlayers : 1;
            var maxPlayers = game.MaxPlayers > 0 ? game.MaxPlayers : 4;
            if (minPlayers > maxPlayers)
                (minPlayers, maxPlayers) = (maxPlayers, minPlayers);

            var gameTemplate = new GameTemplate
            {
                Id = Guid.NewGuid(),
                Name = game.Name,
                ThumbnailUrl = game.ThumbnailUrl,
                Description = game.Description,
                MaxPlayers = maxPlayers,
                MinPlayers = minPlayers,
                PlayTime = game.PlayTime > 0 ? game.PlayTime : 60,
                BggId = game.BggId,
                BggSyncedAt = game.BggId.HasValue ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Components = []
            };

            ApplySearchAliases(gameTemplate);
            AddComponents(gameTemplate, game.Components);

            await _context.GameTemplates.AddAsync(gameTemplate);
            await EnsureCategoriesAndAliasesAsync(gameTemplate);
            await _context.SaveChangesAsync();
            Console.WriteLine($"  ✓ Inserted: {gameTemplate.Name} ({gameTemplate.Components.Count} components)");
        }

        private static void ApplyGameFields(GameTemplate existing, GameCatalogSeedDto game)
        {
            existing.Name = game.Name;
            existing.NameSearchKey = VietnameseTextNormalizer.ToSearchKey(game.Name);
            existing.ThumbnailUrl = game.ThumbnailUrl ?? existing.ThumbnailUrl;
            existing.Description = game.Description ?? existing.Description;
            existing.MinPlayers = game.MinPlayers > 0 ? game.MinPlayers : existing.MinPlayers;
            existing.MaxPlayers = game.MaxPlayers > 0 ? game.MaxPlayers : existing.MaxPlayers;
            existing.PlayTime = game.PlayTime > 0 ? game.PlayTime : existing.PlayTime;
            if (game.BggId.HasValue)
            {
                existing.BggId = game.BggId;
                existing.BggSyncedAt = DateTime.UtcNow;
            }
            existing.UpdatedAt = DateTime.UtcNow;
            ApplySearchAliases(existing);
        }

        private static void ApplySearchAliases(GameTemplate game)
        {
            var aliases = GameCategorySeedMap.GetSearchAliases(game.Name);
            if (!string.IsNullOrWhiteSpace(aliases))
                game.SearchAliases = aliases;
        }

        private async Task EnsureCategoriesAndAliasesAsync(GameTemplate game)
        {
            var slugs = GameCategorySeedMap.GetCategorySlugs(game.Name);
            if (slugs.Count == 0)
                return;

            var categories = await _context.Categories
                .Where(c => slugs.Contains(c.Slug))
                .ToListAsync();

            var existingCategoryIds = game.Categories.Select(gc => gc.CategoryId).ToHashSet();

            foreach (var category in categories)
            {
                if (existingCategoryIds.Contains(category.Id))
                    continue;

                _context.GameTemplateCategories.Add(new GameTemplateCategory
                {
                    GameTemplateId = game.Id,
                    CategoryId = category.Id,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        private static void AddComponents(GameTemplate gameTemplate, IEnumerable<GameCatalogComponentDto> components)
        {
            foreach (var component in components)
            {
                gameTemplate.Components.Add(new GameComponentTemplate
                {
                    Id = Guid.NewGuid(),
                    GameTemplateId = gameTemplate.Id,
                    ComponentName = component.Name,
                    ComponentKind = BoardVerse.Core.Data.ComponentCatalog.ResolveKindFromName(component.Name),
                    DefaultQuantity = component.Quantity > 0 ? component.Quantity : 1,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
    }
}
