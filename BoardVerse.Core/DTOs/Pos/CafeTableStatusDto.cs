using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos
{
    public class CafeTableStatusDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public CafeTableStatus Status { get; set; }
    }
}
