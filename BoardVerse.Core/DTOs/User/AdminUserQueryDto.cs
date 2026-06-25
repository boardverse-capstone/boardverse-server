using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.User
{
    public class AdminUserQueryDto
    {
        [StringLength(100, ErrorMessage = ApiErrorMessages.Validation.SearchMax100)]
        public string? Search { get; set; }

        [StringLength(32, ErrorMessage = ApiErrorMessages.Validation.RoleMax32)]
        public string? Role { get; set; }
        public bool? IsActive { get; set; }

        [StringLength(32, ErrorMessage = ApiErrorMessages.Validation.AccountStatusMax32)]
        public string? AccountStatus { get; set; }

        [Range(1, 100, ErrorMessage = ApiErrorMessages.Validation.PageRange1To100)]
        public int Page { get; set; } = 1;

        [Range(1, 100, ErrorMessage = ApiErrorMessages.Validation.PageSizeRange1To100)]
        public int PageSize { get; set; } = 10;
    }
}
