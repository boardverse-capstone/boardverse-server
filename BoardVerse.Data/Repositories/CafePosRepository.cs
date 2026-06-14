using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class CafePosRepository : ICafePosRepository
    {
        private readonly BoardVerseDbContext _context;

        public CafePosRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public async Task<bool> CanOperateCafeAsync(Guid cafeId, Guid userId, string userRole)
        {
            if (userRole == UserRole.Manager.ToString())
            {
                return await _context.Cafes.AnyAsync(c =>
                    c.Id == cafeId && c.ManagerId == userId && c.IsActive);
            }

            if (userRole == UserRole.CafeStaff.ToString())
            {
                return await _context.CafeStaffs.AnyAsync(cs =>
                    cs.CafeId == cafeId && cs.UserId == userId && cs.IsActive);
            }

            return false;
        }

        public async Task<IReadOnlyList<CafeTable>> GetActiveTablesAsync(Guid cafeId) =>
            await _context.CafeTables
                .AsNoTracking()
                .Where(t => t.CafeId == cafeId && t.IsActive)
                .ToListAsync();

        public async Task<CafeTable?> GetTableAsync(Guid cafeId, Guid tableId) =>
            await _context.CafeTables
                .FirstOrDefaultAsync(t => t.CafeId == cafeId && t.Id == tableId && t.IsActive);

        public async Task<CafeInventoryBox?> GetBoxByBarcodeAsync(Guid cafeId, string barcode) =>
            await _context.CafeInventoryBoxes
                .Include(b => b.CafeGameInventory)
                    .ThenInclude(i => i.GameTemplate)
                .FirstOrDefaultAsync(b =>
                    b.IsActive
                    && b.Barcode == barcode
                    && b.CafeGameInventory.IsActive
                    && b.CafeGameInventory.CafeId == cafeId);

        public async Task<IReadOnlyList<CafeInventoryBox>> GetBoxesAsync(Guid cafeId, Guid? gameTemplateId)
        {
            var query = _context.CafeInventoryBoxes
                .AsNoTracking()
                .Include(b => b.CafeGameInventory)
                    .ThenInclude(i => i.GameTemplate)
                .Where(b =>
                    b.IsActive
                    && b.CafeGameInventory.IsActive
                    && b.CafeGameInventory.CafeId == cafeId);

            if (gameTemplateId.HasValue)
            {
                query = query.Where(b => b.CafeGameInventory.GameTemplateId == gameTemplateId.Value);
            }

            return await query
                .OrderBy(b => b.CafeGameInventory.GameTemplate!.Name)
                .ThenBy(b => b.Barcode)
                .ToListAsync();
        }

        public async Task<ActiveSession?> GetActiveSessionByIdAsync(Guid cafeId, Guid sessionId) =>
            await _context.ActiveSessions
                .Include(s => s.CafeTable)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .FirstOrDefaultAsync(s =>
                    s.Id == sessionId
                    && s.CafeId == cafeId
                    && s.IsActive);

        public async Task<ActiveSession?> GetActiveSessionByBoxIdAsync(Guid boxId) =>
            await _context.ActiveSessions
                .FirstOrDefaultAsync(s => s.CafeInventoryBoxId == boxId && s.IsActive);

        public async Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId)
        {
            var query = _context.ActiveSessions
                .AsNoTracking()
                .Include(s => s.CafeTable)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .Where(s => s.CafeId == cafeId && s.IsActive);

            if (gameTemplateId.HasValue)
            {
                query = query.Where(s => s.GameTemplateId == gameTemplateId.Value);
            }

            return await query.ToListAsync();
        }

        public Task AddSessionAsync(ActiveSession session)
        {
            _context.ActiveSessions.Add(session);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
