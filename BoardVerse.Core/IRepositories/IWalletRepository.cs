using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;

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

    /// <summary>
    /// Lấy tất cả wallets (phân trang) cho admin dashboard.
    /// </summary>
    Task<(IReadOnlyList<Wallet> Items, int TotalCount)> GetAllWalletsPagedAsync(
        int page, int pageSize,
        string? searchTerm = null,
        AccountStatus? statusFilter = null,
        RiskLevel? riskLevelFilter = null);

    /// <summary>
    /// Lấy chi tiết wallet + thông tin user.
    /// </summary>
    Task<Wallet?> GetWalletWithUserAsync(Guid userId);
}
