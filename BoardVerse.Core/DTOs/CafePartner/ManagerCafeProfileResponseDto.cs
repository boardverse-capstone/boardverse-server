namespace BoardVerse.Core.DTOs.CafePartner
{
    /// <summary>Manager Web POS profile — source of truth is <c>Cafe</c> after approval.</summary>
    public class ManagerCafeProfileResponseDto
    {
        public Guid CafeId { get; set; }
        public Guid ApplicationId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public WorkingHoursDto WorkingHours { get; set; } = new();

        public int NumberOfTables { get; set; }
        public int NumberOfPrivateRooms { get; set; }
        public List<string> SpaceImageUrls { get; set; } = new();
        public int NumberOfGamesOwned { get; set; }
        public string PopularGamesList { get; set; } = string.Empty;
        public bool HasGameMaster { get; set; }
        public string BillingModel { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal? TieredBlockRate { get; set; }
        public int TieredBlockMinutes { get; set; }
        public decimal DepositPercentage { get; set; }
        public int DefaultHoldDurationMinutes { get; set; }
        public bool IsPricingLocked { get; set; }
        public List<string> TableNames { get; set; } = new();

        public string ApplicationStatus { get; set; } = string.Empty;
        public string? OperationalStatus { get; set; }
        public string? OperationalStatusReason { get; set; }
        public bool IsTableLayoutConfigured { get; set; }
        public bool CanActivate { get; set; }
        public bool CanReopen { get; set; }
        public List<string> ActivationBlockers { get; set; } = new();

        public DateTime? ApprovedAt { get; set; }
        public DateTime? OperationalProfileUpdatedAt { get; set; }
    }
}
