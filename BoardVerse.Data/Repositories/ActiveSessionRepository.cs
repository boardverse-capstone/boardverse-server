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

        public async Task<ActiveSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
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
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        }

        public async Task<ActiveSession?> GetByIdWithMembersAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.Cafe)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
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
        public async Task<ActiveSession?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
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
                        .ThenInclude(u => u.Profile)
                .Include(s => s.Games)
                .Include(s => s.CafeTable)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .FirstOrDefaultAsync(s => s.OrderId != null
                    && s.OrderId.Replace("-", "").ToUpper() == normalized, cancellationToken);
        }

        /// <summary>
        /// GAP-XX Fix: Normalize OrderId cho webhook lookup. Strip dấu '-' và uppercase
        /// để chấp nhận cả "BV-3382750A787C4AEF" (DB) lẫn "BV3382750A787C4AEF" (SePay webhook).
        /// </summary>
        private static string NormalizeOrderId(string orderId)
            => orderId.Replace("-", "").Trim().ToUpperInvariant();

        /// <summary>
        /// Split Bill (2026-08-25): Lookup ActiveSession qua MemberId — dùng cho webhook QR
        /// khi SePay trả về payload chỉ chứa MemberId (qua hoặc parse từ OrderId).
        /// Query sub-collection trước, sau đó load session cùng navigation đầy đủ
        /// (giống <see cref="GetByIdWithMembersAsync"/>).
        /// </summary>
        public async Task<ActiveSession?> GetByMemberIdWithSessionAsync(Guid memberId, CancellationToken cancellationToken = default)
        {
            if (memberId == Guid.Empty) return null;

            var sessionId = await _db.ActiveSessionMembers
                .Where(m => m.Id == memberId)
                .Select(m => (Guid?)m.ActiveSessionId)
                .FirstOrDefaultAsync(cancellationToken);

            if (sessionId == null) return null;

            return await GetByIdWithMembersAsync(sessionId.Value, cancellationToken);
        }

        public async Task<ActiveSession?> GetByLobbyIdWithMembersAsync(Guid lobbyId, CancellationToken cancellationToken = default)
        {
            if (lobbyId == Guid.Empty) return null;
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.GameTemplate)
                .FirstOrDefaultAsync(s => s.LobbyId == lobbyId, cancellationToken);
        }

        public async Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId, CancellationToken cancellationToken = default)
        {
            var query = _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .Where(s => s.CafeId == cafeId && s.Status != GroupSessionStatus.Paid && s.Status != GroupSessionStatus.Closed);

            if (gameTemplateId.HasValue)
            {
                query = query.Where(s => s.GameTemplateId == gameTemplateId.Value);
            }

            return await query.ToListAsync(cancellationToken);
        }

        /// <summary>
        /// GAP-R4-A12 Fix: Fetch sessions overlap [rangeStart, rangeEnd] trong 1 query.
        /// Filter overlap: StartedAt < rangeEnd AND (EndedAt IS NULL OR EndedAt > rangeStart).
        /// Caller (CafeBookingService.GetAvailabilityAsync) sẽ filter thêm in-memory theo slot.
        /// </summary>
        public async Task<List<ActiveSession>> GetActiveSessionsInRangeAsync(
            Guid cafeId, DateTime rangeStart, DateTime rangeEnd,
            CancellationToken cancellationToken = default)
        {
            return await _db.ActiveSessions
                .Include(s => s.CafeTable)
                .Where(s => s.CafeId == cafeId
                    && s.Status != GroupSessionStatus.Paid
                    && s.StartedAt < rangeEnd
                    && (!s.EndedAt.HasValue || s.EndedAt.Value > rangeStart))
                .ToListAsync(cancellationToken);
        }

        public Task AddAsync(ActiveSession session, CancellationToken cancellationToken = default)
        {
            _db.ActiveSessions.Add(session);
            return Task.CompletedTask;
        }

        public Task AddMemberAsync(ActiveSessionMember member, CancellationToken cancellationToken = default)
        {
            _db.ActiveSessionMembers.Add(member);
            return Task.CompletedTask;
        }

        public Task UpdateMemberAsync(ActiveSessionMember member, CancellationToken cancellationToken = default)
        {
            _db.ActiveSessionMembers.Update(member);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ActiveSession session, CancellationToken cancellationToken = default)
        {
            _db.ActiveSessions.Update(session);
            return Task.CompletedTask;
        }

        public async Task<int> CountActiveSessionMembersAsync(Guid cafeId, CancellationToken cancellationToken = default)
        {
            return await _db.ActiveSessionMembers
                .Where(m => m.ActiveSession!.CafeId == cafeId
                    && m.ActiveSession.Status != GroupSessionStatus.Paid
                    && m.Status != IndividualSessionStatus.Finished)
                .CountAsync(cancellationToken);
        }

        public async Task<IReadOnlyDictionary<Guid, int>> CountActiveSessionMembersByCafesAsync(
            IReadOnlyCollection<Guid> cafeIds,
            CancellationToken cancellationToken = default)
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
                .ToListAsync(cancellationToken);

            // Khởi tạo 0 cho tất cả cafeIds để caller không phải check missing key.
            var result = cafeIds.ToDictionary(id => id, _ => 0);
            foreach (var row in grouped)
            {
                result[row.CafeId] = row.Count;
            }
            return (IReadOnlyDictionary<Guid, int>)result;
        }

        public async Task<ActiveSessionMember?> GetMemberByIdAsync(Guid memberId, CancellationToken cancellationToken = default)
        {
            return await _db.ActiveSessionMembers
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<IDatabaseTransactionContext> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            return new EfTransactionContextAdapter(tx);
        }

        // GAP-3 Fix: Expose current transaction for ambient transaction pattern.
        public IDatabaseTransactionContext? GetCurrentTransaction()
        {
            var tx = _db.Database.CurrentTransaction;
            if (tx == null) return null;
            return new EfTransactionContextAdapter(tx);
        }

        public async Task<IReadOnlyList<ActiveSession>> GetAllUnpaidAsync(CancellationToken cancellationToken = default)
        {
            return await _db.ActiveSessions
                .Where(s => s.Status == GroupSessionStatus.Unpaid && !string.IsNullOrWhiteSpace(s.OrderId))
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// P0 Fix #2: Atomic status update to prevent race conditions.
        /// Updates only if current status matches expected, returns affected rows.
        /// </summary>
        public async Task<bool> TryUpdateStatusAsync(Guid sessionId, GroupSessionStatus expectedStatus, GroupSessionStatus newStatus, CancellationToken cancellationToken = default)
        {
            var rowsAffected = await _db.ActiveSessions
                .Where(s => s.Id == sessionId && s.Status == expectedStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Status, newStatus)
                    .SetProperty(s => s.PaidAt, DateTime.UtcNow), cancellationToken);

            return rowsAffected > 0;
        }

        public Task<ActiveSessionGame?> GetSessionGameByIdAsync(Guid sessionGameId, CancellationToken cancellationToken = default)
        {
            return _db.ActiveSessionGames
                .Include(g => g.GameTemplate)
                .FirstOrDefaultAsync(g => g.Id == sessionGameId, cancellationToken);
        }

        public Task UpdateSessionGameAsync(ActiveSessionGame sessionGame, CancellationToken cancellationToken = default)
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
        public async Task ReleaseMembersAndCloseLobbyAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var session = await _db.ActiveSessions
                .Include(s => s.Members)
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

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
                .FirstOrDefaultAsync(l => l.ActiveSessionId == sessionId, cancellationToken);
            if (lobby != null && lobby.Status != LobbyStatus.Closed)
            {
                lobby.Status = LobbyStatus.Closed;
                lobby.ClosedAt = now;
                lobby.UpdatedAt = now;
            }

            // Persist all changes in a single transaction.
            await _db.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Releases the board game box and cafe table back to Available.
        /// Called at payment time (when session becomes PAID) and by auto-release job.
        /// Idempotent: safe to call multiple times.
        /// </summary>
        public async Task ReleaseSessionTableAndBoxAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var session = await _db.ActiveSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

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
                    .FirstOrDefaultAsync(b => b.Id == session.CafeInventoryBoxId.Value, cancellationToken);
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
                    .FirstOrDefaultAsync(t => t.Id == session.CafeTableId.Value && t.CafeId == session.CafeId, cancellationToken);
                if (table != null && table.Status == CafeTableStatus.InUse)
                {
                    table.Status = CafeTableStatus.Available;
                    table.UpdatedAt = now;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// GAP-9 Fix: Returns sessions that are Paid but haven't had BVC captured yet.
        /// Sessions that failed capture during PaySessionAsync will be retried here.
        /// </summary>
        public async Task<IReadOnlyList<ActiveSession>> GetSessionsNeedingBvcCaptureRetryAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            return await _db.ActiveSessions
                .Where(s => s.Status == GroupSessionStatus.Paid
                            && s.LobbyId.HasValue
                            && s.PaidAt.HasValue)
                .OrderBy(s => s.PaidAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> IsUserSessionParticipantAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            // Host is always a participant. Member participants are recorded in
            // ActiveSessionMembers. Staff who performed the check-in is also a participant.
            return await _db.ActiveSessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId)
                .AnyAsync(s => s.HostId == userId
                    || _db.ActiveSessionMembers.Any(m => m.ActiveSessionId == sessionId && m.UserId == userId)
                    || _db.Bookings.Any(b => b.LobbyId == s.LobbyId && b.CheckedInByUserId == userId), cancellationToken);
        }

        // GAP-R3-08 Fix: chống multi-tenant SignalR leak — verify user là participant VÀ session thuộc cafe chỉ định.
        public async Task<bool> IsUserSessionParticipantInCafeAsync(Guid sessionId, Guid userId, Guid cafeId, CancellationToken cancellationToken = default)
        {
            return await _db.ActiveSessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId && s.CafeId == cafeId)
                .AnyAsync(s => s.HostId == userId
                    || _db.ActiveSessionMembers.Any(m => m.ActiveSessionId == sessionId && m.UserId == userId)
                    || _db.Bookings.Any(b => b.LobbyId == s.LobbyId && b.CheckedInByUserId == userId), cancellationToken);
        }

        public async Task<IReadOnlyList<ActiveSession>> GetExpiredAsync(DateTime cutoff, CancellationToken ct = default)
        {
            // GAP-1 Fix: Lấy session Active mà EndedAt (đã gia hạn) + 30p grace đã qua cutoff.
            // Nếu EndedAt null → session chưa kết thúc → kiểm tra StartedAt + grace.
            // Walk-in (không có Lobby) không dùng reservation → dùng EndedAt hoặc StartedAt + grace.
            // GAP-R2-29 Fix: Skip paused sessions — staff intentional pause phải được tôn trọng.
            return await _db.ActiveSessions
                .Where(s => s.Status == GroupSessionStatus.Active)
                .Where(s => !s.IsPaused)
                .Where(s =>
                    (s.EndedAt.HasValue && s.EndedAt.Value.AddMinutes(30) < cutoff) ||
                    (!s.EndedAt.HasValue && s.StartedAt.AddMinutes(30) < cutoff))
                .ToListAsync(ct);
        }

        /// <summary>
        /// BR-END-05: Lấy session Active mà đã quá deadline end + grace 30 phút.
        /// Deadline = COALESCE(Reservation.ExtendedEndTime, Reservation.ScheduledEndTime)
        /// (JOIN qua Lobby vì ActiveSession không có FK trực tiếp tới Reservation).
        ///
        /// GAP-R4-A4 Fix: Cluster-safe variant dùng cho background job.
        /// Dùng <c>FOR UPDATE SKIP LOCKED</c> trong transaction — nếu deploy cluster với 2+ instance,
        /// instance A lock session row, instance B skip → mỗi session chỉ release đúng 1 lần.
        /// Caller phải mở transaction trước khi gọi method này (Postgres chỉ giữ row lock khi tx còn sống).
        ///
        /// Session walk-in không có Reservation → bỏ qua (deadline dựa trên Reservation).
        /// </summary>
        public async Task<IReadOnlyList<ActiveSession>> GetExpiredForUpdateAsync(DateTime cutoff, CancellationToken ct = default)
        {
            // Postgres-specific SQL.
            // Logic: deadline = COALESCE(R.ExtendedEndTime, R.ScheduledEndTime).
            //        expired = deadline + INTERVAL '30 minutes' < cutoff.
            // Chỉ release session link với Reservation (có LobbyId); session walk-in
            // (LobbyId IS NULL) bỏ qua vì không có ScheduledEndTime để so sánh.
            var sql =
                "SELECT a.* FROM \"ActiveSessions\" AS a " +
                "INNER JOIN \"Lobbies\" AS l ON l.\"Id\" = a.\"LobbyId\" " +
                "INNER JOIN \"Reservations\" AS r ON r.\"LobbyId\" = l.\"Id\" " +
                "WHERE a.\"Status\" = {0} " +
                "AND a.\"IsPaused\" = false " +
                "AND COALESCE(r.\"ExtendedEndTime\", r.\"ScheduledEndTime\") + INTERVAL '30 minutes' < {1} " +
                "FOR UPDATE OF a SKIP LOCKED";
            return await _db.ActiveSessions
                .FromSqlRaw(sql, (int)GroupSessionStatus.Active, cutoff)
                .AsNoTracking()
                .ToListAsync(ct);
        }

        /// <summary>
        /// Tìm phiên chơi ACTIVE mà user đang tham gia (chỉ member chưa Finished/Left).
        /// GAP-9 + GAP-1 Fix: Filter member chưa Finished + LeftAt == null để tránh trả session Paid cũ.
        /// </summary>
        public async Task<ActiveSession?> GetByUserIdWithMembersAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.Cafe)
                // GAP-1 Fix: Filter member đang active — loại trừ Finished + SuspendedMutation + đã rời.
                // Tránh trả Paid session cũ khi player vừa thanh toán xong.
                .Where(s => s.Members.Any(m => m.UserId == userId
                    && m.Status != IndividualSessionStatus.Finished
                    && m.Status != IndividualSessionStatus.SuspendedMutation
                    && m.LeftAt == null))
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// GAP-8 + GAP-2 + GAP-7 Fix: Lấy lịch sử phiên đã chơi của user (bao gồm walk-in).
        /// Logic: member đã Finished, không phụ thuộc group session status (walk-in có thể không chuyển Paid).
        /// </summary>
        public async Task<IReadOnlyList<ActiveSession>> GetHistoryByUserIdAsync(
            Guid userId,
            int limit = 20,
            DateTime? beforePaidAt = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            CancellationToken cancellationToken = default)
        {
            var query = _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.Cafe)
                // GAP-2 Fix: Bỏ filter group Status == Paid — walk-in session có thể không chuyển Paid
                // nhưng member đã Finished vẫn phải hiển thị trong history.
                .Where(s => s.Members.Any(m => m.UserId == userId
                    && m.Status == IndividualSessionStatus.Finished));

            // GAP-7 Fix: Cursor pagination theo PaidAt (fallback StartedAt)
            if (beforePaidAt.HasValue)
            {
                query = query.Where(s => (s.PaidAt ?? s.StartedAt) < beforePaidAt.Value);
            }

            // GAP-8 Fix: Date range filter (UTC)
            if (fromDate.HasValue)
            {
                query = query.Where(s => (s.PaidAt ?? s.StartedAt) >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(s => (s.PaidAt ?? s.StartedAt) <= toDate.Value);
            }

            return await query
                .OrderByDescending(s => s.PaidAt ?? s.StartedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }
    }
}