using BoardVerse.Core.Entities;
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
}
