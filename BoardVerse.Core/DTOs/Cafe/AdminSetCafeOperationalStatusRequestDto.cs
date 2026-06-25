using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class AdminSetCafeOperationalStatusRequestDto
    {
        /// <summary>DATA_BLANK, ACTIVE, INACTIVE, or BANNED.</summary>
        [Required(ErrorMessage = ApiErrorMessages.Validation.OperationalStatusRequired)]
        [StringLength(32, ErrorMessage = ApiErrorMessages.Validation.OperationalStatusMax32)]
        public string Status { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = ApiErrorMessages.Validation.OperationalStatusReasonMax500)]
        public string? Reason { get; set; }
    }
}
