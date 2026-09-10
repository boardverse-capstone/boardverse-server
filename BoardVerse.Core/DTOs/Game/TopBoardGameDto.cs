namespace BoardVerse.Core.DTOs.Game
{
    /// <summary>
    /// Board game xếp hạng theo số lượt chơi trong hệ thống (dùng cho widget UI mobile).
    /// </summary>
    public class TopBoardGameDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? Description { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayTime { get; set; }
        public int ComponentCount { get; set; }
        public int PlayCount { get; set; }
        public List<CategoryDto> Categories { get; set; } = [];
    }
}
