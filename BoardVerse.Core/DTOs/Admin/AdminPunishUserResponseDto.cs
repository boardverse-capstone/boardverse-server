namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminPunishUserResponseDto
    {
        public Guid UserId { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string AccountStatus { get; set; } = string.Empty;
        public DateTime? LockoutEndDate { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
