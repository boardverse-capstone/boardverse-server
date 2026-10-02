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

        /// <summary>
        /// Số điện thoại khách vô danh (optional).
        /// Validate format VN (10-11 chữ số, đầu 03/05/07/08/09) ở service.
        /// </summary>
        [StringLength(20, ErrorMessage = "Số điện thoại không được dài quá 20 ký tự.")]
        public string? PhoneNumber { get; set; }

        /// <summary>
        /// BR-13 (revised 2026-10-01) Permanent fix:
        /// Staff đánh dấu khách vô danh này là "khách đầu nhóm" (primary customer / host của lobby walk-in).
        /// Khi true:
        ///   - Service set <c>ActiveSessionMember.IsHost = true</c> cho member vừa thêm.
        ///   - <c>MapSession.ResolveHostDisplayName</c> sẽ hiển thị <c>DisplayName</c> thay vì "Khách vãng lai".
        ///   - Lobby.HostUserId giữ nguyên = staff (FK an toàn cho 30+ chỗ đang đọc HostUserId);
        ///     audit field <c>ActiveSession.StartedByStaffId</c> vẫn trỏ về staff thật.
        /// Chỉ cho phép khi chưa có primary customer nào trong session (mỗi session 1 host duy nhất).
        /// </summary>
        public bool DesignateAsHost { get; set; } = false;
    }
}
