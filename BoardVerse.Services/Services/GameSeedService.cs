using BoardVerse.Core.DTOs.BGG;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services
{
    public class GameSeedService : IGameSeedService
    {
        private readonly IBggApiService _bggApiService;
        private readonly IGameTemplateRepository _gameTemplateRepository;
        private readonly BoardVerseDbContext _context;

        public GameSeedService(
            IBggApiService bggApiService,
            IGameTemplateRepository gameTemplateRepository,
            BoardVerseDbContext context)
        {
            _bggApiService = bggApiService;
            _gameTemplateRepository = gameTemplateRepository;
            _context = context;
        }

        public async Task SeedGamesFromBggAsync(List<int> bggGameIds)
        {
            Console.WriteLine($"Fetching {bggGameIds.Count} games from BGG...");
            
            var bggGames = await _bggApiService.GetGamesByIdsAsync(bggGameIds);
            
            Console.WriteLine($"Successfully fetched {bggGames.Count} games from BGG");
            
            foreach (var bggGame in bggGames)
            {
                await SeedSingleGameAsync(bggGame);
            }
            
            Console.WriteLine("Seeding completed!");
        }

        public async Task SeedSingleGameFromBggAsync(int bggGameId)
        {
            Console.WriteLine($"Fetching game {bggGameId} from BGG...");
            var bggGame = await _bggApiService.GetGameByIdAsync(bggGameId);
            
            if (bggGame == null)
            {
                Console.WriteLine($"Failed to fetch game {bggGameId}");
                return;
            }
            
            await SeedSingleGameAsync(bggGame);
        }

        private async Task SeedSingleGameAsync(BggGameDto bggGame)
        {
            // Check if game already exists
            var existingGame = await _context.GameTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Name == bggGame.Name);
            
            if (existingGame != null)
            {
                Console.WriteLine($"Game '{bggGame.Name}' already exists in database. Skipping.");
                return;
            }

            // Map BGG data to GameTemplate
            var gameTemplate = new GameTemplate
            {
                Id = Guid.NewGuid(),
                Name = bggGame.Name,
                ThumbnailUrl = bggGame.ThumbnailUrl ?? bggGame.ImageUrl,
                Description = bggGame.Description,
                MinPlayers = bggGame.MinPlayers > 0 ? bggGame.MinPlayers : 1,
                MaxPlayers = bggGame.MaxPlayers > 0 ? bggGame.MaxPlayers : 4,
                PlayTime = bggGame.PlayingTime > 0 ? bggGame.PlayingTime : 60,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Components = new List<GameComponentTemplate>()
            };

            // Map components
            foreach (var bggComponent in bggGame.Components)
            {
                var component = new GameComponentTemplate
                {
                    Id = Guid.NewGuid(),
                    GameTemplateId = gameTemplate.Id,
                    ComponentName = bggComponent.Name,
                    DefaultQuantity = bggComponent.Quantity > 0 ? bggComponent.Quantity : 1,
                    CreatedAt = DateTime.UtcNow
                };
                gameTemplate.Components.Add(component);
            }

            // Add to database
            await _context.GameTemplates.AddAsync(gameTemplate);
            await _context.SaveChangesAsync();

            Console.WriteLine($"✓ Seeded game: {bggGame.Name} with {gameTemplate.Components.Count} components");
        }
    }
}
