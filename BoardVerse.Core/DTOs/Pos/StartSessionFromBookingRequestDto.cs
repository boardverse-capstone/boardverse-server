using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// Request DTO cho Host-led check-in.
/// Nhân viên quét mã đặt chỗ (BookingCode) để kích hoạt phiên chơi cho cả nhóm.
/// MDC Happy Path Step 9: "Quét một lần mã định danh đặt chỗ trên ứng dụng của người chơi khởi tạo để thực hiện thủ tục vào quán cho cả nhóm"
/// </summary>
public class StartSessionFromBookingRequestDto
{
    /// <summary>
    /// Mã đặt chỗ (BookingCode) từ ứng dụng của Host.
    /// </summary>
    [Required(ErrorMessage = "Mã đặt chỗ là bắt buộc.")]
    [StringLength(20, MinimumLength = 4, ErrorMessage = "Mã đặt chỗ phải từ 4-20 ký tự.")]
    public string BookingCode { get; set; } = string.Empty;

    /// <summary>
    /// ID bàn mà nhân viên chỉ định.
    /// </summary>
    [Required(ErrorMessage = "ID bàn là bắt buộc.")]
    public Guid CafeTableId { get; set; }

    /// <summary>
    /// Mã vạch hộp game đầu tiên mà nhóm sẽ chơi.
    /// </summary>
    [Required(ErrorMessage = "Mã vạch game là bắt buộc.")]
    public string Barcode { get; set; } = string.Empty;
}
