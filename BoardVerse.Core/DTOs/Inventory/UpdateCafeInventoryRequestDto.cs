using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Inventory
{
    public class UpdateCafeInventoryRequestDto
    {
        [Range(1, 1000, ErrorMessage = ApiErrorMessages.Validation.BoxQuantityRange)]
        public int? BoxQuantity { get; set; }

        public CafeGameInventoryStatus? Status { get; set; }

        public List<ComponentPenaltyRequestDto>? ComponentPenalties { get; set; }
    }
}
