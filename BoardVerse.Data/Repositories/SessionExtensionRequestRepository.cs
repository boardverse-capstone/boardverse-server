using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// GAP-1 Fix: Repository cho SessionExtensionRequest.
/// </summary>
public class SessionExtensionRequestRepository : ISessionExtensionRequestRepository
{
    private readonly BoardVerseDbContext _db;

    public SessionExtensionRequestRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<SessionExtensionRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.SessionExtensionRequests
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<SessionExtensionRequest?> GetByIdWithSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.SessionExtensionRequests
            .Include(r => r.Session)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<IReadOnlyList<SessionExtensionRequest>> GetPendingBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return await _db.SessionExtensionRequests
            .Where(r => r.SessionId == sessionId && r.Status == SessionExtensionRequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// GAP-9 Fix: Lấy tất cả extension request (mọi status) của 1 session.
    /// Dùng cho GetCurrentSessionAsync trả LastExtensionRequest cho player.
    /// </summary>
    public async Task<IReadOnlyList<SessionExtensionRequest>> GetAllBySessionIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await _db.SessionExtensionRequests
            .Where(r => r.SessionId == sessionId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SessionExtensionRequest>> GetPendingByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        return await _db.SessionExtensionRequests
            .Include(r => r.Session)
            .Include(r => r.RequestedByUser)
            .Where(r => r.Session!.CafeId == cafeId && r.Status == SessionExtensionRequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task AddAsync(SessionExtensionRequest request, CancellationToken cancellationToken = default)
    {
        _db.SessionExtensionRequests.Add(request);
    }

    public async Task UpdateAsync(SessionExtensionRequest request, CancellationToken cancellationToken = default)
    {
        _db.SessionExtensionRequests.Update(request);
    }

    public async Task<IReadOnlyList<SessionExtensionRequest>> GetAllPendingAsync(CancellationToken ct = default)
    {
        return await _db.SessionExtensionRequests
            .Where(r => r.Status == SessionExtensionRequestStatus.Pending)
            .ToListAsync(ct);
    }

    // GAP-13 Fix: Batched query — filter tại DB, limit rows
    public async Task<IReadOnlyList<SessionExtensionRequest>> GetExpiredRequestsBatchAsync(
        DateTime cutoff, int batchSize, CancellationToken ct = default)
    {
        return await _db.SessionExtensionRequests
            .Where(r => r.Status == SessionExtensionRequestStatus.Pending
                && r.CreatedAt < cutoff)
            .OrderBy(r => r.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    // GAP-R2-05 Fix: Atomic batch update với ExecuteUpdateAsync — WHERE Status=Pending AND CreatedAt < cutoff
    // Tránh race với staff approve/reject giữa chừng (last writer wins).
    public async Task<int> ExpireBatchAsync(DateTime cutoff, int batchSize, CancellationToken ct = default)
    {
        return await _db.SessionExtensionRequests
            .Where(r => r.Status == SessionExtensionRequestStatus.Pending
                && r.CreatedAt < cutoff)
            .OrderBy(r => r.CreatedAt)
            .Take(batchSize)
            .ExecuteUpdateAsync(
                u => u.SetProperty(r => r.Status, SessionExtensionRequestStatus.Expired)
                      .SetProperty(r => r.ProcessedAt, DateTime.UtcNow),
                ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
