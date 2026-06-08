using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Inventory
{
    public class UpdateCafeInventoryRequestDto
    {
        [Range(1, 1000)]
        public int? BoxQuantity { get; set; }

        public CafeGameInventoryStatus? Status { get; set; }

        public List<ComponentPenaltyRequestDto>? ComponentPenalties { get; set; }
    }
}
