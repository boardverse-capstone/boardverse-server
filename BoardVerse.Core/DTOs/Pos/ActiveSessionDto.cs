namespace BoardVerse.Core.DTOs.Pos
{
    public class ActiveSessionDto
    {
        public Guid Id { get; set; }
        public Guid CafeTableId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public Guid CafeInventoryBoxId { get; set; }
        public string BoxBarcode { get; set; } = string.Empty;
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public int DefaultPlayTimeMinutes { get; set; }
        public DateTime StartedAt { get; set; }
        public int ElapsedMinutes { get; set; }
        public int EstimatedRemainingMinutes { get; set; }
    }
}
