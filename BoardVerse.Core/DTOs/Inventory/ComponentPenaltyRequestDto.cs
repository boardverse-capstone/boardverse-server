using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Inventory
{
    public class ComponentPenaltyRequestDto
    {
        [Required]
        public Guid GameComponentTemplateId { get; set; }

        [Range(0, 999999999)]
        public decimal PenaltyFee { get; set; }
    }
}
