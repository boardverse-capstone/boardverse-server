namespace BoardVerse.Core.Enum;

/// <summary>
/// R-01: Loại alert cho PlayerAlertEntity (BR-RISK-02).
/// </summary>
public enum PlayerAlertType
{
    /// <summary>Tự động khi riskScore vượt ngưỡng 30/50/75.</summary>
    AutoThresholdCrossed = 0,

    /// <summary>Phát hiện multi-account (BR-RISK-08).</summary>
    MultiAccountDetected = 1,

    /// <summary>User khác report (SIG-10).</summary>
    ManualReport = 2,

    /// <summary>Admin flag trực tiếp.</summary>
    AdminFlagged = 3
}
