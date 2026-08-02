using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho ví BVC của player.
/// Auto-create wallet bởi service; repository không tự sinh row mới.
/// </summary>
public interface IWalletRepository
{
    Task<Wallet?> GetByUserIdAsync(Guid userId);
    Task<Wallet?> GetByUserIdForUpdateAsync(Guid userId);
    Task AddAsync(Wallet wallet);
    Task UpdateAsync(Wallet wallet);
    Task SaveChangesAsync();
}
