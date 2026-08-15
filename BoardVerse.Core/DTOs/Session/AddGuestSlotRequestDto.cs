using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BoardVerse.Core.DTOs.Session
{
    public class AddGuestSlotRequestDto
    {
        /// <summary>
        /// GAP-17 Fix: Tên hiển thị phải có ý nghĩa, không phải số thuần túy hoặc ký tự đặc biệt.
        /// Không [Required] ở đây vì service chấp nhận cả alias "username" — merge xong mới validate.
        /// </summary>
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên hiển thị phải từ 2-100 ký tự.")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Alias JSON "username" — chấp nhận cả hai key để không vỡ client cũ.
        /// Service sẽ merge vào DisplayName nếu DisplayName rỗng.
        /// </summary>
        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }
}
