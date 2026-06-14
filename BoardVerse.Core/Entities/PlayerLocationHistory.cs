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

        public virtual User User { get; set; } = null!;
    }
}
