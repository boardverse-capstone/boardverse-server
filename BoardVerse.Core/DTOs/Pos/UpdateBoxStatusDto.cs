using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos
{
 /// <summary>
 /// Request thay đổi trạng thái hộp game (CafeInventoryBox).
 /// PATCH /api/cafes/{cafeId}/pos/boxes/{boxId}/status
 /// </summary>
 public class UpdateBoxStatusRequestDto
 {
 /// <summary>
 /// Trạng thái mới muốn đổi sang. Hợp lệ:
 /// - Available: hộp đã được bổ sung/bổ sung linh kiện → sẵn sàng cho khách mới.
 /// - Maintenance: hộp cần kiểm tra/sửa chữa (không cho thuê).
 /// - Damaged: hộp hỏng nặng, không thể cho thuê.
 /// - Retired: hộp ngừng sử dụng vĩnh viễn.
 /// KHÔNG chấp nhận <c>InUse</c> (chỉ <c>EndGameSession</c> mới đổi sang InUse).
 /// </summary>
 [Required]
 public CafeGameInventoryStatus Status { get; set; }

 /// <summary>
 /// Lý do thay đổi (bắt buộc, tối đa 500 ký tự). Ghi audit log.
 /// VD: "Đã bổ sung 1 quân cờ đường bộ", "Thay hộp mới do hộp cũ hỏng nặng".
 /// </summary>
 [Required]
 [StringLength(500, MinimumLength = 3)]
 public string Reason { get; set; } = string.Empty;

 /// <summary>
 /// (Optional) ID linh kiện phục hồi để admin audit nhanh (chỉ áp dụng khi Status = Available
 /// và nhân viên vừa bổ sung 1 hoặc nhiều linh kiện).
 /// </summary>
 public Guid? RestoredComponentId { get; set; }

 /// <summary>
 /// (Optional) Số lượng linh kiện phục hồi, chỉ áp dụng kèm RestoredComponentId.
 /// </summary>
 [Range(0, int.MaxValue)]
 public int? RestoredQuantity { get; set; }
 }

 /// <summary>
 /// Response trả về sau khi đổi trạng thái hộp thành công.
 /// </summary>
 public class UpdateBoxStatusResponseDto
 {
 public Guid BoxId { get; set; }
 public string Barcode { get; set; } = string.Empty;
 public string GameName { get; set; } = string.Empty;
 public CafeGameInventoryStatus Status { get; set; }
 public string PreviousStatus { get; set; } = string.Empty;
 public DateTime UpdatedAt { get; set; }
 public Guid UpdatedByStaffId { get; set; }
 public string Reason { get; set; } = string.Empty;
 }
}