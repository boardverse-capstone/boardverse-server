using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// Request DTO cho POS check-in (BR §21A.7).
/// Staff quét QR của Host tại quầy để kích hoạt phiên chơi cho cả nhóm.
///
/// <para>
/// <see cref="Code"/> là mã check-in tổng quát, hỗ trợ 2 format:
/// <list type="bullet">
///   <item>
///     <b>ReservationCode</b> (BR mới BVC) — 8 ký tự alphanumeric uppercase, exclude 0/1/I/O.
///     VD: <c>ABC234XY</c>.
///   </item>
///   <item>
///     <b>BookingCode</b> (legacy VND) — format <c>BV{N}</c> VD: <c>BV12345678</c>.
///   </item>
/// </list>
/// </para>
///
/// <see cref="ReservationCodeDetector"/> sẽ tự động phân biệt 2 format và route
/// về flow tương ứng (ReservationService.CheckInAsync hoặc BookingDeposit flow).
/// </summary>
public class CheckInRequestDto
{
    /// <summary>
    /// Mã check-in do host cung cấp (ReservationCode mới hoặc BookingCode legacy).
    /// </summary>
    [Required(ErrorMessage = "Mã check-in là bắt buộc.")]
    [StringLength(20, MinimumLength = 4, ErrorMessage = "Mã check-in phải từ 4-20 ký tự.")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// ID bàn mà nhân viên chỉ định cho nhóm.
    /// </summary>
    [Required(ErrorMessage = "ID bàn là bắt buộc.")]
    public Guid CafeTableId { get; set; }

    /// <summary>
    /// Mã vạch hộp game đầu tiên mà nhóm sẽ chơi.
    /// Reservation flow: validate hộp phải thuộc reservation.GameId (GAP #7).
    /// </summary>
    [Required(ErrorMessage = "Mã vạch game là bắt buộc.")]
    public string Barcode { get; set; } = string.Empty;

    /// <summary>
    /// GAP-1/GAP-37 Fix: IdempotencyKey chống double-tap khi nhân viên bấm nhiều lần.
    /// Client gửi cùng key → trả kết quả cũ, không tạo session trùng.
    /// </summary>
    [StringLength(100)]
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// GAP-1/GAP-37 Fix: Nonce chống replay attack — QR code bị chụp và scan lại.
    /// Mã một lần, được sinh phía client khi quét QR, server validate không trùng.
    /// </summary>
    [StringLength(64)]
    public string? Nonce { get; set; }
}
