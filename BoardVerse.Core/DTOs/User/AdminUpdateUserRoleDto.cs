using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.User
{
    public class AdminUpdateUserRoleDto
    {
        [Required(ErrorMessage = "Role is required.")]
        public string Role { get; set; } = string.Empty;
    }
}