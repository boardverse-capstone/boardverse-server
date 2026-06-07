using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
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

        public async Task<PaginatedResponse<GameTemplate>> GetPagedAsync(GetMasterGamesQuery query)
        {
            var baseQuery = _context.GameTemplates
                .AsNoTracking()
                .Include(g => g.Components)
                .AsQueryable();

            // Apply search filter if provided
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                baseQuery = baseQuery.Where(g => 
                    EF.Functions.ILike(g.Name, $"%{query.SearchTerm}%"));
            }

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
    }
}
