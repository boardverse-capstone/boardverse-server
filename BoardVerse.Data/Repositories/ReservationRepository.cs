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

    public Task<Reservation?> GetByIdempotencyKeyAsync(string idempotencyKey)
    {
        return _db.Reservations.FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey);
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
                (int)LobbyStatus.PendingCafeApproval,
                cutoff,
                limit)
            .ToListAsync();

        return pendingIds;
    }

    /// <summary>
    /// GAP #23 fix: cluster-safe — FOR UPDATE SKIP LOCKED + push filter xuống SQL.
    /// </summary>
    public async Task<IReadOnlyList<Reservation>> GetDueForNoShowAsync(DateTime cutoff, int limit = 100)
    {
        // scheduledTime + 30 phút grace ≤ cutoff. Đẩy filter xuống SQL thay vì ToListAsync + in-memory filter.
        var graceCutoff = cutoff.AddMinutes(-30);
        return await _db.Reservations
            .FromSqlRaw(
                "SELECT * FROM \"Reservations\" " +
                "WHERE \"Status\" = {0} " +
                "AND \"ScheduledTime\" IS NOT NULL " +
                "AND \"ScheduledTime\" <= {1} " +
                "ORDER BY \"ScheduledTime\" " +
                "LIMIT {2} " +
                "FOR UPDATE SKIP LOCKED",
                (int)ReservationStatus.Confirmed, graceCutoff, limit)
            .ToListAsync();
    }

    public async Task<int> CountHostActionsForPlayDateAsync(Guid hostId, DateOnly playDate)
    {
        return await _db.Reservations
            .Where(r => r.HostId == hostId && r.PlayDate == playDate)
            .CountAsync();
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
}