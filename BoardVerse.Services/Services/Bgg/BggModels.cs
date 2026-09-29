namespace BoardVerse.Services.Services.Bgg
{
    internal sealed class BggThingData
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? ThumbnailUrl { get; init; }
        public int MinPlayers { get; init; } = 1;
        public int MaxPlayers { get; init; } = 4;
        public int PlayTime { get; init; } = 60;

        /// <summary>
        /// BGG Complexity Weight — độ phức tạp game thang 1.0 → 5.0.
        /// Nguồn: BoardGameGeek (averageweight community vote).
        /// Dùng trong MatchScore để bonus/penalize dựa trên trình độ người chơi.
        /// Null nếu BGG chưa có đủ vote hoặc stats không được request.
        /// </summary>
        public double? Weight { get; init; }

        public IReadOnlyList<string> Categories { get; init; } = [];
        public IReadOnlyList<string> Mechanics { get; init; } = [];
    }

    internal sealed class BggSearchItem
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int? YearPublished { get; init; }
    }
}
