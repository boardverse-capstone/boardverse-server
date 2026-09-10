using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data.Helpers;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class GameTemplateRepository : IGameTemplateRepository
    {
        private readonly BoardVerseDbContext _context;

        public GameTemplateRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public Task<PaginatedResponse<GameTemplate>> GetBoardGamesPagedAsync(GetMasterGamesQuery query, CancellationToken cancellationToken = default) =>
            GetPagedInternalAsync(query, includeComponents: false);

        public Task<PaginatedResponse<GameTemplate>> GetPagedAsync(GetMasterGamesQuery query, CancellationToken cancellationToken = default) =>
            GetPagedInternalAsync(query, includeComponents: true);

        private async Task<PaginatedResponse<GameTemplate>> GetPagedInternalAsync(
            GetMasterGamesQuery query,
            bool includeComponents)
        {
            var baseQuery = _context.GameTemplates
                .AsNoTracking()
                .Where(g => g.IsActive)
                .AsQueryable();

            if (includeComponents)
                baseQuery = baseQuery.Include(g => g.Components);

            baseQuery = baseQuery
                .Include(g => g.Categories)
                    .ThenInclude(gc => gc.Category);

            baseQuery = ApplyFilters(baseQuery, query).OrderBy(g => g.Name);

            var totalItems = await baseQuery.CountAsync();
            var skipPagination = query.SkipPagination;
            var items = skipPagination
                ? await baseQuery.ToListAsync()
                : await baseQuery
                    .Skip((query.PageNumber - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToListAsync();

            var totalPages = skipPagination
                ? (totalItems == 0 ? 0 : 1)
                : (int)Math.Ceiling(totalItems / (double)query.PageSize);

            return new PaginatedResponse<GameTemplate>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = skipPagination ? 1 : query.PageNumber,
                    PageSize = skipPagination ? totalItems : query.PageSize,
                    TotalItems = totalItems,
                    TotalPages = totalPages
                }
            };
        }

        private IQueryable<GameTemplate> ApplyFilters(IQueryable<GameTemplate> baseQuery, GetMasterGamesQuery query)
        {
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
                baseQuery = GameSearchHelper.ApplyFuzzyNameSearch(baseQuery, query.SearchTerm);

            if (query.CategoryIds is { Count: > 0 })
            {
                baseQuery = baseQuery.Where(g =>
                    g.Categories.Any(gc => query.CategoryIds.Contains(gc.CategoryId)));
            }

            if (query.PlayerCount.HasValue)
            {
                var playerCount = query.PlayerCount.Value;
                baseQuery = baseQuery.Where(g =>
                    g.MinPlayers <= playerCount && g.MaxPlayers >= playerCount);
            }

            if (query.PlayTimeRanges is { Count: > 0 })
            {
                baseQuery = baseQuery.Where(g =>
                    (query.PlayTimeRanges.Contains(PlayTimeRange.Under30) && g.PlayTime < 30) ||
                    (query.PlayTimeRanges.Contains(PlayTimeRange.ThirtyToSixty) && g.PlayTime >= 30 && g.PlayTime <= 60) ||
                    (query.PlayTimeRanges.Contains(PlayTimeRange.Over60) && g.PlayTime > 60));
            }

            if (query.CafeId.HasValue && query.ExcludeInInventory)
            {
                var inInventoryIds = _context.CafeGameInventories
                    .AsNoTracking()
                    .Where(i => i.CafeId == query.CafeId.Value && i.IsActive)
                    .Select(i => i.GameTemplateId);

                baseQuery = baseQuery.Where(g => !inInventoryIds.Contains(g.Id));
            }

            return baseQuery;
        }

        public async Task<GameTemplate?> GetByIdWithComponentsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.GameTemplates
                .AsNoTracking()
                .Include(g => g.Components)
                .Include(g => g.Categories)
                    .ThenInclude(gc => gc.Category)
                .FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task<GameTemplate?> GetActiveByIdWithComponentsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.GameTemplates
                .AsNoTracking()
                .Include(g => g.Components)
                .Include(g => g.Categories)
                    .ThenInclude(gc => gc.Category)
                .FirstOrDefaultAsync(g => g.Id == id && g.IsActive);
        }

        public Task<GameTemplate?> GetByIdWithCategoriesForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
            _context.GameTemplates
                .Include(g => g.Categories)
                .FirstOrDefaultAsync(g => g.Id == id);

        public Task<GameTemplate?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
            _context.GameTemplates
                .FirstOrDefaultAsync(g => g.Id == id);

        public Task<GameTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            _context.GameTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == id);

        public Task<GameTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
            _context.GameTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Name.ToLower() == name.ToLower() && g.IsActive);

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
            _context.GameTemplates.AsNoTracking().AnyAsync(g => g.Id == id);

        public async Task<Dictionary<Guid, int>> GetComponentCountsByGameIdsAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken cancellationToken = default)
        {
            if (gameIds.Count == 0)
                return new Dictionary<Guid, int>();

            return await _context.GameComponentTemplates
                .AsNoTracking()
                .Where(c => gameIds.Contains(c.GameTemplateId))
                .GroupBy(c => c.GameTemplateId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _context.SaveChangesAsync();
        }

        public async Task<bool> CafeHasGameAsync(Guid cafeId, Guid gameTemplateId, CancellationToken cancellationToken = default)
        {
            return await _context.CafeGameInventories
                .AnyAsync(c => c.CafeId == cafeId && c.GameTemplateId == gameTemplateId && c.BoxQuantity > 0);
        }

        /// <summary>
        /// Top N board game được chơi nhiều nhất trong hệ thống.
        /// Đếm theo số lần xuất hiện trong <see cref="ActiveSession.GameTemplateId"/> (primary game)
        /// cộng với số lần xuất hiện trong <see cref="ActiveSessionGame.GameTemplateId"/> (additional games).
        /// Chỉ tính các session đã thực sự bắt đầu (StartedAt != default).
        /// Kết quả chỉ gồm game đang hoạt động (IsActive = true); nếu game trong top bị deactive,
        /// tự động fill tiếp từ các game active kế tiếp để đảm bảo trả đủ <paramref name="topCount"/> record khi hệ thống đủ dữ liệu.
        /// </summary>
        /// <param name="topCount">Số lượng game tối đa cần lấy (phải &gt; 0).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<List<TopBoardGameDto>> GetTopPlayedBoardGamesAsync(int topCount, CancellationToken cancellationToken = default)
        {
            if (topCount <= 0)
            {
                return new List<TopBoardGameDto>();
            }

            // 1) Đếm số lượt chơi từ ActiveSession.GameTemplateId (primary game của session).
            var primaryCounts = await _context.ActiveSessions
                .AsNoTracking()
                .Where(s => s.StartedAt != default(DateTime))
                .GroupBy(s => s.GameTemplateId)
                .Select(g => new { GameTemplateId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            // 2) Đếm số lượt chơi từ ActiveSessionGame.GameTemplateId (additional games trong session).
            // Navigation property không được Include nên dùng sub-query: lọc ActiveSessionId có StartedAt đã set.
            var startedSessionIds = _context.ActiveSessions
                .AsNoTracking()
                .Where(s => s.StartedAt != default(DateTime))
                .Select(s => s.Id);

            var additionalCounts = await _context.ActiveSessionGames
                .AsNoTracking()
                .Where(sg => startedSessionIds.Contains(sg.ActiveSessionId))
                .GroupBy(sg => sg.GameTemplateId)
                .Select(g => new { GameTemplateId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            // 3) Gộp 2 bảng đếm, lấy top 2N game có tổng lượt chơi cao nhất (reserve thêm để fill khi có game inactive).
            var reserveCount = Math.Max(topCount * 2, topCount + 5);
            var playCounts = primaryCounts
                .Concat(additionalCounts)
                .GroupBy(x => x.GameTemplateId)
                .Select(g => new { GameTemplateId = g.Key, Count = g.Sum(x => x.Count) })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.GameTemplateId)
                .Take(reserveCount)
                .ToList();

            if (playCounts.Count == 0)
            {
                return new List<TopBoardGameDto>();
            }

            // 4) Lọc chỉ giữ game IsActive = true.
            var allTopGameIds = playCounts.Select(x => x.GameTemplateId).ToList();
            var activeGameIds = await _context.GameTemplates
                .AsNoTracking()
                .Where(g => g.IsActive && allTopGameIds.Contains(g.Id))
                .Select(g => g.Id)
                .ToListAsync(cancellationToken);

            var activeIdSet = activeGameIds.ToHashSet();

            // Chỉ lấy tối đa topCount game còn active, theo thứ tự play count giảm dần.
            var topGameIds = playCounts
                .Where(x => activeIdSet.Contains(x.GameTemplateId))
                .Take(topCount)
                .Select(x => x.GameTemplateId)
                .ToList();

            if (topGameIds.Count == 0)
            {
                return new List<TopBoardGameDto>();
            }

            var playCountMap = playCounts.ToDictionary(x => x.GameTemplateId, x => x.Count);

            // 5) Lấy thông tin game active kèm categories.
            var games = await _context.GameTemplates
                .AsNoTracking()
                .Where(g => g.IsActive && topGameIds.Contains(g.Id))
                .Include(g => g.Categories)
                    .ThenInclude(gc => gc.Category)
                .ToListAsync(cancellationToken);

            // 6) Đếm component theo batch.
            var componentCounts = await GetComponentCountsByGameIdsAsync(topGameIds, cancellationToken);

            // 7) Map sang DTO theo đúng thứ tự top play count.
            var ordered = topGameIds
                .Select(id => games.FirstOrDefault(g => g.Id == id))
                .Where(g => g != null)
                .Cast<GameTemplate>()
                .ToList();

            return ordered.Select(g => new TopBoardGameDto
            {
                Id = g.Id,
                Name = g.Name,
                ThumbnailUrl = g.ThumbnailUrl,
                Description = g.Description,
                MinPlayers = g.MinPlayers,
                MaxPlayers = g.MaxPlayers,
                PlayTime = g.PlayTime,
                ComponentCount = componentCounts.GetValueOrDefault(g.Id),
                PlayCount = playCountMap.GetValueOrDefault(g.Id),
                Categories = GameCatalogMapper.MapCategories(g)
            }).ToList();
        }
    }
}
