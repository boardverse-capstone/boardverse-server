using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Lobby
{
    public class SearchLobbiesRequestDto
    {
        [Required]
        public Guid GameTemplateId { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? RadiusKm { get; set; }

        /// <summary>
        /// BR-10: Lọc theo điểm Karma tối thiểu.
        /// Không dùng Elo vì Elo chỉ dùng trong phân hệ Giải đấu.
        /// </summary>
        public int? MinKarmaScore { get; set; }
    }
}
