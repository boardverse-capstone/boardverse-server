using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.BGG;
using BoardVerse.Core.Entities;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services
{
    public class GameSeedService : IGameSeedService
    {
        private readonly IBggApiService _bggApiService;
        private readonly BoardVerseDbContext _context;

        public GameSeedService(IBggApiService bggApiService, BoardVerseDbContext context)
        {
            _bggApiService = bggApiService;
            _context = context;
        }

        public async Task SeedGamesFromBggAsync(List<int> bggGameIds)
        {
            if (!_bggApiService.IsConfigured)
            {
                Console.WriteLine("BGG API token not configured — falling back to curated catalog.");
                await SeedGamesFromCatalogAsync(bggGameIds);
                return;
            }

            Console.WriteLine($"Fetching {bggGameIds.Count} games from BGG XML API...");

            var bggGames = await _bggApiService.GetGamesByIdsAsync(bggGameIds);
            Console.WriteLine($"Successfully fetched {bggGames.Count} games from BGG");

            if (bggGames.Count == 0)
            {
                Console.WriteLine("No games returned from BGG — falling back to curated catalog.");
                await SeedGamesFromCatalogAsync(bggGameIds);
                return;
            }

            foreach (var bggGame in bggGames)
                await UpsertGameAsync(bggGame);

            var missingIds = bggGameIds.Except(bggGames.Select(g => g.BggGameId)).ToList();
            if (missingIds.Count > 0)
            {
                Console.WriteLine($"Seeding {missingIds.Count} missing games from catalog...");
                await SeedGamesFromCatalogAsync(missingIds);
            }

            Console.WriteLine("BGG seeding completed!");
        }

        public async Task SeedSingleGameFromBggAsync(int bggGameId)
        {
            if (!_bggApiService.IsConfigured)
            {
                await SeedGamesFromCatalogAsync([bggGameId]);
                return;
            }

            Console.WriteLine($"Fetching game {bggGameId} from BGG...");
            var bggGame = await _bggApiService.GetGameByIdAsync(bggGameId);

            if (bggGame == null)
            {
                Console.WriteLine($"BGG fetch failed for {bggGameId} — using catalog.");
                await SeedGamesFromCatalogAsync([bggGameId]);
                return;
            }

            await UpsertGameAsync(bggGame);
        }

        public async Task SeedGamesFromCatalogAsync(List<int>? bggGameIds = null)
        {
            var ids = bggGameIds ?? BggKnownGameCatalog.PopularGameIds.ToList();
            Console.WriteLine($"Seeding {ids.Count} games from curated master catalog...");

            foreach (var bggGameId in ids)
            {
                var catalogEntry = BggKnownGameCatalog.GetById(bggGameId);
                if (catalogEntry == null)
                {
                    Console.WriteLine($"  ✗ No catalog entry for BGG id {bggGameId}");
                    continue;
                }

                var dto = MapCatalogToDto(catalogEntry);
                await UpsertGameAsync(dto);
            }

            Console.WriteLine("Catalog seeding completed!");
        }

        private static BggGameDto MapCatalogToDto(KnownBggGameEntry entry) =>
            new()
            {
                Id = Guid.NewGuid(),
                BggGameId = entry.BggGameId,
                Name = entry.Name,
                Description = entry.Description,
                MinPlayers = entry.MinPlayers,
                MaxPlayers = entry.MaxPlayers,
                PlayingTime = entry.PlayTime,
                Components = entry.Components
                    .Select(c => new BggComponentDto { Name = c.Name, Quantity = c.Quantity })
                    .ToList()
            };

        private async Task UpsertGameAsync(BggGameDto bggGame)
        {
            var existing = await _context.GameTemplates
                .Include(g => g.Components)
                .FirstOrDefaultAsync(g =>
                    (bggGame.BggGameId > 0 && g.BggGameId == bggGame.BggGameId) ||
                    g.Name == bggGame.Name);

            if (existing != null)
            {
                existing.BggGameId = bggGame.BggGameId > 0 ? bggGame.BggGameId : existing.BggGameId;
                existing.Name = bggGame.Name;
                existing.ThumbnailUrl = bggGame.ThumbnailUrl ?? bggGame.ImageUrl ?? existing.ThumbnailUrl;
                existing.Description = bggGame.Description ?? existing.Description;
                existing.MinPlayers = bggGame.MinPlayers > 0 ? bggGame.MinPlayers : existing.MinPlayers;
                existing.MaxPlayers = bggGame.MaxPlayers > 0 ? bggGame.MaxPlayers : existing.MaxPlayers;
                existing.PlayTime = bggGame.PlayingTime > 0 ? bggGame.PlayingTime : existing.PlayTime;
                existing.UpdatedAt = DateTime.UtcNow;

                if (existing.Components.Count == 0 && bggGame.Components.Count > 0)
                    AddComponents(existing, bggGame.Components);

                await _context.SaveChangesAsync();
                Console.WriteLine($"  ↻ Updated: {existing.Name} ({existing.Components.Count} components)");
                return;
            }

            var minPlayers = bggGame.MinPlayers > 0 ? bggGame.MinPlayers : 1;
            var maxPlayers = bggGame.MaxPlayers > 0 ? bggGame.MaxPlayers : 4;
            if (minPlayers > maxPlayers)
                (minPlayers, maxPlayers) = (maxPlayers, minPlayers);

            var gameTemplate = new GameTemplate
            {
                Id = Guid.NewGuid(),
                BggGameId = bggGame.BggGameId > 0 ? bggGame.BggGameId : null,
                Name = bggGame.Name,
                ThumbnailUrl = bggGame.ThumbnailUrl ?? bggGame.ImageUrl,
                Description = bggGame.Description,
                MaxPlayers = maxPlayers,
                MinPlayers = minPlayers,
                PlayTime = bggGame.PlayingTime > 0 ? bggGame.PlayingTime : 60,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Components = []
            };

            AddComponents(gameTemplate, bggGame.Components);

            await _context.GameTemplates.AddAsync(gameTemplate);
            await _context.SaveChangesAsync();
            Console.WriteLine($"  ✓ Inserted: {gameTemplate.Name} ({gameTemplate.Components.Count} components)");
        }

        private static void AddComponents(GameTemplate gameTemplate, IEnumerable<BggComponentDto> components)
        {
            foreach (var bggComponent in components)
            {
                gameTemplate.Components.Add(new GameComponentTemplate
                {
                    Id = Guid.NewGuid(),
                    GameTemplateId = gameTemplate.Id,
                    ComponentName = bggComponent.Name,
                    DefaultQuantity = bggComponent.Quantity > 0 ? bggComponent.Quantity : 1,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
    }
}
