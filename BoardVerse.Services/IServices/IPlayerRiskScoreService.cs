using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Services.IServices;

/// <summary>
/// BR-RISK-01 + BR-RISK-11: Service tính + lưu riskScore snapshot + history.
/// Gọi từ <c>risk_score_recompute</c> job mỗi giờ.
/// </summary>
public interface IPlayerRiskScoreService
{
    /// <summary>Tính riskScore (0-100) từ signals — pure calc, không side effect.</summary>
    int ComputeRiskScore(IReadOnlyDictionary<string, int> signals);

    RiskLevel ResolveRiskLevel(int riskScore);

    /// <summary>Recompute cho 1 user: read signals → compute → upsert snapshot + append history.</summary>
    Task<PlayerRiskScore?> RecomputeForUserAsync(Guid userId, DateTime now, CancellationToken ct = default);

    /// <summary>Tính cho 1 batch users (gọi từ job).</summary>
    Task<int> RecomputeBatchAsync(int batchSize, DateTime now, CancellationToken ct = default);

    /// <summary>Đọc snapshot hiện tại (admin view).</summary>
    Task<PlayerRiskScore?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Đọc history cho chart trend.</summary>
    Task<IReadOnlyList<RiskScoreHistory>> GetHistoryAsync(Guid userId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
