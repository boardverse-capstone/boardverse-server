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

    public async Task<Reservation?> GetByIdAsync(Guid reservationId, bool includeRelations = false)
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
        return await query.FirstOrDefaultAsync(r => r.Id == reservationId);
    }

    public async Task<Reservation?> GetByIdempotencyKeyAsync(string idempotencyKey)
    {
        return await _db.Reservations
            .Include(r => r.Lobby)
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey);
    }

    public Task<Reservation?> GetByReservationCodeAsync(string reservationCode)
    {
        return _db.Reservations.FirstOrDefaultAsync(r => r.ReservationCode == reservationCode);
    }

    public Task<Reservation?> GetByLobbyIdAsync(Guid lobbyId)
    {
        return _db.Reservations.FirstOrDefaultAsync(r => r.LobbyId == lobbyId);
    }

    public async Task<IReadOnlyList<Reservation>> GetByHostAndPlayDateAsync(Guid hostId, DateOnly playDate)
    {
        return await _db.Reservations
            .Where(r => r.HostId == hostId && r.PlayDate == playDate)
            .ToListAsync();
    }

    private static readonly HashSet<ReservationStatus> ActiveReservationStatuses = new()
    {
        ReservationStatus.Holding,
        ReservationStatus.Confirmed,
        ReservationStatus.CheckedIn
    };

    public async Task<IReadOnlyList<Reservation>> GetActiveByCafePlayDateSlotAsync(
        Guid cafeId, DateOnly playDate, TimeSlot timeSlot)
    {
        return await _db.Reservations
            .Where(r => r.CafeId == cafeId
                && r.PlayDate == playDate
                && r.TimeSlot == timeSlot
                && ActiveReservationStatuses.Contains(r.Status))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Reservation>> GetActiveByHostAsync(Guid hostId)
    {
        return await _db.Reservations
            .Where(r => r.HostId == hostId && ActiveReservationStatuses.Contains(r.Status))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Reservation>> GetJoinedByUserAsync(Guid userId)
    {
        return await _db.Reservations
            .Where(r => ActiveReservationStatuses.Contains(r.Status)
                && r.Lobby != null
                && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive))
            .ToListAsync();
    }

    /// <summary>
    /// GAP #23 fix: cluster-safe query — dùng FOR UPDATE SKIP LOCKED để nhiều
    /// instance ReservationDeadlineJob không pick trùng reservation.
    /// Caller phải wrap transaction trước khi gọi (BR §17.3).
    /// </summary>
    public async Task<IReadOnlyList<Reservation>> GetDueForDeadlineAsync(DateTime cutoff, int limit = 100)
    {
        return await _db.Reservations
            .FromSqlRaw(
                "SELECT * FROM \"Reservations\" " +
                "WHERE \"Status\" = {0} AND \"RecruitmentDeadline\" <= {1} " +
                "ORDER BY \"RecruitmentDeadline\" " +
                "LIMIT {2} " +
                "FOR UPDATE SKIP LOCKED",
                (int)ReservationStatus.Holding, cutoff, limit)
            .ToListAsync();
    }

    /// <summary>
    /// GAP #23 fix: cluster-safe — FOR UPDATE SKIP LOCKED + push filter xuống SQL.
    /// </summary>
    public async Task<IReadOnlyList<Reservation>> GetDueForCafeApprovalExpiryAsync(DateTime cutoff, int limit = 100)
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
            .ToListAsync();

        return pendingIds;
    }

    /// <summary>
    /// BR-21A.9: No-show khi scheduledStartTime + 30 phút grace ≤ cutoff mà reservation
    /// vẫn Confirmed (chưa check-in). scheduledStartTime lưu sẵn ở Reservation.ScheduledStartTime.
    /// Dùng LINQ để filter trong C# tránh hard-code SQL cho enum.
    /// </summary>
    public async Task<IReadOnlyList<Reservation>> GetDueForNoShowAsync(DateTime cutoff, int limit = 100)
    {
        var graceCutoff = cutoff.AddMinutes(-30);

        return await _db.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed
                && r.ScheduledStartTime <= graceCutoff)
            .OrderBy(r => r.ScheduledStartTime)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> CountHostActionsForPlayDateAsync(Guid hostId, DateOnly playDate)
    {
        return await _db.Reservations
            .Where(r => r.HostId == hostId && r.PlayDate == playDate)
            .CountAsync();
    }

    public async Task<IReadOnlyList<Reservation>> GetNoShowCandidatesAsync(DateTime cutoff, CancellationToken ct = default)
    {
        // BR-CHECKIN-02: ScheduledStartTime < cutoff AND Status = Confirmed
        return await _db.Reservations
            .Where(r => r.ScheduledStartTime < cutoff
                && r.Status == ReservationStatus.Confirmed)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetListAsync(
        Guid userId,
        bool hostedByMe,
        bool joinedByMe,
        List<ReservationStatus>? statuses,
        DateOnly? playDate,
        Guid? cafeId,
        int page,
        int pageSize)
    {
        var query = _db.Reservations
            .Include(r => r.Host).ThenInclude(u => u.Profile)
            .Include(r => r.Cafe)
            .Include(r => r.Game)
            .Include(r => r.Lobby)
            .AsQueryable();

        // Filter: host hoặc joined
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
            query = query.Where(r =>
                r.Lobby != null && r.Lobby.Members.Any(m => m.UserId == userId && m.IsActive));
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

        // Filter: playDate
        if (playDate.HasValue)
        {
            query = query.Where(r => r.PlayDate == playDate.Value);
        }

        // Filter: cafe
        if (cafeId.HasValue)
        {
            query = query.Where(r => r.CafeId == cafeId.Value);
        }

        // Count total
        var totalCount = await query.CountAsync();

        // Paginate
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

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
        int pageSize)
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
        var totalCount = await query.CountAsync();

        // Paginate
        var items = await query
            .OrderBy(r => r.Lobby!.CafeApprovalDeadline)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <summary>
    /// BR-NEW-11: Lấy 1 reservation pending cafe approval theo ID.
    /// </summary>
    public async Task<Reservation?> GetPendingCafeApprovalByIdAsync(Guid reservationId)
    {
        return await _db.Reservations
            .Include(r => r.Host).ThenInclude(u => u.Profile)
            .Include(r => r.Cafe)
            .Include(r => r.Game)
            .Include(r => r.Lobby)
            .Where(r => r.Id == reservationId && r.Lobby != null && r.Lobby.Status == LobbyStatus.PendingCafeApproval)
            .FirstOrDefaultAsync();
    }

    public Task AddAsync(Reservation reservation)
    {
        _db.Reservations.Add(reservation);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Reservation reservation)
    {
        reservation.UpdatedAt = DateTime.UtcNow;
        _db.Reservations.Update(reservation);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
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
        int pageSize)
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

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}