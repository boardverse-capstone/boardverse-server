namespace BoardVerse.Core.Enum;

/// <summary>
/// R-01: Mức độ nghiêm trọng của PlayerAlert (BR-RISK-02).
/// </summary>
public enum PlayerAlertSeverity
{
    /// <summary>Thông tin, không cần review ngay.</summary>
    Info = 0,

    /// <summary>Cần chú ý theo dõi.</summary>
    Warning = 1,

    /// <summary>Cần admin review trong 24h.</summary>
    Critical = 2
}
