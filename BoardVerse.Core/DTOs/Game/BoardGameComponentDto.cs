namespace BoardVerse.Core.DTOs.Game
{
    public class BoardGameComponentDto
    {
        public Guid Id { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public int DefaultQuantity { get; set; }
    }
}
