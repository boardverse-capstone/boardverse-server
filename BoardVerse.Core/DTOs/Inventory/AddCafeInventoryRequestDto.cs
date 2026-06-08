using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Inventory
{
    public class AddCafeInventoryRequestDto
    {
        [Required]
        public Guid GameTemplateId { get; set; }

        [Range(1, 1000)]
        public int BoxQuantity { get; set; } = 1;

        public CafeGameInventoryStatus Status { get; set; } = CafeGameInventoryStatus.Available;

        public List<ComponentPenaltyRequestDto>? ComponentPenalties { get; set; }
    }
}
