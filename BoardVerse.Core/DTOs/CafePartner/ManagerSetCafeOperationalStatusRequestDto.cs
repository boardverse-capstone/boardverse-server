using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.CafePartner
{
    /// <summary>
    /// PATCH body để Manager tự set trạng thái vận hành quán mình sở hữu.
    /// Cho phép 3 giá trị: <c>DATA_BLANK</c>, <c>ACTIVE</c>, <c>INACTIVE</c>.
    /// Trạng thái <c>BANNED</c> chỉ Admin mới có quyền đặt.
    /// </summary>
    public class ManagerSetCafeOperationalStatusRequestDto
    {
        /// <summary>DATA_BLANK, ACTIVE hoặc INACTIVE.</summary>
        [Required(ErrorMessage = ApiErrorMessages.Validation.OperationalStatusRequired)]
        [StringLength(32, ErrorMessage = ApiErrorMessages.Validation.OperationalStatusMax32)]
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Lý do chuyển trạng thái (tuỳ chọn).
        /// Khuyến nghị cung cấp khi chuyển sang <c>INACTIVE</c>.
        /// </summary>
        [StringLength(500, ErrorMessage = ApiErrorMessages.Validation.OperationalStatusReasonMax500)]
        public string? Reason { get; set; }
    }
}
