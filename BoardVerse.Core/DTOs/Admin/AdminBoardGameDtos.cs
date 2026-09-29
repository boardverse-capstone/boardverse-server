using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminBoardGameResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? SearchAliases { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? Description { get; set; }
        public int? BggId { get; set; }
        public double? Weight { get; set; }
        /// <summary>Nguồn gốc của Weight (BGG / Manual / Default). BR-AUDIT-WEIGHT-01.</summary>
        public WeightSource WeightSource { get; set; } = WeightSource.BGG;
        /// <summary>Số lần background job đã retry fetch từ BGG cho Weight. Reset về 0 khi set thành công.</summary>
        public int BggRetryCount { get; set; }
        /// <summary>Lần cuối retry. Null = chưa retry.</summary>
        public DateTime? LastBggRetryAt { get; set; }
        public bool IsActive { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AdminUpdateBoardGameRequestDto
    {
        [StringLength(100, MinimumLength = 1, ErrorMessage = ApiErrorMessages.Validation.NameMax100)]
        public string? Name { get; set; }

        [StringLength(500, ErrorMessage = "Tên gọi khác không được vượt quá 500 ký tự.")]
        public string? SearchAliases { get; set; }

        [StringLength(2000, ErrorMessage = ApiErrorMessages.Validation.DescriptionMax2000)]
        public string? Description { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "BGG ID phải là số nguyên dương.")]
        public int? BggId { get; set; }

        /// <summary>
        /// BGG Complexity Weight (1.0 → 5.0). Null = xoá weight hiện tại.
        /// Dùng để sửa tay sau khi import, hoặc set cho game không import từ BGG.
        /// </summary>
        [Range(1.0, 5.0, ErrorMessage = "Weight phải nằm trong khoảng 1.0 đến 5.0.")]
        public double? Weight { get; set; }

        [Range(1, 1000, ErrorMessage = "Số người chơi tối thiểu phải từ 1 đến 1000.")]
        public int? MinPlayers { get; set; }

        [Range(1, 1000, ErrorMessage = "Số người chơi tối đa phải từ 1 đến 1000.")]
        public int? MaxPlayers { get; set; }

        [Range(1, 9999, ErrorMessage = "Thời gian chơi phải từ 1 đến 9999 phút.")]
        public int? PlayTime { get; set; }

        public bool? IsActive { get; set; }
    }

    public class AdminUpdateThumbnailRequestDto
    {
        [Required(ErrorMessage = "URL ảnh thumbnail là bắt buộc.")]
        [Url(ErrorMessage = "URL ảnh thumbnail không hợp lệ.")]
        [StringLength(2000, ErrorMessage = "URL ảnh không được vượt quá 2000 ký tự.")]
        public string ThumbnailUrl { get; set; } = string.Empty;
    }
}
