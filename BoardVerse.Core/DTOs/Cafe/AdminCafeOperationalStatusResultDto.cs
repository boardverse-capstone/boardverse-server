namespace BoardVerse.Core.DTOs.Cafe
{
    public class AdminCafeOperationalStatusResultDto
    {
        public Guid CafeId { get; set; }
        public string OperationalStatus { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? Reason { get; set; }
    }
}
