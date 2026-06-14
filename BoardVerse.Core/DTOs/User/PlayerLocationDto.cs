namespace BoardVerse.Core.DTOs.User
{
    public class PlayerLocationDto
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? Source { get; set; }
        public bool HasLocation { get; set; }
    }
}
