using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class PlayerLocationHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public PlayerLocationSource Source { get; set; } = PlayerLocationSource.Gps;
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        // Reverse-geocode snapshot tại thời điểm ghi (Nominatim). Nullable vì lookup có thể fail.
        public string? ResolvedDistrict { get; set; }
        public string? ResolvedCity { get; set; }
        public string? ResolvedCountry { get; set; }
        public string? ResolvedDisplayName { get; set; }

        public virtual User User { get; set; } = null!;
    }
}
