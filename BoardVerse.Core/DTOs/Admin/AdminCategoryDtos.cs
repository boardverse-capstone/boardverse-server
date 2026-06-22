using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminCategoryResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AdminCreateCategoryRequestDto
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(100, MinimumLength = 2)]
        public string? Slug { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [Range(0, 9999)]
        public int SortOrder { get; set; }
    }

    public class AdminUpdateCategoryRequestDto
    {
        [StringLength(100, MinimumLength = 2)]
        public string? Name { get; set; }

        [StringLength(100, MinimumLength = 2)]
        public string? Slug { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [Range(0, 9999)]
        public int? SortOrder { get; set; }

        public bool? IsActive { get; set; }
    }
}
