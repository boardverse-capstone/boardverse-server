using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class WalletRepository : IWalletRepository
{
    private readonly BoardVerseDbContext _db;

    public WalletRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<Wallet?> GetByUserIdAsync(Guid userId)
    {
        return _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
    }

    /// <summary>
    /// Lấy wallet với khóa hàng (SELECT ... FOR UPDATE) — dùng trong transaction
    /// atomic trừ/cộng BVC cho deposit hold/capture (BR § 17.3).
    /// </summary>
    public Task<Wallet?> GetByUserIdForUpdateAsync(Guid userId)
    {
        return _db.Wallets.FromSqlRaw(
            "SELECT * FROM \"Wallets\" WHERE \"UserId\" = {0} FOR UPDATE", userId)
            .FirstOrDefaultAsync();
    }

    public Task AddAsync(Wallet wallet)
    {
        _db.Wallets.Add(wallet);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Wallet wallet)
    {
        wallet.UpdatedAt = DateTime.UtcNow;
        _db.Wallets.Update(wallet);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }

    public async Task<(IReadOnlyList<Wallet> Items, int TotalCount)> GetAllWalletsPagedAsync(
        int page,
        int pageSize,
        string? searchTerm = null,
        AccountStatus? statusFilter = null,
        RiskLevel? riskLevelFilter = null)
    {
        var query = _db.Wallets
            .Include(w => w.User)
            .AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(w => w.AccountStatus == statusFilter.Value);
        }

        if (riskLevelFilter.HasValue)
        {
            query = query.Where(w => w.RiskLevel == riskLevelFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLowerInvariant();
            query = query.Where(w =>
                w.UserId.ToString().ToLower().Contains(term) ||
                (w.User != null && w.User.Email.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(w => w.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Wallet?> GetWalletWithUserAsync(Guid userId)
    {
        return await _db.Wallets
            .Include(w => w.User)
            .FirstOrDefaultAsync(w => w.UserId == userId);
    }

    /// <summary>
    /// BR-NEW-10 §XI.2 — Lấy wallet đang trong cooling-off (cho background job expire).
    /// Chỉ load những wallet đã quá hạn deactivation (CoolingOffExpiresAt &lt; now).
    /// </summary>
    public async Task<IReadOnlyList<Wallet>> GetActiveCoolingOffWalletsPagedAsync(int batchSize)
    {
        var now = DateTime.UtcNow;
        return await _db.Wallets
            .Where(w => w.IsCoolingOff && w.CoolingOffExpiresAt != null && w.CoolingOffExpiresAt <= now)
            .OrderBy(w => w.CoolingOffExpiresAt)
            .Take(batchSize)
            .ToListAsync();
    }

    /// <summary>
    /// BR-NEW-10 §XI.1 — Lấy wallet còn active (AccountStatus = Active, không bị suspended/banned)
    /// để background job quét detect signals.
    /// </summary>
    public async Task<IReadOnlyList<Wallet>> GetActiveWalletsPagedAsync(int batchSize)
    {
        return await _db.Wallets
            .Where(w => w.AccountStatus == AccountStatus.Active || w.AccountStatus == AccountStatus.Warning)
            .OrderBy(w => w.UpdatedAt)
            .Take(batchSize)
            .ToListAsync();
    }
}
