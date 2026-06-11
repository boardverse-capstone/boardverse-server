using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.CafePartner
{
    public class AdminCafePartnerApplicationQueryDto
    {
        public string? Search { get; set; }
        public CafePartnerApplicationStatus? Status { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
