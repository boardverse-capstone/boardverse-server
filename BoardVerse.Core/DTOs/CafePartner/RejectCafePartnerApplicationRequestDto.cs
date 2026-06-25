using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.CafePartner
{
    public class RejectCafePartnerApplicationRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.RejectionReasonRequired)]
        [StringLength(1000, ErrorMessage = ApiErrorMessages.Validation.RejectionReasonMax1000)]
        public string Reason { get; set; } = string.Empty;
    }
}
