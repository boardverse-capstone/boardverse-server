using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.CafePartner
{
    public class RejectCafePartnerApplicationRequestDto
    {
        [Required(ErrorMessage = "Rejection reason is required.")]
        [StringLength(1000, ErrorMessage = "Rejection reason cannot exceed 1000 characters.")]
        public string Reason { get; set; } = string.Empty;
    }
}
