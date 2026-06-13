using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Inventory;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Helpers;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class CafeInventoryRepository : ICafeInventoryRepository
    {
        private readonly BoardVerseDbContext _context;

        public CafeInventoryRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public async Task<CafeGameInventory?> GetByIdWithDetailsAsync(Guid inventoryId)
        {
            return await _context.CafeGameInventories
                .Include(i => i.GameTemplate)
                    .ThenInclude(g => g!.Categories)
                        .ThenInclude(gc => gc.Category)
                .Include(i => i.GameTemplate)
                    .ThenInclude(g => g!.Components)
                .Include(i => i.ComponentPenalties)
                    .ThenInclude(p => p.GameComponentTemplate)
                .FirstOrDefaultAsync(i => i.Id == inventoryId && i.IsActive);
        }

        public async Task<CafeGameInventory?> GetByIdWithDetailsIncludingInactiveAsync(Guid inventoryId)
        {
            return await _context.CafeGameInventories
                .Include(i => i.GameTemplate)
                    .ThenInclude(g => g!.Components)
                .Include(i => i.ComponentPenalties)
                    .ThenInclude(p => p.GameComponentTemplate)
                .FirstOrDefaultAsync(i => i.Id == inventoryId);
        }

        public async Task<CafeGameInventory?> GetByCafeAndGameTemplateAsync(Guid cafeId, Guid gameTemplateId)
        {
            return await _context.CafeGameInventories
                .Include(i => i.ComponentPenalties)
                .FirstOrDefaultAsync(i =>
                    i.CafeId == cafeId &&
                    i.GameTemplateId == gameTemplateId &&
                    i.IsActive);
        }

        public async Task<CafeGameInventory?> GetByCafeAndGameTemplateIncludingInactiveAsync(
            Guid cafeId,
            Guid gameTemplateId)
        {
            return await _context.CafeGameInventories
                .FirstOrDefaultAsync(i =>
                    i.CafeId == cafeId &&
                    i.GameTemplateId == gameTemplateId);
        }

        public async Task<HashSet<Guid>> GetActiveGameTemplateIdsByCafeAsync(Guid cafeId)
        {
            var ids = await _context.CafeGameInventories
                .AsNoTracking()
                .Where(i => i.CafeId == cafeId && i.IsActive)
                .Select(i => i.GameTemplateId)
                .ToListAsync();

            return ids.ToHashSet();
        }

        public async Task<PaginatedResponse<CafeGameInventory>> GetPagedByCafeAsync(
            Guid cafeId,
            GetCafeInventoryQuery query,
            bool deletedOnly = false)
        {
            var baseQuery = _context.CafeGameInventories
                .AsNoTracking()
                .Include(i => i.GameTemplate)
                    .ThenInclude(g => g!.Categories)
                        .ThenInclude(gc => gc.Category)
                .Include(i => i.GameTemplate)
                    .ThenInclude(g => g!.Components)
                .Include(i => i.ComponentPenalties)
                    .ThenInclude(p => p.GameComponentTemplate)
                .Where(i => i.CafeId == cafeId && i.IsActive != deletedOnly);

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var trimmed = query.SearchTerm.Trim();
                var searchKey = VietnameseTextNormalizer.ToSearchKey(trimmed);
                baseQuery = baseQuery.Where(i =>
                    EF.Functions.ILike(i.GameTemplate!.NameSearchKey, $"%{searchKey}%") ||
                    EF.Functions.ILike(i.GameTemplate!.Name, $"%{trimmed}%") ||
                    EF.Functions.ILike(i.GameTemplate!.SearchAliasesKey, $"%{searchKey}%"));
            }

            if (query.Status.HasValue)
            {
                baseQuery = baseQuery.Where(i => i.Status == query.Status.Value);
            }

            baseQuery = ApplySort(baseQuery, query);

            var totalItems = await baseQuery.CountAsync();
            var items = await baseQuery
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalItems / (double)query.PageSize);

            return new PaginatedResponse<CafeGameInventory>
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

        public Task AddAsync(CafeGameInventory inventory)
        {
            _context.CafeGameInventories.Add(inventory);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync() => _context.SaveChangesAsync();

        private static IQueryable<CafeGameInventory> ApplySort(
            IQueryable<CafeGameInventory> query,
            GetCafeInventoryQuery filter)
        {
            return filter.SortBy switch
            {
                InventorySortField.Name => filter.SortDescending
                    ? query.OrderByDescending(i => i.GameTemplate!.Name)
                    : query.OrderBy(i => i.GameTemplate!.Name),
                InventorySortField.BoxQuantity => filter.SortDescending
                    ? query.OrderByDescending(i => i.BoxQuantity)
                    : query.OrderBy(i => i.BoxQuantity),
                InventorySortField.Status => filter.SortDescending
                    ? query.OrderByDescending(i => i.Status)
                    : query.OrderBy(i => i.Status),
                _ => filter.SortDescending
                    ? query.OrderByDescending(i => i.UpdatedAt)
                    : query.OrderBy(i => i.UpdatedAt)
            };
        }
    }
}
