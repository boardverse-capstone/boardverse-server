using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session
{
    public class AddGuestSlotRequestDto
    {
        /// <summary>
        /// GAP-17 Fix: DisplayName validation — tên hiển thị phải có ý nghĩa, không phải số thuần túy hoặc ký tự đặc biệt.
        /// </summary>
        [Required(ErrorMessage = "Tên hiển thị là bắt buộc.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên hiển thị phải từ 2-100 ký tự.")]
        public string DisplayName { get; set; } = string.Empty;
    }
}
