using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session
{
    public class PartialCheckoutRequestDto
    {
        [Required]
        public List<Guid> MemberIds { get; set; } = new();
    }
}
