using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.User
{
    public class UpdatePlayerLocationRequestDto
    {
        [Required]
        [Range(-90, 90)]
        public double Latitude { get; set; }

        [Required]
        [Range(-180, 180)]
        public double Longitude { get; set; }

        public PlayerLocationSource Source { get; set; } = PlayerLocationSource.Gps;
    }
}
