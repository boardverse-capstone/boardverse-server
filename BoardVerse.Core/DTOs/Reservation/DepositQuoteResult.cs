using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Reservation;

/// <summary>
/// Khoảng cách từ now đến playDate — mapping sang BR-NEW-01 §VIII.
/// </summary>
public enum DistanceBucket
{
    SameDay = 0,
    OneDay = 1,
    TwoDays = 2,
    ThreeToFourDays = 3,
    FiveToSevenDays = 4,
    OutOfRange = 99
}

/// <summary>
/// Kết quả tính cọc (BR-DEPOSIT-02..04 + BR-NEW-01).
/// </summary>
public class DepositQuoteResult
{
    /// <summary>BVC deposit gốc (ratePerPerson × maxPlayers).</summary>
    public long BaseDeposit { get; set; }

    /// <summary>minDeposit theo khoảng cách playDate (BR-NEW-01).</summary>
    public long MinDepositApplied { get; set; }

    /// <summary>riskMultiplier từ wallet (BR-RISK-03, 1.0..2.0).</summary>
    public decimal RiskMultiplier { get; set; }

    /// <summary>
    /// finalDeposit = max(minDepositApplied, ratePerPerson × maxPlayers × riskMultiplier).
    /// BR-DEPOSIT-02.
    /// </summary>
    public long FinalDeposit { get; set; }

    /// <summary>
    /// Giá vé cơ bản của cafe (VND) — dùng để FE hiển thị breakdown
    /// "Tiền cọc = X% × {CafeBasePriceVnd:N0}đ = {FinalDeposit} BVC".
    /// </summary>
    public decimal CafeBasePriceVnd { get; set; }

    /// <summary>
    /// % cọc hiện tại (0.20 = 20%). Config trong code, không phụ thuộc BR-NEW-01 nữa.
    /// </summary>
    public decimal DepositPercentage { get; set; }

    /// <summary>Distance bucket từ playDate vs now.</summary>
    public DistanceBucket Distance { get; set; }

    /// <summary>maxPlayers đã được clamp theo distance.</summary>
    public int MaxPlayersApplied { get; set; }

    /// <summary>Buffer (recruitmentDeadline - now) tính bằng phút.</summary>
    public int BufferMinutes { get; set; }

    /// <summary>True khi buffer &lt; 120 nhưng ≥ 60 (cảnh báo BR-LOBBY-01c).</summary>
    public bool BufferWarning { get; set; }

    /// <summary>True khi cafe cần duyệt thủ công (BR-NEW-11, distance ≥ threshold).</summary>
    public bool RequiresCafeApproval { get; set; }
}

/// <summary>
/// Input cho EligibilityValidator (BR-USER-LIMIT-01..05, BR-NEW-02, BR-NEW-05, BR-NEW-08).
/// </summary>
public class EligibilityContext
{
    public Guid HostId { get; set; }
    public Guid CafeId { get; set; }
    public DateOnly PlayDate { get; set; }
    public TimeOnly PreferredStartTime { get; set; }
    public TimeOnly PreferredEndTime { get; set; }
    public DateTime RecruitmentDeadline { get; set; }
    public DateTime Now { get; set; }

    public bool IsVip { get; set; }
    public bool IsRiskMultiplierHigh { get; set; }
    public bool IsCoolingOff { get; set; }
    public long WalletHeldBalance { get; set; }
    public long FinalDeposit { get; set; }
}