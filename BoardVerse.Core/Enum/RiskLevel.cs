namespace BoardVerse.Core.Enum;

/// <summary>
/// Mức rủi ro player (BR-RISK-01 + § 16.3).
/// User chỉ thấy được <see cref="Level"/>, không thấy <see cref="int"/> RiskScore (BR-RISK-09).
/// </summary>
public enum RiskLevel
{
    /// <summary>0-29 — bình thường.</summary>
    Low = 0,

    /// <summary>30-49 — cảnh báo UI nhẹ.</summary>
    Medium = 1,

    /// <summary>50-74 — cọc ×1.5, ghi audit.</summary>
    High = 2,

    /// <summary>75-100 — cọc ×2, hạn chế tạo lobby, yêu cầu admin review.</summary>
    Critical = 3
}
