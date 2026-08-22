using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <inheritdoc cref="IPlayerAlertRepository"/>
public class PlayerAlertRepository : IPlayerAlertRepository
{
    private readonly BoardVerseDbContext _db;

    public PlayerAlertRepository(BoardVerseDbContext db) => _db = db;

    public async Task<PlayerAlert?> GetByIdAsync(Guid alertId, CancellationToken cancellationToken = default) =>
        await _db.PlayerAlerts
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == alertId);

    public async Task AddAsync(PlayerAlert alert, CancellationToken cancellationToken = default)
    {
        await _db.PlayerAlerts.AddAsync(alert);
    }

    public async Task<PaginatedResponse<PlayerAlertDto>> GetPagedAsync(PlayerAlertQuery q, CancellationToken cancellationToken = default)
    {
        var query = _db.PlayerAlerts.AsNoTracking().AsQueryable();

        if (q.UserId.HasValue) query = query.Where(a => a.UserId == q.UserId.Value);
        if (q.AlertType.HasValue) query = query.Where(a => a.AlertType == q.AlertType.Value);
        if (q.Severity.HasValue) query = query.Where(a => a.Severity == q.Severity.Value);
        if (q.Status.HasValue) query = query.Where(a => a.Status == q.Status.Value);
        if (q.FromUtc.HasValue) query = query.Where(a => a.CreatedAt >= q.FromUtc.Value);
        if (q.ToUtc.HasValue) query = query.Where(a => a.CreatedAt <= q.ToUtc.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((q.PageNumber - 1) * q.PageSize)
            .Take(q.PageSize)
            .Select(a => new PlayerAlertDto
            {
                Id = a.Id,
                UserId = a.UserId,
                Username = a.User != null ? a.User.Username : null,
                AlertType = a.AlertType,
                Severity = a.Severity,
                Status = a.Status,
                Signals = a.Signals,
                RiskScoreSnapshot = a.RiskScoreSnapshot,
                AcknowledgedBy = a.AcknowledgedBy,
                AcknowledgedAt = a.AcknowledgedAt,
                ResolutionNote = a.ResolutionNote,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return new PaginatedResponse<PlayerAlertDto>
        {
            Data = items,
            Meta = new PaginationMeta
            {
                CurrentPage = q.PageNumber,
                PageSize = q.PageSize,
                TotalItems = total,
                TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)q.PageSize)
            }
        };
    }

    public async Task<bool> ShouldCreateAutoAlertAsync(Guid userId, PlayerAlertType alertType, string? signalsKey, int cooldownHours, CancellationToken cancellationToken = default)
    {
        var cooldownSince = DateTime.UtcNow.AddHours(-cooldownHours);
        var exists = await _db.PlayerAlerts
            .Where(a => a.UserId == userId
                && a.AlertType == alertType
                && a.Status == PlayerAlertStatus.Open
                && a.CreatedAt >= cooldownSince)
            .AnyAsync();
        return !exists;
    }

    public async Task<int> CountOpenCriticalAsync(CancellationToken cancellationToken = default) =>
        await _db.PlayerAlerts
            .Where(a => a.Status == PlayerAlertStatus.Open && a.Severity == PlayerAlertSeverity.Critical)
            .CountAsync();

    public async Task<IReadOnlyList<PlayerAlert>> GetStaleAlertsForDismissalAsync(int maxAgeDays, int batchSize, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
        return await _db.PlayerAlerts
            .Where(a => a.Status == PlayerAlertStatus.Open
                && a.CreatedAt <= cutoff
                && a.AcknowledgedAt == null)
            .OrderBy(a => a.CreatedAt)
            .Take(batchSize)
            .ToListAsync();
    }
}
