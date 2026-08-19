using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Request tạo đơn top-up BVC từ tiền thật (BR § II.2).
/// Validate server-side; BR-REFUND-05: chỉ tính BVC integer, không có bonus.
/// </summary>
public class TopUpRequestDto
{
    /// <summary>
    /// Số tiền VND khách thanh toán. Validate:
    ///  ≥ 10.000 VND (min top-up), chia hết cho 1.000 VND (bội số).
    /// BVC nhận = amountVnd / 1.000 (integer).
    /// </summary>
    [Required]
    [Range(10_000, 100_000_000, ErrorMessage = "Số tiền nạp tối thiểu 10.000 VND và tối đa 100.000.000 VND.")]
    public long AmountVnd { get; set; }

    /// <summary>
    /// Idempotency key do client sinh (uuid v4). UNIQUE ở DB.
    /// BR § XVII.1: double-tap confirm với cùng key → backend trả kết quả cũ.
    /// </summary>
    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}
