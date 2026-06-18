namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminAdjustKarmaResponseDto
    {
        public Guid UserId { get; set; }
        public int PreviousKarma { get; set; }
        public int NewKarma { get; set; }
        public int AdjustedAmount { get; set; }
        public Guid KarmaLogId { get; set; }
    }
}
