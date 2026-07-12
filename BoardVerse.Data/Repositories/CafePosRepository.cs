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
                    cs.CafeId == cafeId && cs.UserId == userId && cs.User.IsActive);
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

        public async Task<ActiveSession?> GetActiveSessionByIdAsync(Guid cafeId, Guid sessionId)
        {
            var sessionData = await _context.ActiveSessions
                .Where(s => s.Id == sessionId && s.CafeId == cafeId && s.Status != GroupSessionStatus.Paid)
                .Select(s => new
                {
                    s.Id,
                    s.CafeId,
                    s.CafeTableId,
                    s.CafeInventoryBoxId,
                    s.GameTemplateId,
                    s.HostId,
                    s.LobbyId,
                    s.Status,
                    s.StartedAt,
                    s.EndedAt,
                    s.CreatedAt
                })
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (sessionData == null) return null;

            var table = await _context.CafeTables.FindAsync(sessionData.CafeTableId);
            var box = await _context.CafeInventoryBoxes.FindAsync(sessionData.CafeInventoryBoxId);
            var gameTemplate = await _context.GameTemplates.FindAsync(sessionData.GameTemplateId);
            var host = await _context.Users.FindAsync(sessionData.HostId);
            var members = await _context.ActiveSessionMembers
                .Include(m => m.User)
                .Where(m => m.ActiveSessionId == sessionId)
                .AsNoTracking()
                .ToListAsync();

            return new ActiveSession
            {
                Id = sessionData.Id,
                CafeId = sessionData.CafeId,
                CafeTableId = sessionData.CafeTableId,
                CafeInventoryBoxId = sessionData.CafeInventoryBoxId,
                GameTemplateId = sessionData.GameTemplateId,
                HostId = sessionData.HostId,
                LobbyId = sessionData.LobbyId,
                Status = sessionData.Status,
                StartedAt = sessionData.StartedAt,
                EndedAt = sessionData.EndedAt,
                CreatedAt = sessionData.CreatedAt,
                CafeTable = table!,
                CafeInventoryBox = box!,
                GameTemplate = gameTemplate!,
                Host = host!,
                Members = members
            };
        }

        public async Task<ActiveSession?> GetActiveSessionByBoxIdAsync(Guid boxId) =>
            await _context.ActiveSessions
                .FirstOrDefaultAsync(s => s.CafeInventoryBoxId == boxId && s.Status != GroupSessionStatus.Paid);

        public async Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId)
        {
            var sessionQuery = _context.ActiveSessions
                .Where(s => s.CafeId == cafeId && s.Status != GroupSessionStatus.Paid);

            if (gameTemplateId.HasValue)
            {
                sessionQuery = sessionQuery.Where(s => s.GameTemplateId == gameTemplateId.Value);
            }

            var sessions = await sessionQuery
                .Select(s => new
                {
                    s.Id,
                    s.CafeId,
                    s.CafeTableId,
                    s.CafeInventoryBoxId,
                    s.GameTemplateId,
                    s.HostId,
                    s.LobbyId,
                    s.Status,
                    s.StartedAt,
                    s.EndedAt,
                    s.CreatedAt
                })
                .AsNoTracking()
                .ToListAsync();

            if (!sessions.Any())
            {
                return [];
            }

            // Get related data
            var tableIds = sessions.Select(s => s.CafeTableId).Distinct().ToList();
            var boxIds = sessions.Select(s => s.CafeInventoryBoxId).Distinct().ToList();
            var gameTemplateIds = sessions.Select(s => s.GameTemplateId).Distinct().ToList();
            var hostIds = sessions.Select(s => s.HostId).Distinct().ToList();
            var sessionIds = sessions.Select(s => s.Id).ToList();

            var tables = await _context.CafeTables
                .Where(t => tableIds.Contains(t.Id))
                .AsNoTracking()
                .ToDictionaryAsync(t => t.Id);

            var boxes = await _context.CafeInventoryBoxes
                .Where(b => boxIds.Contains(b.Id))
                .AsNoTracking()
                .ToDictionaryAsync(b => b.Id);

            var gameTemplates = await _context.GameTemplates
                .Where(g => gameTemplateIds.Contains(g.Id))
                .AsNoTracking()
                .ToDictionaryAsync(g => g.Id);

            var hosts = await _context.Users
                .Where(u => hostIds.Contains(u.Id))
                .AsNoTracking()
                .ToDictionaryAsync(u => u.Id);

            var members = await _context.ActiveSessionMembers
                .Include(m => m.User)
                .Where(m => sessionIds.Contains(m.ActiveSessionId))
                .AsNoTracking()
                .ToListAsync();

            var membersBySession = members.GroupBy(m => m.ActiveSessionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            return sessions.Select(s => new ActiveSession
            {
                Id = s.Id,
                CafeId = s.CafeId,
                CafeTableId = s.CafeTableId,
                CafeInventoryBoxId = s.CafeInventoryBoxId,
                GameTemplateId = s.GameTemplateId,
                HostId = s.HostId,
                LobbyId = s.LobbyId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                CafeTable = tables.GetValueOrDefault(s.CafeTableId) ?? null!,
                CafeInventoryBox = boxes.GetValueOrDefault(s.CafeInventoryBoxId) ?? null!,
                GameTemplate = gameTemplates.GetValueOrDefault(s.GameTemplateId) ?? null!,
                Host = hosts.GetValueOrDefault(s.HostId) ?? null!,
                Members = membersBySession.GetValueOrDefault(s.Id) ?? []
            }).ToList();
        }

        public Task AddSessionAsync(ActiveSession session)
        {
            _context.ActiveSessions.Add(session);
            return Task.CompletedTask;
        }

        public Task AddSessionMemberAsync(ActiveSessionMember member)
        {
            _context.ActiveSessionMembers.Add(member);
            return Task.CompletedTask;
        }

        /// <summary>
        /// BR-12: Auto-create ActiveSessionGame when starting session so SubmitComponentCheck
        /// has a valid target immediately when session enters CHECKING state.
        /// </summary>
        public Task AddSessionGameAsync(ActiveSessionGame sessionGame)
        {
            _context.ActiveSessionGames.Add(sessionGame);
            return Task.CompletedTask;
        }

        public Task AddComponentLossReportAsync(ComponentLossReport report)
        {
            _context.ComponentLossReports.Add(report);
            return Task.CompletedTask;
        }

        public Task UpdateDepositAsync(BookingDeposit deposit)
        {
            _context.BookingDeposits.Update(deposit);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<ActiveSessionGame>> GetSessionGamesAsync(Guid sessionId) =>
            await _context.ActiveSessionGames
                .Include(g => g.CafeInventoryBox)
                    .ThenInclude(b => b.CafeGameInventory)
                .Include(g => g.GameTemplate)
                    .ThenInclude(t => t.Components)
                .Where(g => g.ActiveSessionId == sessionId)
                .ToListAsync();

        /// <summary>
        /// BR-12: Kiểm tra tất cả game trong session đã được kiểm tra đủ linh kiện.
        /// Returns true only if ALL session games have CheckStatus != NotChecked.
        /// </summary>
        public async Task<bool> IsSessionFullyCheckedAsync(Guid sessionId)
        {
            var games = await _context.ActiveSessionGames
                .Where(g => g.ActiveSessionId == sessionId)
                .ToListAsync();

            // If no games attached, no checklist needed
            if (games.Count == 0)
                return true;

            // All games must have been checked (Verified or MissingComponents)
            return games.All(g => g.CheckStatus != ComponentCheckStatus.NotChecked);
        }

        public async Task<ActiveSessionGame?> GetActiveSessionGameByIdAsync(Guid sessionGameId) =>
            await _context.ActiveSessionGames
                .Include(g => g.CafeInventoryBox)
                    .ThenInclude(b => b.CafeGameInventory)
                .Include(g => g.GameTemplate)
                    .ThenInclude(t => t.Components)
                .Include(g => g.ActiveSession)
                .FirstOrDefaultAsync(g => g.Id == sessionGameId);

        public async Task<GameTemplate?> GetGameTemplateWithComponentsAsync(Guid gameTemplateId) =>
            await _context.GameTemplates
                .Include(t => t.Components)
                .FirstOrDefaultAsync(t => t.Id == gameTemplateId);

        public async Task<CafeGameComponentPenalty?> GetComponentPenaltyAsync(Guid cafeId, Guid gameTemplateId, Guid componentId) =>
            await _context.CafeGameComponentPenalties
                .Include(p => p.CafeGameInventory)
                .Include(p => p.GameComponentTemplate)
                .FirstOrDefaultAsync(p =>
                    p.CafeGameInventory.CafeId == cafeId &&
                    p.CafeGameInventory.GameTemplateId == gameTemplateId &&
                    p.GameComponentTemplateId == componentId);

        public Task SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
