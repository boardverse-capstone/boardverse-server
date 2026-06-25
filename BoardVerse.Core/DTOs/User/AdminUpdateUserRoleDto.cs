using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.User
{
    public class AdminUpdateUserRoleDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.RoleRequired)]
        public string Role { get; set; } = string.Empty;
    }
}
