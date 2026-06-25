using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Game
{
    public class ResolveGamePlayNavigationRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        public PlayerPlayMode PlayMode { get; set; }
    }
}
