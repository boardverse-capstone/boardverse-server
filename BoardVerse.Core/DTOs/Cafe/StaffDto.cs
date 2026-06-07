namespace BoardVerse.Core.DTOs.Cafe
{
    public class StaffDto
    {
        public Guid UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public DateTime JoinedAt { get; set; }
    }
}
