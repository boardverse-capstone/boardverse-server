using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.User
{
    public class UpdatePlayerLocationRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        [Range(-90, 90, ErrorMessage = ApiErrorMessages.Validation.LatitudeRange)]
        public double Latitude { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        [Range(-180, 180, ErrorMessage = ApiErrorMessages.Validation.LongitudeRange)]
        public double Longitude { get; set; }

        public PlayerLocationSource Source { get; set; } = PlayerLocationSource.Gps;
    }
}
