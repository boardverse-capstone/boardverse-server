using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class ReservationRepository : IReservationRepository
{
    private readonly BoardVerseDbContext _db;

    public ReservationRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<Reservation?> GetByIdAsync(Guid reservationId, bool includeRelations = false, CancellationToken cancellationToken = default)
    {
        var query = _db.Reservations.AsQueryable();
        if (includeRelations)
        {
            query = query
                .Include(r => r.Host)
                .Include(r => r.Cafe)
                .Include(r => r.Game)
                .Include(r => r.Lobby)
                .Include(r => r.SeatInventory)
                .Include(r => r.GameInventory);
        }
        return await query.FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken);
    }

    public async Task<Reservation?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await _db.Reservations
            .Include(r => r.Lobby)
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public Task<Reservation?> GetByReservationCodeAsync(string reservationCode, CancellationToken cancellationToken = default)
    {
        return _db.Reservations.FirstOrDefaultAsync(r => r.ReservationCode == reservationCode, cancellationToken);
    }

    public Task<Reservation?> GetByLobbyIdAsync(Guid lobbyId, CancellationToken cancellationToken = default)
    {
        return _db.Reservations.FirstOrDefaultAsync(r => r.LobbyId == lobbyId, cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> GetByHostAndPlayDateAsync(Guid hostId, DateOnly playDate, CancellationToken cancellationToken = default)
    {
        return await _db.Reservations
            .Where(r => r.HostId == hostId && r.PlayDate == playDate)
            .ToListAsync(cancellationToken);
    }

    private static readonly HashSet<ReservationStatus> ActiveReservationStatuses = new()
    {
        ReservationStatus.Holding,
        ReservationStatus.Confirmed,
        ReservationStatus.CheckedIn
    };

    /// <summary>
    /// GAP-01 fix (2026-08-21): Đếm reservation active cho cafe + playDate + khung giờ cụ thể.
    /// BR-NEW-15: Dùng PreferredStartTime/PreferredEndTime thay vì TimeSlot enum.
    /// </summary>
    public async Task<IReadOnlyList<Reservation>> GetActiveByCafePlayDateSlotAsync(
        Guid cafeId, DateOnly playDate, TimeOnly preferredStartTime, TimeOnly preferredEndTime,
        CancellationToken cancellationToken = default)
    {
        return await _db.Reservations
            .Where(r => r.CafeId == cafeId
                && r.PlayDate == playDate
                && r.PreferredStartTime == preferredStartTime
                && r.PreferredEndTime == preferredEndTime
                && ActiveReservationStatuses.Contains(r.Status))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// GAP-01 fix (2026-08-21): Lấy tất cả reservation active cho cafe + playDate.
    /// Dùng cho CafeService dashboard - không cần filter theo slot cố định.
    /// </summary>
    public async Task<IReadOnlyList<Reservation>> GetActiveByCafePlayDateAsync(Guid cafeId, DateOnly playDate, CancellationToken cancellationToken = default)
    {
        return await _db.Reservations
            .Where(r => r.CafeId == cafeId
                && r.PlayDate == playDate
                && ActiveReservationStatuses.Contains(r.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> GetActiveByHostAsync(Guid hostId, CancellationToken cancellationToken = default)
    {
        return await _db.Reservations
            .Where(r => r.HostId == hostId && ActiveReservationStatuses.Contains(r.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> GetJoinedByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.Reservations
            .Where(r => ActiveReservationStatuses.Contains(r.Status)
                && r.Lobby != null
                && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> GetOverlappingReservationsAsync(
        Guid cafeId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        // Reservation overlap với [startTime, endTime] khi:
        //   reservation.ScheduledStartTime < endTime
        //   && reservation.ScheduledEndTime > startTime
        // (logic giống GetOverlappingBookingsAsync của BookingRepository).
        return await _db.Reservations
            .AsNoTracking()
            .Where(r =>
                r.CafeId == cafeId
                && r.ScheduledStartTime < endTime
                && r.ScheduledEndTime > startTime)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// GAP #23 fix: cluster-safe query — dùng FOR UPDATE SKIP LOCKED để nhiều
    /// instance ReservationDeadlineJob không pick trùng reservation.
    /// Caller phải wrap transaction trước khi gọi (BR §17.3).
    /// </summary>
    public async Task<IReadOnlyList<Reservation>> GetDueForDeadlineAsync(DateTime cutoff, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _db.Reservations
            .FromSqlRaw(
                "SELECT * FROM \"Reservations\" " +
                "WHERE \"Status\" = {0} AND \"RecruitmentDeadline\" <= {1} " +
                "ORDER BY \"RecruitmentDeadline\" " +
                "LIMIT {2} " +
                "FOR UPDATE SKIP LOCKED",
                (int)ReservationStatus.Holding, cutoff, limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// GAP #23 fix: cluster-safe — FOR UPDATE SKIP LOCKED + push filter xuống SQL.
    /// </summary>
    public async Task<IReadOnlyList<Reservation>> GetDueForCafeApprovalExpiryAsync(DateTime cutoff, int limit = 100, CancellationToken cancellationToken = default)
    {
        // BR-NEW-11: lobby PendingCafeApproval quá 24 giờ → expiredByCafe.
        // SKIP LOCKED trên join: Postgres lock row Reservation, lookup Lobby sau.
        var pendingIds = await _db.Reservations
            .FromSqlRaw(
                "SELECT * FROM \"Reservations\" " +
                "WHERE \"Status\" = {0} AND \"LobbyId\" IS NOT NULL " +
                "AND EXISTS (SELECT 1 FROM \"Lobbies\" l WHERE l.\"Id\" = \"Reservations\".\"LobbyId\" " +
                "            AND l.\"Status\" = {1} " +
                "            AND l.\"CafeApprovalDeadline\" IS NOT NULL " +
                "            AND l.\"CafeApprovalDeadline\" <= {2}) " +
                "LIMIT {3} " +
                "FOR UPDATE SKIP LOCKED",
                (int)ReservationStatus.Holding,
                LobbyStatus.PendingCafeApproval.ToString(),
                cutoff,
                limit)
            .ToListAsync(cancellationToken);

        return pendingIds;
    }

    /// <summary>
    /// BR-21A.9: No-show khi scheduledStartTime + 30 phút grace ≤ cutoff mà reservation
    /// vẫn Confirmed (chưa check-in). scheduledStartTime lưu sẵn ở Reservation.ScheduledStartTime.
    /// Dùng LINQ để filter trong C# tránh hard-code SQL cho enum.
    /// </summary>
    public async Task<IReadOnlyList<Reservation>> GetDueForNoShowAsync(DateTime cutoff, int limit = 100, CancellationToken cancellationToken = default)
    {
        var graceCutoff = cutoff.AddMinutes(-30);

        return await _db.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed
                && r.ScheduledStartTime <= graceCutoff)
            .OrderBy(r => r.ScheduledStartTime)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountHostActionsForPlayDateAsync(Guid hostId, DateOnly playDate, CancellationToken cancellationToken = default)
    {
        return await _db.Reservations
            .Where(r => r.HostId == hostId && r.PlayDate == playDate)
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> GetNoShowCandidatesAsync(DateTime cutoff, CancellationToken ct = default)
    {
        // BR-CHECKIN-02: ScheduledStartTime < cutoff AND Status = Confirmed
        // AND CHƯA check-in: không có ActiveSession liên kết qua LobbyId.
        // Lý do: nếu đã có ActiveSession (status=Active/Checking) thì khách ĐÃ check-in rồi
        // → KHÔNG đánh no-show. ActiveSession sẽ do AutoReleaseExpiredSessionsJob xử lý.
        // Walk-in reservation (LobbyId IS NULL) → không có ActiveSession, vẫn áp dụng no-show.
        return await _db.Reservations
            .Where(r => r.ScheduledStartTime < cutoff
                && r.Status == ReservationStatus.Confirmed
                && !_db.ActiveSessions.Any(s =>
                    s.LobbyId != null
                    && r.LobbyId != null
                    && s.LobbyId == r.LobbyId))
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetListAsync(
        Guid userId,
        bool hostedByMe,
        bool joinedByMe,
        List<ReservationStatus>? statuses,
        DateOnly? playDate,
        DateOnly? fromDate,
        DateOnly? toDate,
        Guid? cafeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Reservations
            .Include(r => r.Host).ThenInclude(u => u.Profile)
            .Include(r => r.Cafe)
            .Include(r => r.Game)
            .Include(r => r.Lobby)
            .AsQueryable();

        // Filter: host hoặc joined
        // Semantics:
        //   hostedByMe=true, joinedByMe=false  → chỉ reservation do user host.
        //   hostedByMe=false, joinedByMe=true  → reservation user tham gia VỚI VAI TRÒ MEMBER
        //                                        (exclude self-hosted để tránh leak: user tự host
        //                                         cũng có LobbyMember.IsActive=true cho chính mình).
        //   hostedByMe=true, joinedByMe=true   → cả 2.
        //   hostedByMe=false, joinedByMe=false  → fallback cả 2.
        if (hostedByMe && joinedByMe)
        {
            query = query.Where(r =>
                r.HostId == userId ||
                (r.Lobby != null && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive)));
        }
        else if (hostedByMe)
        {
            query = query.Where(r => r.HostId == userId);
        }
        else if (joinedByMe)
        {
            // Member-only: phải có member IsActive=true VÀ KHÔNG phải do user host.
            query = query.Where(r =>
                r.HostId != userId
                && r.Lobby != null
                && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive));
        }
        else
        {
            // Default: host or joined
            query = query.Where(r =>
                r.HostId == userId ||
                (r.Lobby != null && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive)));
        }

        // Filter: statuses
        if (statuses != null && statuses.Count > 0)
        {
            query = query.Where(r => statuses.Contains(r.Status));
        }

        // Filter: playDate (1 ngày cụ thể — backward compat cho endpoint /reservations)
        if (playDate.HasValue)
        {
            query = query.Where(r => r.PlayDate == playDate.Value);
        }

        // Filter: fromDate/toDate (inclusive) — cho /reservations/my lịch sử.
        // Defensive swap: nếu caller pass fromDate > toDate, swap lại để tránh query rỗng.
        // Service GetMyReservationsAsync cũng swap trước khi gọi, nhưng safeguard ở repo
        // giúp các caller khác (vd. admin tools) không bị bug im lặng.
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(r => r.PlayDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(r => r.PlayDate <= toDate.Value);
        }

        // Filter: cafe
        if (cafeId.HasValue)
        {
            query = query.Where(r => r.CafeId == cafeId.Value);
        }

        // Count total
        var totalCount = await query.CountAsync(cancellationToken);

        // Paginate
        // Lịch sử user: ưu tiên reservation gần nhất (PlayDate desc, ScheduledStartTime desc).
        // Endpoint /reservations/my đã dùng sort này; /reservations (legacy) cũng OK vì cùng semantic.
        var items = await query
            .OrderByDescending(r => r.PlayDate)
            .ThenByDescending(r => r.ScheduledStartTime)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <summary>
    /// BR-NEW-11: lấy lobby pending cafe approval cho manager.
    /// Filter theo danh sách CafeId mà user quản lý.
    /// </summary>
    public async Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetPendingCafeApprovalAsync(
        List<Guid> cafeIds,
        Guid? cafeId,
        DateOnly? playDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (cafeIds.Count == 0)
        {
            return ([], 0);
        }

        var query = _db.Reservations
            .Include(r => r.Host).ThenInclude(u => u.Profile)
            .Include(r => r.Cafe)
            .Include(r => r.Game)
            .Include(r => r.Lobby)
            .Where(r => r.Lobby != null && r.Lobby.Status == LobbyStatus.PendingCafeApproval)
            .AsQueryable();

        // Filter theo cafe của manager
        query = query.Where(r => cafeIds.Contains(r.CafeId));

        // Filter theo cafe cụ thể nếu có
        if (cafeId.HasValue)
        {
            query = query.Where(r => r.CafeId == cafeId.Value);
        }

        // Filter theo ngày
        if (playDate.HasValue)
        {
            query = query.Where(r => r.PlayDate == playDate.Value);
        }

        // Count total
        var totalCount = await query.CountAsync(cancellationToken);

        // Paginate
        var items = await query
            .OrderBy(r => r.Lobby!.CafeApprovalDeadline)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <summary>
    /// BR-NEW-11: Lấy 1 reservation pending cafe approval theo ID.
    /// </summary>
    public async Task<Reservation?> GetPendingCafeApprovalByIdAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        return await _db.Reservations
            .Include(r => r.Host).ThenInclude(u => u.Profile)
            .Include(r => r.Cafe)
            .Include(r => r.Game)
            .Include(r => r.Lobby)
            .Where(r => r.Id == reservationId && r.Lobby != null && r.Lobby.Status == LobbyStatus.PendingCafeApproval)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Tìm kiếm reservation theo tên game hoặc ngày tháng.
    /// BR-USER-LIMIT-01: user chỉ thấy reservation mình host hoặc có tham gia.
    /// </summary>
    public async Task<(IReadOnlyList<Reservation> Items, int TotalCount)> SearchAsync(
        Guid userId,
        string? gameName,
        DateOnly? fromDate,
        DateOnly? toDate,
        List<ReservationStatus>? statuses,
        Guid? cafeId,
        bool hostedByMe,
        bool joinedByMe,
        int page,
        int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.Reservations
            .Include(r => r.Host).ThenInclude(u => u.Profile)
            .Include(r => r.Cafe)
            .Include(r => r.Game)
            .Include(r => r.Lobby)
            .AsQueryable();

        // Filter: host hoặc joined (cùng semantics với GetListAsync — Gap-2 fix).
        if (hostedByMe && joinedByMe)
        {
            query = query.Where(r =>
                r.HostId == userId ||
                (r.Lobby != null && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive)));
        }
        else if (hostedByMe)
        {
            query = query.Where(r => r.HostId == userId);
        }
        else if (joinedByMe)
        {
            // Member-only: exclude self-hosted.
            query = query.Where(r =>
                r.HostId != userId
                && r.Lobby != null
                && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive));
        }
        else
        {
            // Default: host or joined
            query = query.Where(r =>
                r.HostId == userId ||
                (r.Lobby != null && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive)));
        }

        // Filter: game name (fuzzy search)
        if (!string.IsNullOrWhiteSpace(gameName))
        {
            var normalizedGameName = gameName.Trim().ToLowerInvariant();
            // Escape SQL wildcard characters to prevent injection
            var escapedGameName = normalizedGameName
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
            query = query.Where(r =>
                r.Game != null && EF.Functions.ILike(r.Game.Name, $"%{escapedGameName}%"));
        }

        // Filter: from date
        if (fromDate.HasValue)
        {
            query = query.Where(r => r.PlayDate >= fromDate.Value);
        }

        // Filter: to date
        if (toDate.HasValue)
        {
            query = query.Where(r => r.PlayDate <= toDate.Value);
        }

        // Filter: statuses
        if (statuses != null && statuses.Count > 0)
        {
            query = query.Where(r => statuses.Contains(r.Status));
        }

        // Filter: cafe
        if (cafeId.HasValue)
        {
            query = query.Where(r => r.CafeId == cafeId.Value);
        }

        // Count total
        var totalCount = await query.CountAsync(cancellationToken);

        // Paginate
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        _db.Reservations.Add(reservation);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Đếm số reservation do user host + user join (member-only) áp dụng cùng filter
    /// (statuses/cafeId/fromDate/toDate) cho summary count ở <c>GET /my</c>.
    /// </summary>
    /// <remarks>
    /// Member count EXCLUDE self-hosted (tránh double-count khi user vừa host vừa join cùng reservation).
    /// Defensive swap từDate/toDate nếu truyền sai thứ tự.
    /// </remarks>
    public async Task<(int HostedCount, int JoinedCount)> GetParticipationCountsAsync(
        Guid userId,
        List<ReservationStatus>? statuses,
        DateOnly? fromDate,
        DateOnly? toDate,
        Guid? cafeId,
        CancellationToken cancellationToken = default)
    {
        // Defensive swap (giống GetListAsync) — caller nên swap trước nhưng repo safeguard.
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        // Query Host-only: HostId == userId + filter.
        var hostedQuery = _db.Reservations.AsNoTracking()
            .Where(r => r.HostId == userId);

        // Query Member-only: NOT host + lobby member IsActive + filter.
        var joinedQuery = _db.Reservations.AsNoTracking()
            .Where(r =>
                r.HostId != userId
                && r.Lobby != null
                && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive));

        if (statuses != null && statuses.Count > 0)
        {
            hostedQuery = hostedQuery.Where(r => statuses.Contains(r.Status));
            joinedQuery = joinedQuery.Where(r => statuses.Contains(r.Status));
        }

        if (fromDate.HasValue)
        {
            hostedQuery = hostedQuery.Where(r => r.PlayDate >= fromDate.Value);
            joinedQuery = joinedQuery.Where(r => r.PlayDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            hostedQuery = hostedQuery.Where(r => r.PlayDate <= toDate.Value);
            joinedQuery = joinedQuery.Where(r => r.PlayDate <= toDate.Value);
        }

        if (cafeId.HasValue)
        {
            hostedQuery = hostedQuery.Where(r => r.CafeId == cafeId.Value);
            joinedQuery = joinedQuery.Where(r => r.CafeId == cafeId.Value);
        }

        var hostedCount = await hostedQuery.CountAsync(cancellationToken);
        var joinedCount = await joinedQuery.CountAsync(cancellationToken);

        return (hostedCount, joinedCount);
    }

    public Task UpdateAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        reservation.UpdatedAt = DateTime.UtcNow;

        // GAP-R6-RT-NEW: nếu instance đã được tracked (qua Include navigation trong
        // cùng DbContext — vd. LobbyRepository.GetByIdAsync include Reservation),
        // KHÔNG gọi _db.Reservations.Update() — sẽ throw "another instance with the same key
        // value is already being tracked". Đánh Modified trên entry hiện tại thay thế.
        var alreadyTracked = _db.Reservations.Local.Any(e => e.Id == reservation.Id);
        if (alreadyTracked)
        {
            _db.Entry(reservation).State = EntityState.Modified;
        }
        else
        {
            _db.Reservations.Update(reservation);
        }

        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Lấy danh sách reservation của 1 cafe cho Manager dashboard.
    /// Filter theo status và playDate, có phân trang.
    /// </summary>
    public async Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetByCafeAsync(
        Guid cafeId,
        List<ReservationStatus>? statuses,
        DateOnly? playDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Reservations
            .Include(r => r.Host).ThenInclude(u => u.Profile)
            .Include(r => r.Cafe)
            .Include(r => r.Game)
            .Include(r => r.Lobby)
            .Where(r => r.CafeId == cafeId);

        if (statuses != null && statuses.Count > 0)
        {
            query = query.Where(r => statuses.Contains(r.Status));
        }

        if (playDate.HasValue)
        {
            query = query.Where(r => r.PlayDate == playDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}