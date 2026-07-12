using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Lobby
{
    public class CreateLobbyRequestDto
    {
        [Required]
        public Guid GameTemplateId { get; set; }

        [Required]
        public DateTime ScheduledStartTime { get; set; }

        [Range(1, 20)]
        public int MaxMembers { get; set; }

        [Range(0, 1440)]
        public int CancellationLeadTimeMinutes { get; set; } = 30;
    }
}
