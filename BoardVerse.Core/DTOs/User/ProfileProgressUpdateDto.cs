using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.User
{
    public class ProfileProgressUpdateDto
    {
        [Range(0, int.MaxValue, ErrorMessage = ApiErrorMessages.Validation.GlobalEloMinZero)]
        public int GlobalElo { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = ApiErrorMessages.Validation.LevelMin1)]
        public int Level { get; set; }
    }
}
