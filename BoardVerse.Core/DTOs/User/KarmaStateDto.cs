namespace BoardVerse.Core.DTOs.User
{
    public class KarmaStateDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public int KarmaPoints { get; set; }
        public string GamerTier { get; set; } = "Bronze";
        public string? AvatarUrl { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>Lịch sử thay đổi Karma (entries gần nhất).</summary>
        public List<KarmaLogEntryDto> RecentHistory { get; set; } = new();
    }

    /// <summary>DTO rút gọn cho endpoint <c>GET /api/v1/users/{id}/karma</c>.</summary>
    public class UserKarmaStateDto
    {
        public Guid UserId { get; set; }
        public int KarmaPoints { get; set; }
        public string KarmaLevel { get; set; } = string.Empty;
    }

    /// <summary>DTO cho endpoint <c>POST /api/v1/users/{id}/karma/appeal</c>.</summary>
    public class SubmitKarmaAppealRequestDto
    {
        public Guid RecordId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}