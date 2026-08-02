using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Ví BVC (BoardVerse Coin) của player.
/// Mỗi user có tối đa 1 wallet. Auto-create khi user lần đầu cần.
/// Theo BR § III và § XVI.5 (BR-RISK-04).
/// </summary>
public class Wallet
{
    public Guid UserId { get; set; }

    /// <summary>BVC có thể dùng để đặt cọc / thanh toán. Đơn vị: BVC (integer).</summary>
    public long AvailableBalance { get; set; }

    /// <summary>BVC đang bị giữ cho reservation/lobby. Đơn vị: BVC.</summary>
    public long HeldBalance { get; set; }

    /// <summary>Mirror tổng đang giữ — dùng cho BR-USER-LIMIT-03 (cap tổng cọc).</summary>
    public long TotalActiveDeposit { get; set; }

    /// <summary>Hệ số nhân cọc 1.0 → 2.0 theo BR-DEPOSIT-04 / BR-RISK-03.</summary>
    public decimal RiskMultiplier { get; set; } = 1.0m;

    /// <summary>Điểm rủi ro 0-100 (BR-RISK-01). User không được thấy (BR-RISK-09).</summary>
    public int RiskScore { get; set; } = 0;

    /// <summary>Mức rủi ro user-visible (BR-RISK-09).</summary>
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

    /// <summary>Cooling-off active: user không được tạo lobby có playDate &gt; 1 ngày (BR-NEW-10).</summary>
    public bool IsCoolingOff { get; set; }

    public DateTime? CoolingOffExpiresAt { get; set; }

    /// <summary>Trạng thái tài khoản tổng (BR-RISK-04).</summary>
    public AccountStatus AccountStatus { get; set; } = AccountStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User User { get; set; } = null!;
}
