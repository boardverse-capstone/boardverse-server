using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
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
                inInventoryIds = await _inventoryRepository.GetActiveGameTemplateIdsByCafeAsync(query.CafeId.Value);

            return new PaginatedResponse<MasterGameResponseDto>
            {
                Data = result.Data.Select(g => MapToMasterDto(g, inInventoryIds)).ToList(),
                Meta = result.Meta
            };
        }

        public async Task<MasterGameResponseDto> GetMasterGameByIdAsync(Guid id, Guid? cafeId = null)
        {
            var game = await _gameTemplateRepository.GetByIdWithComponentsAsync(id);
            if (game == null)
                throw new BoardGameNotFoundException("Không tìm thấy board game master.");

            HashSet<Guid>? inInventoryIds = null;
            if (cafeId.HasValue)
                inInventoryIds = await _inventoryRepository.GetActiveGameTemplateIdsByCafeAsync(cafeId.Value);

            return MapToMasterDto(game, inInventoryIds);
        }

        private static MasterGameResponseDto MapToMasterDto(GameTemplate game, HashSet<Guid>? inInventoryIds) =>
            new()
            {
                Id = game.Id,
                Name = game.Name,
                ThumbnailUrl = game.ThumbnailUrl,
                Description = game.Description,
                MinPlayers = game.MinPlayers,
                MaxPlayers = game.MaxPlayers,
                PlayTime = game.PlayTime,
                CreatedAt = game.CreatedAt,
                UpdatedAt = game.UpdatedAt,
                AlreadyInInventory = inInventoryIds != null ? inInventoryIds.Contains(game.Id) : null,
                Categories = GameCatalogMapper.MapCategories(game),
                Components = game.Components.Select(c => new GameComponentTemplateDto
                {
                    Id = c.Id,
                    GameTemplateId = c.GameTemplateId,
                    ComponentName = c.ComponentName,
                    DefaultQuantity = c.DefaultQuantity,
                    CreatedAt = c.CreatedAt
                }).ToList()
            };
    }
}

