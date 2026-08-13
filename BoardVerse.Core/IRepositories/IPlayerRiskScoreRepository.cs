using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// BR-RISK-01 + BR-RISK-11: Repository cho PlayerRiskScore (snapshot current) + RiskScoreHistory (audit)..
/// </summary>
public interface IPlayerRiskScoreRepository
{
    Task<PlayerRiskScore?> GetByUserIdAsync(Guid userId);

    Task UpsertAsync(PlayerRiskScore snapshot);

    Task AppendHistoryAsync(RiskScoreHistory history);

    /// <summary>Lấy tất cả users đang có Wallet → batch process.</summary>
    Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(int batchSize, int skip);

    /// <summary>Snapshot lịch sử riskScore trong khoảng ngày (cho chart trend).</summary>
    Task<IReadOnlyList<RiskScoreHistory>> GetHistoryByUserIdAndDateRangeAsync(Guid userId, DateOnly fromDate, DateOnly toDate);
}
