using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Caching.Memory;

namespace BoardVerse.Services.Services
{
    public class BoardGameService : IBoardGameService
    {
        // Cache TTL cho widget "Top N board game phổ biến". Thấp vì play count tăng real-time.
        private static readonly TimeSpan TopGamesCacheTtl = TimeSpan.FromMinutes(5);
        private const string TopGamesCacheKeyPrefix = "BoardVerse:BoardGames:Top:";

        private readonly IGameTemplateRepository _gameTemplateRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ICafeRepository _cafeRepository;
        private readonly IPlayerBoardGameSaveRepository _playerBoardGameSaveRepository;
        private readonly IMemoryCache _memoryCache;

        public BoardGameService(
            IGameTemplateRepository gameTemplateRepository,
            ICategoryRepository categoryRepository,
            ICafeRepository cafeRepository,
            IPlayerBoardGameSaveRepository playerBoardGameSaveRepository,
            IMemoryCache memoryCache)
        {
            _gameTemplateRepository = gameTemplateRepository;
            _categoryRepository = categoryRepository;
            _cafeRepository = cafeRepository;
            _playerBoardGameSaveRepository = playerBoardGameSaveRepository;
            _memoryCache = memoryCache;
        }

        public async Task<PaginatedResponse<BoardGameListItemDto>> SearchBoardGamesAsync(GetBoardGamesQuery query, CancellationToken cancellationToken = default)
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

        public async Task<BoardGameDetailDto> GetBoardGameByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var game = await _gameTemplateRepository.GetActiveByIdWithComponentsAsync(id);
            if (game == null)
                throw new BoardGameNotFoundException(ApiErrorMessages.BoardGame.NotFound(id));

            return MapDetail(game);
        }

        public async Task<List<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default)
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

        public async Task<List<TopBoardGameDto>> GetTopPlayedBoardGamesAsync(int topCount = 5, CancellationToken cancellationToken = default)
        {
            if (topCount <= 0)
            {
                throw new BadRequestException("Số lượng board game phải lớn hơn 0.");
            }

            // Cache key theo topCount để mỗi kích thước có bucket riêng.
            var cacheKey = $"{TopGamesCacheKeyPrefix}{topCount}";
            if (_memoryCache.TryGetValue(cacheKey, out List<TopBoardGameDto>? cached) && cached != null)
            {
                return cached;
            }

            var result = await _gameTemplateRepository.GetTopPlayedBoardGamesAsync(topCount, cancellationToken);

            _memoryCache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TopGamesCacheTtl,
                Priority = CacheItemPriority.Normal
            });

            return result;
        }

        public async Task<GamePlayConfigurationDto> GetPlayConfigurationAsync(Guid gameTemplateId, CancellationToken cancellationToken = default)
        {
            var game = await RequireActiveGameAsync(gameTemplateId);
            return MapPlayConfiguration(game);
        }

        public async Task<GamePlayNavigationResponseDto> ResolvePlayNavigationAsync(
            Guid gameTemplateId,
            ResolveGamePlayNavigationRequestDto request, CancellationToken cancellationToken = default)
        {
            var game = await RequireActiveGameAsync(gameTemplateId);

            if (request.PlayMode == PlayerPlayMode.Solo && !GamePlayRoutingHelper.SupportsSoloPlay(game.MinPlayers))
            {
                throw new BadRequestException(
                    ApiErrorMessages.BoardGame.SoloPlayNotSupported(gameTemplateId, game.MinPlayers));
            }

            var navigationTarget = GamePlayRoutingHelper.ResolveNavigationTarget(game.MinPlayers, request.PlayMode);

            return new GamePlayNavigationResponseDto
            {
                GameTemplateId = game.Id,
                GameName = game.Name,
                PlayMode = request.PlayMode,
                MinPlayers = game.MinPlayers,
                MaxPlayers = game.MaxPlayers,
                SupportsSoloPlay = GamePlayRoutingHelper.SupportsSoloPlay(game.MinPlayers),
                NavigationTarget = navigationTarget,
                RoomConfiguration = GamePlayRoutingHelper.BuildRoomConfiguration(
                    game.MinPlayers,
                    game.MaxPlayers,
                    navigationTarget)
            };
        }

        public async Task<ActiveCafesByBoardGameResponseDto> GetActiveCafesByBoardGameAsync(
            Guid boardGameId,
            Guid? userId,
            ActiveCafesByBoardGameQueryDto query,
            CancellationToken cancellationToken = default)
        {
            // Validate board game tồn tại & còn active trước khi query ngược ra cafe.
            // Tránh trả danh sách rỗng ambiguous cho client (không phân biệt được
            // "game không tồn tại" vs "game tồn tại nhưng không có cafe nào có").
            var game = await _gameTemplateRepository.GetActiveByIdWithComponentsAsync(boardGameId, cancellationToken);
            if (game == null)
            {
                throw new BoardGameNotFoundException(ApiErrorMessages.BoardGame.NotFound(boardGameId));
            }

            var pagination = new PaginationParams
            {
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };

            // Chỉ truyền lat/lng khi cả 2 đều có — nếu chỉ truyền 1 thì coi như không có,
            // tránh tính khoảng cách sai.
            bool hasLocation = query.Latitude.HasValue && query.Longitude.HasValue;

            var cafes = await _cafeRepository.GetActiveCafesByBoardGameAsync(
                boardGameId,
                hasLocation ? query.Latitude : null,
                hasLocation ? query.Longitude : null,
                query.Name,
                pagination,
                cancellationToken);

            // ── IsSaved: xác định board game này có nằm trong danh sách yêu thích của player ──
            // Tương tự field `isSaved` trong DiscoveryBoardGameDto (POST /api/v1/discovery/survey).
            // - userId = null (anonymous): mặc định false (player chưa đăng nhập nên chưa có favorites).
            // - userId có giá trị: tra PlayerBoardGameSave; nếu không có row thì false.
            // Lưu ý: chỉ cần 1 lần EXISTS cho 1 board game, không phụ thuộc vào danh sách cafe.
            bool isSaved = false;
            if (userId.HasValue)
            {
                isSaved = await _playerBoardGameSaveRepository.ExistsAsync(
                    userId.Value,
                    boardGameId,
                    cancellationToken);
            }

            return new ActiveCafesByBoardGameResponseDto
            {
                IsSaved = isSaved,
                Cafes = cafes
            };
        }

        private async Task<GameTemplate> RequireActiveGameAsync(Guid gameTemplateId)
        {
            var game = await _gameTemplateRepository.GetActiveByIdWithComponentsAsync(gameTemplateId);
            if (game == null)
            {
                throw new BoardGameNotFoundException(ApiErrorMessages.BoardGame.NotFound(gameTemplateId));
            }

            return game;
        }

        private static GamePlayConfigurationDto MapPlayConfiguration(GameTemplate game) =>
            new()
            {
                GameTemplateId = game.Id,
                GameName = game.Name,
                MinPlayers = game.MinPlayers,
                MaxPlayers = game.MaxPlayers,
                SupportsSoloPlay = GamePlayRoutingHelper.SupportsSoloPlay(game.MinPlayers),
                AvailablePlayModes = GamePlayRoutingHelper.GetAvailablePlayModes(game.MinPlayers)
            };

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

