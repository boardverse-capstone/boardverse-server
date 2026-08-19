using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Admin tặng/trừ BVC thủ công (compensation, penalty, manual refund).
/// Dùng cho: BR-RISK-06 (compensation khi nhầm user), support cases, fraud refund.
/// KHÔNG qua SePay — ghi thẳng ledger AdminCredit/AdminDebit.
/// </summary>
public class AdminAdjustBalanceRequestDto
{
    /// <summary>UserId của player được điều chỉnh ví.</summary>
    [Required]
    public Guid TargetUserId { get; set; }

    /// <summary>Số BVC (luôn dương, dấu quyết định bởi IsCredit).</summary>
    [Required]
    [Range(1, 10_000_000, ErrorMessage = "Số BVC phải trong khoảng 1 → 10.000.000.")]
    public long AmountBvc { get; set; }

    /// <summary>true = cộng BVC (AdminCredit), false = trừ BVC (AdminDebit).</summary>
    [Required]
    public bool IsCredit { get; set; }

    /// <summary>Lý do điều chỉnh — bắt buộc cho audit (BR-RISK-05).</summary>
    [Required]
    [StringLength(512, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Idempotency key do admin sinh (uuid v4). UNIQUE ở DB.</summary>
    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}
