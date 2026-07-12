namespace BoardVerse.Core.DTOs.Cafe
{
    /// <summary>
    /// Public-facing cafe profile.
    /// BR-01/BR-02/BR-03/BR-05: exposes pricing + available seat context.
    /// </summary>
    public class CafeDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }

        public int TotalSeats { get; set; }
        public string BillingModel { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal? TieredBlockRate { get; set; }
        public int TieredBlockMinutes { get; set; }
        public decimal DepositPercentage { get; set; }
        public bool IsPricingLocked { get; set; }

        /// <summary>True nếu cafe đã được cấu hình SePay cho session payment.</summary>
        public bool HasSePayConfigured { get; set; }
    }
}
