using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Thông tin ví BVC của player (BR § III.1).
/// User chỉ thấy <see cref="AvailableBalance"/> mặc định (UI mobile);
/// field heldBalance chỉ trả khi client gửi <c>includeHeld=true</c> query
/// (BR-REFUND-05 + chống lộ thông tin tài chính).
/// </summary>
public class WalletDto
{
    public Guid UserId { get; set; }

    /// <summary>BVC có thể dùng để đặt cọc / thanh toán.</summary>
    public long AvailableBalance { get; set; }

    /// <summary>BVC đang bị giữ cho reservation/lobby. Chỉ trả khi includeHeld=true.</summary>
    public long? HeldBalance { get; set; }

    /// <summary>Risk multiplier 1.0..2.0 (để service tính cọc — BR-DEPOSIT-04, BR-RISK-03).</summary>
    public decimal RiskMultiplier { get; set; } = 1.0m;

    /// <summary>Mức rủi ro (low/medium/high/critical) — chỉ enum, không trả điểm (BR-RISK-09).</summary>
    public RiskLevel RiskLevel { get; set; }

    /// <summary>true khi user đang trong cooling-off (BR-NEW-10).</summary>
    public bool IsCoolingOff { get; set; }

    /// <summary>Trạng thái tài khoản (active/warning/restricted/suspended/banned) — BR-RISK-04.</summary>
    public AccountStatus AccountStatus { get; set; }
}
