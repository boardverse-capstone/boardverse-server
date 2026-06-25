using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Inventory
{
    public class ComponentPenaltyRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.ComponentIdRequired)]
        public Guid GameComponentTemplateId { get; set; }

        [Range(0, 999999999, ErrorMessage = ApiErrorMessages.Validation.PenaltyFeeRange)]
        public decimal PenaltyFee { get; set; }
    }
}
