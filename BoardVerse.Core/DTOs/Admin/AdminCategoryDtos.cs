using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

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
        [Required(ErrorMessage = ApiErrorMessages.Validation.CategoryNameRequired)]
        [StringLength(100, MinimumLength = 2, ErrorMessage = ApiErrorMessages.Validation.CategoryNameLength)]
        public string Name { get; set; } = string.Empty;

        [StringLength(100, MinimumLength = 2, ErrorMessage = ApiErrorMessages.Validation.CategorySlugLength)]
        public string? Slug { get; set; }

        [StringLength(500, ErrorMessage = ApiErrorMessages.Validation.CategoryDescriptionMax500)]
        public string? Description { get; set; }

        [Range(0, 9999, ErrorMessage = ApiErrorMessages.Validation.SortOrderRange)]
        public int SortOrder { get; set; }
    }

    public class AdminUpdateCategoryRequestDto
    {
        [StringLength(100, MinimumLength = 2, ErrorMessage = ApiErrorMessages.Validation.CategoryNameLength)]
        public string? Name { get; set; }

        [StringLength(100, MinimumLength = 2, ErrorMessage = ApiErrorMessages.Validation.CategorySlugLength)]
        public string? Slug { get; set; }

        [StringLength(500, ErrorMessage = ApiErrorMessages.Validation.CategoryDescriptionMax500)]
        public string? Description { get; set; }

        [Range(0, 9999, ErrorMessage = ApiErrorMessages.Validation.SortOrderRange)]
        public int? SortOrder { get; set; }

        public bool? IsActive { get; set; }
    }
}
