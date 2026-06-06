using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.User
{
    public class AdminBlockUserDto
    {
        [Required(ErrorMessage = "Block reason is required.")]
        [StringLength(500, ErrorMessage = "Block reason cannot exceed 500 characters.")]
        public string Reason { get; set; } = string.Empty;
    }
}