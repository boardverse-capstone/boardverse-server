using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.User
{
    public class ProfileProgressUpdateDto
    {
        [Range(0, int.MaxValue, ErrorMessage = "GlobalElo must be zero or greater.")]
        public int GlobalElo { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Level must be at least 1.")]
        public int Level { get; set; }
    }
}
