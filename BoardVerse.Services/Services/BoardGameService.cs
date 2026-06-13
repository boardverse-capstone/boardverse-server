using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class BoardGameService : IBoardGameService
    {
        private readonly IGameTemplateRepository _gameTemplateRepository;
        private readonly ICategoryRepository _categoryRepository;

        public BoardGameService(
            IGameTemplateRepository gameTemplateRepository,
            ICategoryRepository categoryRepository)
        {
            _gameTemplateRepository = gameTemplateRepository;
            _categoryRepository = categoryRepository;
        }

        public async Task<PaginatedResponse<BoardGameListItemDto>> SearchBoardGamesAsync(GetBoardGamesQuery query)
        {
            var result = await _gameTemplateRepository.GetBoardGamesPagedAsync(query.ToMasterGamesQuery());
            var componentCounts = await _gameTemplateRepository.GetComponentCountsByGameIdsAsync(
                result.Data.Select(g => g.Id).ToList());

            return new PaginatedResponse<BoardGameListItemDto>
            {
                Data = result.Data.Select(game => MapListItem(game, componentCounts.GetValueOrDefault(game.Id))).ToList(),
                Meta = result.Meta
            };
        }

        public Task<BoardGameDetailDto> GetBoardGameDetailsAsync(Guid id) =>
            GetBoardGameByIdAsync(id);

        public async Task<BoardGameDetailDto> GetBoardGameByIdAsync(Guid id)
        {
            var game = await _gameTemplateRepository.GetActiveByIdWithComponentsAsync(id);
            if (game == null)
                throw new BoardGameNotFoundException(ApiErrorMessages.BoardGame.NotFound(id));

            return MapDetail(game);
        }

        public async Task<List<CategoryDto>> GetCategoriesAsync()
        {
            var categories = await _categoryRepository.GetAllActiveAsync();
            return categories.Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                Description = c.Description,
                SortOrder = c.SortOrder
            }).ToList();
        }

        private static BoardGameListItemDto MapListItem(GameTemplate game, int componentCount) =>
            new()
            {
                Id = game.Id,
                Name = game.Name,
                ThumbnailUrl = game.ThumbnailUrl,
                Description = game.Description,
                MinPlayers = game.MinPlayers,
                MaxPlayers = game.MaxPlayers,
                PlayTime = game.PlayTime,
                ComponentCount = componentCount,
                Categories = GameCatalogMapper.MapCategories(game)
            };

        private static BoardGameDetailDto MapDetail(GameTemplate game) =>
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
                Categories = GameCatalogMapper.MapCategories(game),
                Components = GameCatalogMapper.MapComponents(game.Components)
            };
    }
}

