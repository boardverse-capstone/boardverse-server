using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Match
{
    public class SubmitMatchResultRequestDto
    {
        [Required]
        public Guid LobbyId { get; set; }

        [Required]
        public MatchOutcome Outcome { get; set; }
    }
}
