using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Match
{
    public class SubmitMatchResultRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.LobbyIdRequired)]
        public Guid LobbyId { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.OutcomeRequired)]
        public MatchOutcome Outcome { get; set; }
    }
}
