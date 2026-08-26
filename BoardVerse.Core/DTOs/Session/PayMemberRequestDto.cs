using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session;

/// <summary>
/// Request để thanh toán cho các thành viên cụ thể trong session.
/// Split Bill (2026-08-25): Thêm validation MaxLength để chặn DoS.
/// </summary>
public class PayMemberRequestDto
{
    /// <summary>
    /// Danh sách ID của các thành viên cần thanh toán.
    /// </summary>
    [Required(ErrorMessage = "Danh sách thành viên không được rỗng.")]
    [MinLength(1, ErrorMessage = "Phải chọn ít nhất 1 thành viên.")]
    // Fix #10: Giới hạn số lượng memberIds để chặn DoS
    [MaxLength(20, ErrorMessage = "Tối đa 20 thành viên mỗi lần thanh toán.")]
    public List<Guid> MemberIds { get; set; } = [];

    /// <summary>
    /// Phương thức thanh toán: "CASH" hoặc "QR_CODE".
    /// </summary>
    [Required(ErrorMessage = "Phương thức thanh toán không được rỗng.")]
    [RegularExpression("^(CASH|QR_CODE)$", ErrorMessage = "PaymentMethod phải là 'CASH' hoặc 'QR_CODE'.")]
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// Ghi chú (optional).
    /// </summary>
    // Fix #10: Giới hạn độ dài notes
    [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string? Notes { get; set; }
}
