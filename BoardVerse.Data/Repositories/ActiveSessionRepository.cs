using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class ActiveSessionRepository : IActiveSessionRepository
    {
        private readonly BoardVerseDbContext _db;

        public ActiveSessionRepository(BoardVerseDbContext db)
        {
            _db = db;
        }

        public async Task<ActiveSession?> GetByIdAsync(Guid sessionId)
        {
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.Games)
                    .ThenInclude(g => g.ComponentCheckResults)
                        .ThenInclude(c => c.GameComponentTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.Cafe)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .Include(s => s.Lobby)
                    .ThenInclude(l => l!.Reservation) // Phase 4 / EC-10: Reservation.ScheduledEndTime cho time-overrun warning.
                .FirstOrDefaultAsync(s => s.Id == sessionId);
        }

        public async Task<ActiveSession?> GetByIdWithMembersAsync(Guid sessionId)
        {
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.Cafe)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .FirstOrDefaultAsync(s => s.Id == sessionId);
        }

        /// <summary>
        /// BUGFIX (subagent audit #3): Index-based lookup theo OrderId.
        /// Thay thế cho GetAllUnpaidAsync() + FirstOrDefault scan trong PaymentService webhook.
        ///
        /// GAP-XX Fix (2026-08-15): SePay BankAPINotify strip non-alphanumeric khỏi
        /// transfer content → webhook OrderId = "BV3382750A787C4AEF" (mất dấu '-').
        /// DB lưu OrderId = "BV-3382750A787C4AEF" (có dấu '-'). Exact match fail.
        /// Fix: normalize cả 2 phía (strip '-', uppercase) trước khi so sánh.
        /// In-memory normalize thay vì EF.Functions vì:
        ///  - Không phụ thuộc provider (Npgsql/PostgreSQL hay SQLite test).
        ///  - OrderId không quá lớn → không cần DB-side function.
        ///  - Tránh EF.Functions.ILike regression Npgsql khác version.
        /// </summary>
        public async Task<ActiveSession?> GetByOrderIdAsync(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                return null;
            }

            var normalized = NormalizeOrderId(orderId);

            // Query normalized form so DB-side match chính xác sau khi strip.
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                .Include(s => s.Games)
                .Include(s => s.CafeTable)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .FirstOrDefaultAsync(s => s.OrderId != null
                    && s.OrderId.Replace("-", "").ToUpper() == normalized);
        }

        /// <summary>
        /// GAP-XX Fix: Normalize OrderId cho webhook lookup. Strip dấu '-' và uppercase
        /// để chấp nhận cả "BV-3382750A787C4AEF" (DB) lẫn "BV3382750A787C4AEF" (SePay webhook).
        /// </summary>
        private static string NormalizeOrderId(string orderId)
            => orderId.Replace("-", "").Trim().ToUpperInvariant();

        public async Task<ActiveSession?> GetByLobbyIdWithMembersAsync(Guid lobbyId)
        {
            if (lobbyId == Guid.Empty) return null;
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.GameTemplate)
                .FirstOrDefaultAsync(s => s.LobbyId == lobbyId);
        }

        public async Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId)
        {
            var query = _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .Where(s => s.CafeId == cafeId && s.Status != GroupSessionStatus.Paid);

            if (gameTemplateId.HasValue)
            {
                query = query.Where(s => s.GameTemplateId == gameTemplateId.Value);
            }

            return await query.ToListAsync();
        }

        public Task AddAsync(ActiveSession session)
        {
            _db.ActiveSessions.Add(session);
            return Task.CompletedTask;
        }

        public Task AddMemberAsync(ActiveSessionMember member)
        {
            _db.ActiveSessionMembers.Add(member);
            return Task.CompletedTask;
        }

        public Task UpdateMemberAsync(ActiveSessionMember member)
        {
            _db.ActiveSessionMembers.Update(member);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ActiveSession session)
        {
            _db.ActiveSessions.Update(session);
            return Task.CompletedTask;
        }

        public async Task<int> CountActiveSessionMembersAsync(Guid cafeId)
        {
            return await _db.ActiveSessionMembers
                .Where(m => m.ActiveSession!.CafeId == cafeId
                    && m.ActiveSession.Status != GroupSessionStatus.Paid
                    && m.Status != IndividualSessionStatus.Finished)
                .CountAsync();
        }

        public async Task<IReadOnlyDictionary<Guid, int>> CountActiveSessionMembersByCafesAsync(
            IReadOnlyCollection<Guid> cafeIds)
        {
            if (cafeIds == null || cafeIds.Count == 0)
            {
                return new Dictionary<Guid, int>();
            }

            // 1 query duy nhất: group by CafeId để đếm active members.
            var grouped = await _db.ActiveSessionMembers
                .Where(m => cafeIds.Contains(m.ActiveSession!.CafeId)
                    && m.ActiveSession.Status != GroupSessionStatus.Paid
                    && m.Status != IndividualSessionStatus.Finished)
                .GroupBy(m => m.ActiveSession!.CafeId)
                .Select(g => new { CafeId = g.Key, Count = g.Count() })
                .ToListAsync();

            // Khởi tạo 0 cho tất cả cafeIds để caller không phải check missing key.
            var result = cafeIds.ToDictionary(id => id, _ => 0);
            foreach (var row in grouped)
            {
                result[row.CafeId] = row.Count;
            }
            return (IReadOnlyDictionary<Guid, int>)result;
        }

        public async Task<ActiveSessionMember?> GetMemberByIdAsync(Guid memberId)
        {
            return await _db.ActiveSessionMembers
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == memberId);
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }

        public async Task<IDatabaseTransactionContext> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            return new EfTransactionContextAdapter(tx);
        }

        public async Task<IReadOnlyList<ActiveSession>> GetAllUnpaidAsync()
        {
            return await _db.ActiveSessions
                .Where(s => s.Status == GroupSessionStatus.Unpaid && !string.IsNullOrWhiteSpace(s.OrderId))
                .ToListAsync();
        }

        /// <summary>
        /// P0 Fix #2: Atomic status update to prevent race conditions.
        /// Updates only if current status matches expected, returns affected rows.
        /// </summary>
        public async Task<bool> TryUpdateStatusAsync(Guid sessionId, GroupSessionStatus expectedStatus, GroupSessionStatus newStatus)
        {
            var rowsAffected = await _db.ActiveSessions
                .Where(s => s.Id == sessionId && s.Status == expectedStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Status, newStatus)
                    .SetProperty(s => s.PaidAt, DateTime.UtcNow));

            return rowsAffected > 0;
        }

        public Task<ActiveSessionGame?> GetSessionGameByIdAsync(Guid sessionGameId)
        {
            return _db.ActiveSessionGames
                .Include(g => g.GameTemplate)
                .FirstOrDefaultAsync(g => g.Id == sessionGameId);
        }

        public Task UpdateSessionGameAsync(ActiveSessionGame sessionGame)
        {
            _db.ActiveSessionGames.Update(sessionGame);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Marks all members as checked out and closes any linked lobby.
        /// Called at checkout time (when session becomes UNPAID).
        /// Table/box release is handled separately in ReleaseSessionTableAndBoxAsync.
        /// Idempotent: safe to call multiple times.
        /// </summary>
        public async Task ReleaseMembersAndCloseLobbyAsync(Guid sessionId)
        {
            var now = DateTime.UtcNow;

            var session = await _db.ActiveSessions
                .Include(s => s.Members)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
            {
                return;
            }

            // 1. Mark all members as checked out
            foreach (var member in session.Members)
            {
                if (!member.IsCheckedOut)
                {
                    member.IsCheckedOut = true;
                    member.CheckedOutAt ??= now;
                }
            }

            // 2. Close any linked lobby
            var lobby = await _db.Lobbies
                .FirstOrDefaultAsync(l => l.ActiveSessionId == sessionId);
            if (lobby != null && lobby.Status != LobbyStatus.Closed)
            {
                lobby.Status = LobbyStatus.Closed;
                lobby.ClosedAt = now;
                lobby.UpdatedAt = now;
            }

            // Persist all changes in a single transaction.
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Releases the board game box and cafe table back to Available.
        /// Called at payment time (when session becomes PAID) and by auto-release job.
        /// Idempotent: safe to call multiple times.
        /// </summary>
        public async Task ReleaseSessionTableAndBoxAsync(Guid sessionId)
        {
            var now = DateTime.UtcNow;

            var session = await _db.ActiveSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
            {
                return;
            }

            // Release the board game box (if attached and still in use).
            // Only flip status when box is currently InUse for this session.
            // Preserve Lost/Maintenance/Retired/etc. — those are operational
            // signals, not session lifecycle states.
            if (session.CafeInventoryBoxId.HasValue)
            {
                var box = await _db.CafeInventoryBoxes
                    .FirstOrDefaultAsync(b => b.Id == session.CafeInventoryBoxId.Value);
                if (box != null && box.Status == CafeGameInventoryStatus.InUse)
                {
                    box.Status = CafeGameInventoryStatus.Available;
                    box.UpdatedAt = now;
                }
            }

            // Release the cafe table (if attached and still in use)
            if (session.CafeTableId.HasValue)
            {
                var table = await _db.CafeTables
                    .FirstOrDefaultAsync(t => t.Id == session.CafeTableId.Value && t.CafeId == session.CafeId);
                if (table != null && table.Status == CafeTableStatus.InUse)
                {
                    table.Status = CafeTableStatus.Available;
                    table.UpdatedAt = now;
                }
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// GAP-9 Fix: Returns sessions that are Paid but haven't had BVC captured yet.
        /// Sessions that failed capture during PaySessionAsync will be retried here.
        /// </summary>
        public async Task<IReadOnlyList<ActiveSession>> GetSessionsNeedingBvcCaptureRetryAsync(int batchSize)
        {
            return await _db.ActiveSessions
                .Where(s => s.Status == GroupSessionStatus.Paid
                            && s.LobbyId.HasValue
                            && s.PaidAt.HasValue)
                .OrderBy(s => s.PaidAt)
                .Take(batchSize)
                .ToListAsync();
        }

        public async Task<bool> IsUserSessionParticipantAsync(Guid sessionId, Guid userId)
        {
            // Host is always a participant. Member participants are recorded in
            // ActiveSessionMembers. Staff who performed the check-in is also a participant.
            return await _db.ActiveSessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId)
                .AnyAsync(s => s.HostId == userId
                    || _db.ActiveSessionMembers.Any(m => m.ActiveSessionId == sessionId && m.UserId == userId)
                    || _db.Bookings.Any(b => b.LobbyId == s.LobbyId && b.CheckedInByUserId == userId));
        }

        public async Task<IReadOnlyList<ActiveSession>> GetExpiredAsync(DateTime cutoff, CancellationToken ct = default)
        {
            // Lấy session Active (chưa Paid) mà ExtendedEndTime/ScheduledEndTime + 30p grace đã qua
            // Không dùng ScheduledEndTime từ ActiveSession vì Reservation lưu ExtendedEndTime
            // Join Reservation để lấy ExtendedEndTime
            return await _db.ActiveSessions
                .Where(s => s.Status == GroupSessionStatus.Active)
                .ToListAsync(ct);
        }
    }
}
