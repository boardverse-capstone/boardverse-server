using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// BR-RISK-01 + BR-RISK-11: Repository cho PlayerRiskScore (snapshot current) + RiskScoreHistory (audit)..
/// </summary>
public interface IPlayerRiskScoreRepository
{
    Task<PlayerRiskScore?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task UpsertAsync(PlayerRiskScore snapshot, CancellationToken cancellationToken = default);

    Task AppendHistoryAsync(RiskScoreHistory history, CancellationToken cancellationToken = default);

    /// <summary>Lấy tất cả users đang có Wallet → batch process.</summary>
    Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(int batchSize, int skip, CancellationToken cancellationToken = default);

    /// <summary>Snapshot lịch sử riskScore trong khoảng ngày (cho chart trend).</summary>
    Task<IReadOnlyList<RiskScoreHistory>> GetHistoryByUserIdAndDateRangeAsync(Guid userId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
