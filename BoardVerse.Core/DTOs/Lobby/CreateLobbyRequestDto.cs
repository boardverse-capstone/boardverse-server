using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Lobby
{
    public class CreateLobbyRequestDto
    {
        [Required]
        public Guid GameTemplateId { get; set; }

        [Required]
        public DateTime ScheduledStartTime { get; set; }

        [Range(2, 20)]
        public int MaxMembers { get; set; }

        /// <summary>
        /// Số người tối thiểu để có thể Lock/Start. Mặc định 2.
        /// </summary>
        [Range(2, 20)]
        public int? MinPlayers { get; set; }

        [Range(0, 1440)]
        public int CancellationLeadTimeMinutes { get; set; } = 30;

        /// <summary>
        /// true = lobby riêng tư (chỉ join qua invite/share code, không hiện trong search).
        /// false = lobby công khai.
        /// </summary>
        public bool IsPrivate { get; set; } = false;

        /// <summary>Mô tả ngắn cho lobby (vd: "Catan 4 người, cần thêm 2"). Tối đa 1000 ký tự.</summary>
        [StringLength(1000)]
        public string? Description { get; set; }

        /// <summary>URL ảnh bìa lobby (optional).</summary>
        [StringLength(500)]
        public string? CoverImageUrl { get; set; }

        /// <summary>Cafe mục tiêu (optional). Nếu có, lobby bị giới hạn ở cafe này.</summary>
        public Guid? CafeId { get; set; }

        /// <summary>Booking đã CONFIRMED để xác nhận đặt chỗ trước (optional).</summary>
        public Guid? BookingId { get; set; }

        /// <summary>Latitude của quán (cache từ Cafe.Latitude).</summary>
        public double? Latitude { get; set; }

        /// <summary>Longitude của quán (cache từ Cafe.Longitude).</summary>
        public double? Longitude { get; set; }

        /// <summary>Số ghế (BR-07). Nếu có, members &lt;= SeatCount.</summary>
        [Range(1, 50)]
        public int? SeatCount { get; set; }
    }

    public class UpdateLobbyRequestDto
    {
        [Range(2, 20)]
        public int? MaxMembers { get; set; }

        [Range(2, 20)]
        public int? MinPlayers { get; set; }

        public DateTime? ScheduledStartTime { get; set; }

        public bool? IsPrivate { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(500)]
        public string? CoverImageUrl { get; set; }

        [Range(5, 1440)]
        public int? CancellationLeadTimeMinutes { get; set; }
    }

    public class CreateLobbyReportDto
    {
        [Required]
        [StringLength(30)]
        public string Category { get; set; } = "Other";

        [Required]
        [StringLength(1000, MinimumLength = 5)]
        public string Reason { get; set; } = string.Empty;
    }
}