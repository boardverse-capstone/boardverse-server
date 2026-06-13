namespace BoardVerse.Core.DTOs.CafePartner
{
    public class OnboardPartnerResultDto
    {
        public CafePartnerApplicationResponseDto Application { get; set; } = new();
        public Guid ManagerUserId { get; set; }
        public string ManagerEmail { get; set; } = string.Empty;
        public Guid CafeId { get; set; }
        public string? TemporaryPassword { get; set; }
    }
}
