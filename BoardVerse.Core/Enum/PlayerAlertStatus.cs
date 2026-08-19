namespace BoardVerse.Core.Enum;

/// <summary>
/// R-01: Trạng thái xử lý PlayerAlert (BR-RISK-02).
/// </summary>
public enum PlayerAlertStatus
{
    /// <summary>Mới phát hiện, chờ admin xem.</summary>
    Open = 0,

    /// <summary>Admin đã xem.</summary>
    Acknowledged = 1,

    /// <summary>Admin đã xử lý (warn/suspend/ban/dismiss).</summary>
    Resolved = 2,

    /// <summary>Đóng do false positive (alert_expiry_cleanup job sau 30 ngày Ack mà không Resolved).</summary>
    Dismissed = 3
}
