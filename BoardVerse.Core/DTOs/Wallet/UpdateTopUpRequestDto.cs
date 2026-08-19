using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Request đổi số tiền đơn top-up BVC đang Pending (chưa thanh toán).
/// Cùng validate rule với <see cref="TopUpRequestDto"/>:
///  ≥ 10.000 VND (min top-up), chia hết cho 1.000 VND (bội số).
/// IdempotencyKey mới — đơn cũ bị set <c>Cancelled</c>, đơn mới tạo thay thế.
/// </summary>
public class UpdateTopUpRequestDto
{
    /// <summary>
    /// Số tiền VND mới. Validate:
    ///  ≥ 10.000 VND (min top-up), chia hết cho 1.000 VND (bội số).
    /// BVC nhận = amountVnd / 1.000 (integer).
    /// </summary>
    [Required]
    [Range(10_000, 100_000_000, ErrorMessage = "Số tiền nạp tối thiểu 10.000 VND và tối đa 100.000.000 VND.")]
    public long AmountVnd { get; set; }

    /// <summary>
    /// Idempotency key mới do client sinh (uuid v4). Khác với key của đơn cũ.
    /// BR § XVII.1: double-tap confirm với cùng key → backend trả kết quả cũ.
    /// </summary>
    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}
