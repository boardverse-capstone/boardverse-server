using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session
{
    public class StartSessionRequestDto
    {
        [Required]
        public Guid CafeTableId { get; set; }

        [Required]
        public string Barcode { get; set; } = string.Empty;

        public Guid? LobbyId { get; set; }
        public Guid GameTemplateId { get; set; }
        public List<Guid>? InitialMemberUserIds { get; set; }
    }
}
