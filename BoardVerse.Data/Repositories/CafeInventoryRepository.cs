using BoardVerse.Core.Common;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
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
                .Include(i => i.ComponentPenalties)
                    .ThenInclude(p => p.GameComponentTemplate)
                .FirstOrDefaultAsync(i => i.Id == inventoryId && i.IsActive);
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

        public async Task<PaginatedResponse<CafeGameInventory>> GetPagedByCafeAsync(
            Guid cafeId,
            PaginationParams paginationParams)
        {
            var baseQuery = _context.CafeGameInventories
                .AsNoTracking()
                .Include(i => i.GameTemplate)
                .Include(i => i.ComponentPenalties)
                    .ThenInclude(p => p.GameComponentTemplate)
                .Where(i => i.CafeId == cafeId && i.IsActive);

            var totalItems = await baseQuery.CountAsync();
            var items = await baseQuery
                .OrderByDescending(i => i.UpdatedAt)
                .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
                .Take(paginationParams.PageSize)
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalItems / (double)paginationParams.PageSize);

            return new PaginatedResponse<CafeGameInventory>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = paginationParams.PageNumber,
                    PageSize = paginationParams.PageSize,
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
    }
}
