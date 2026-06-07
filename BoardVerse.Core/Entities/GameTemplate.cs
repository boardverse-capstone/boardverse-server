namespace BoardVerse.Core.Entities
{
    public class GameTemplate
    {
        private int _minPlayers = 1;
        private int _maxPlayers = 4;
        private int _playTime = 30;

        public Guid Id { get; set; }
        private int? _bggGameId;
        public int? BggGameId
        {
            get => _bggGameId;
            set
            {
                if (value.HasValue && value.Value < 1)
                    throw new ArgumentException("BggGameId must be positive if set");
                _bggGameId = value;
            }
        }
        public string Name { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? Description { get; set; }

        public int MinPlayers
        {
            get => _minPlayers;
            set
            {
                if (value < 1)
                    throw new ArgumentException("MinPlayers must be at least 1");
                if (value > _maxPlayers)
                    throw new ArgumentException("MinPlayers cannot be greater than MaxPlayers");
                _minPlayers = value;
            }
        }

        public int MaxPlayers
        {
            get => _maxPlayers;
            set
            {
                if (value < 1)
                    throw new ArgumentException("MaxPlayers must be at least 1");
                if (value < _minPlayers)
                    throw new ArgumentException("MaxPlayers cannot be less than MinPlayers");
                _maxPlayers = value;
            }
        }

        public int PlayTime
        {
            get => _playTime;
            set
            {
                if (value <= 0)
                    throw new ArgumentException("PlayTime must be positive");
                _playTime = value;
            }
        }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property for components
        public virtual ICollection<GameComponentTemplate> Components { get; set; } = new List<GameComponentTemplate>();
    }
}
