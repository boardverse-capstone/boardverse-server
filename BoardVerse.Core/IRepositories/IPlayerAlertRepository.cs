using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// R-01 (BR-RISK-02): Repository cho PlayerAlert — admin xem + filter alerts.
/// </summary>
public interface IPlayerAlertRepository
{
    Task<PlayerAlert?> GetByIdAsync(Guid alertId);

    Task AddAsync(PlayerAlert alert);

    Task<PaginatedResponse<PlayerAlertDto>> GetPagedAsync(PlayerAlertQuery query);

    /// <summary>Auto-trigger: insert alert nếu có signal trùng với 1 user chưa có alert open cùng AlertType.</summary>
    Task<bool> ShouldCreateAutoAlertAsync(Guid userId, PlayerAlertType alertType, string? signalsKey, int cooldownHours);

    /// <summary>Đếm alerts Open/Critical (admin dashboard).</summary>
    Task<int> CountOpenCriticalAsync();

    /// <summary>Đóng alerts Open quá 30 ngày chưa acknowledge.</summary>
    Task<IReadOnlyList<PlayerAlert>> GetStaleAlertsForDismissalAsync(int maxAgeDays, int batchSize);
}

/// <summary>Query cho PlayerAlert list endpoint.</summary>
public class PlayerAlertQuery : PaginationParams
{
    public Guid? UserId { get; set; }
    public PlayerAlertType? AlertType { get; set; }
    public PlayerAlertSeverity? Severity { get; set; }
    public PlayerAlertStatus? Status { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}
