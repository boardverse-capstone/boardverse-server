using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
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

        public Task<PaginatedResponse<GameTemplate>> GetBoardGamesPagedAsync(GetMasterGamesQuery query) =>
            GetPagedInternalAsync(query, includeComponents: false);

        public Task<PaginatedResponse<GameTemplate>> GetPagedAsync(GetMasterGamesQuery query) =>
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
            var items = await baseQuery
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalItems / (double)query.PageSize);

            return new PaginatedResponse<GameTemplate>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = query.PageNumber,
                    PageSize = query.PageSize,
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

        public async Task<GameTemplate?> GetByIdWithComponentsAsync(Guid id)
        {
            return await _context.GameTemplates
                .AsNoTracking()
                .Include(g => g.Components)
                .Include(g => g.Categories)
                    .ThenInclude(gc => gc.Category)
                .FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task<GameTemplate?> GetActiveByIdWithComponentsAsync(Guid id)
        {
            return await _context.GameTemplates
                .AsNoTracking()
                .Include(g => g.Components)
                .Include(g => g.Categories)
                    .ThenInclude(gc => gc.Category)
                .FirstOrDefaultAsync(g => g.Id == id && g.IsActive);
        }

        public Task<GameTemplate?> GetByIdWithCategoriesForUpdateAsync(Guid id) =>
            _context.GameTemplates
                .Include(g => g.Categories)
                .FirstOrDefaultAsync(g => g.Id == id);

        public Task<GameTemplate?> GetByIdForUpdateAsync(Guid id) =>
            _context.GameTemplates
                .FirstOrDefaultAsync(g => g.Id == id);

        public Task<bool> ExistsAsync(Guid id) =>
            _context.GameTemplates.AsNoTracking().AnyAsync(g => g.Id == id);

        public async Task<Dictionary<Guid, int>> GetComponentCountsByGameIdsAsync(IReadOnlyCollection<Guid> gameIds)
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

        public Task SaveChangesAsync()
        {
            return _context.SaveChangesAsync();
        }
    }
}
