using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Pos
{
    public class SubmitComponentCheckRequestDto
    {
        [Required]
        public Guid SessionGameId { get; set; }

        public List<ComponentCheckResultDto> Results { get; set; } = [];
    }

    public class ComponentCheckResultDto
    {
        [Required]
        public Guid ComponentId { get; set; }

        [Required]
        [Range(0, int.MaxValue)]
        public int ActualQuantity { get; set; }
    }
}
