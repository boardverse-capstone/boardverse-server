using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Inventory
{
    public class AddCafeInventoryRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.GameTemplateIdRequired)]
        public Guid GameTemplateId { get; set; }

        [Range(1, 1000, ErrorMessage = ApiErrorMessages.Validation.BoxQuantityRange)]
        public int BoxQuantity { get; set; } = 1;

        public CafeGameInventoryStatus Status { get; set; } = CafeGameInventoryStatus.Available;

        public List<ComponentPenaltyRequestDto>? ComponentPenalties { get; set; }
    }
}
