using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Game
{
    public class ResolveGamePlayNavigationRequestDto
    {
        [Required]
        public PlayerPlayMode PlayMode { get; set; }
    }
}
