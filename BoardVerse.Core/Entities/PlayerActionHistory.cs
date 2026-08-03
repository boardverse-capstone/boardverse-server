using System.Text.Json;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Audit log cho admin actions — theo BR-RISK-05.
/// Append-only, không bao giờ sửa/xóa.
/// </summary>
public class PlayerActionHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User bị tác động.</summary>
    public Guid UserId { get; set; }

    /// <summary>Loại action: AdminCredit, AdminDebit, AccountStatusChange, RiskScoreReset, Warning, Suspend, Ban, MultiAccountConfirmed.</summary>
    public AdminActionType ActionType { get; set; }

    /// <summary>Admin userId thực hiện, hoặc "system".</summary>
    public Guid ActionBy { get; set; }

    /// <summary>Lý do admin ghi (audit).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>JSON snapshot metadata: before/after values, signals, etc.</summary>
    public JsonDocument? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Nullable — dùng khi Status = Suspended.</summary>
    public DateTime? ExpiresAt { get; set; }
}
