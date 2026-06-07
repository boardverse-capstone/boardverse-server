namespace BoardVerse.Core.DTOs.Cafe
{
    public class CafeDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
