using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class GameTemplateService : IGameTemplateService
    {
        private readonly IGameTemplateRepository _gameTemplateRepository;
        private readonly ICafeInventoryRepository _inventoryRepository;

        public GameTemplateService(
            IGameTemplateRepository gameTemplateRepository,
            ICafeInventoryRepository inventoryRepository)
        {
            _gameTemplateRepository = gameTemplateRepository;
            _inventoryRepository = inventoryRepository;
        }

        public async Task<PaginatedResponse<MasterGameResponseDto>> GetMasterGamesAsync(GetMasterGamesQuery query)
        {
            var result = await _gameTemplateRepository.GetPagedAsync(query);

            HashSet<Guid>? inInventoryIds = null;
            if (query.CafeId.HasValue)
            {
                inInventoryIds = await _inventoryRepository.GetActiveGameTemplateIdsByCafeAsync(query.CafeId.Value);
            }

            var dtoData = result.Data.Select(game => new MasterGameResponseDto
            {
                Id = game.Id,
                BggGameId = game.BggGameId,
                Name = game.Name,
                ThumbnailUrl = game.ThumbnailUrl,
                Description = game.Description,
                MinPlayers = game.MinPlayers,
                MaxPlayers = game.MaxPlayers,
                PlayTime = game.PlayTime,
                CreatedAt = game.CreatedAt,
                UpdatedAt = game.UpdatedAt,
                AlreadyInInventory = inInventoryIds != null ? inInventoryIds.Contains(game.Id) : null,
                Components = game.Components.Select(c => new GameComponentTemplateDto
                {
                    Id = c.Id,
                    GameTemplateId = c.GameTemplateId,
                    ComponentName = c.ComponentName,
                    DefaultQuantity = c.DefaultQuantity,
                    CreatedAt = c.CreatedAt
                }).ToList()
            }).ToList();

            return new PaginatedResponse<MasterGameResponseDto>
            {
                Data = dtoData,
                Meta = result.Meta
            };
        }
    }
}
