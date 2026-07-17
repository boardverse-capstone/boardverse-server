using System.ComponentModel.DataAnnotations;
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
