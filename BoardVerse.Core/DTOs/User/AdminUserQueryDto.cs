using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.User
{
    public class AdminUserQueryDto
    {
        [StringLength(100, ErrorMessage = "Search cannot exceed 100 characters.")]
        public string? Search { get; set; }

        [StringLength(32, ErrorMessage = "Role cannot exceed 32 characters.")]
        public string? Role { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsBlocked { get; set; }

        [Range(1, 100, ErrorMessage = "Page must be between 1 and 100.")]
        public int Page { get; set; } = 1;

        [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
        public int PageSize { get; set; } = 10;
    }
}
