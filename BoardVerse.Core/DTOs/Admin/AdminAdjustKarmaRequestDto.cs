using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminAdjustKarmaRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        [Range(-100, 100, ErrorMessage = ApiErrorMessages.Validation.KarmaAdjustmentRange)]
        public int Amount { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.ReasonRequired)]
        [StringLength(1000, MinimumLength = 5, ErrorMessage = ApiErrorMessages.Validation.ReasonLength5To1000)]
        public string Reason { get; set; } = string.Empty;
    }
}
