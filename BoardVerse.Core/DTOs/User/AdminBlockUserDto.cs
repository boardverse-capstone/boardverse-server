using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.User
{
    public class AdminBlockUserDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.BlockReasonRequired)]
        [StringLength(500, ErrorMessage = ApiErrorMessages.Validation.BlockReasonMax500)]
        public string Reason { get; set; } = string.Empty;
    }
}
