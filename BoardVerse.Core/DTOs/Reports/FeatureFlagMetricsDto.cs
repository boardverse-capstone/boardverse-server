namespace BoardVerse.Core.DTOs.Reports
{
    /// <summary>
    /// Snapshot tất cả metrics counters + gauges hiện tại (in-memory).
    /// M2 Phase 6 / Task C5.5.
    /// </summary>
    public class FeatureFlagMetricsDto
    {
        /// <summary>Tên các feature flag đang bật (computed runtime — HostDepositDiscount, MemberBvcPayment).</summary>
        public FeatureFlagStatusDto ActiveFlags { get; set; } = new();

        /// <summary>List cafe IDs đang trong pilot (raw string từ config).</summary>
        public IReadOnlyList<string> PilotCafeIds { get; set; } = Array.Empty<string>();

        /// <summary>Tất cả counters hiện tại.</summary>
        public IReadOnlyDictionary<string, long> Counters { get; set; } = new Dictionary<string, long>();

        /// <summary>Tất cả gauges hiện tại.</summary>
        public IReadOnlyDictionary<string, double> Gauges { get; set; } = new Dictionary<string, double>();

        /// <summary>Thời điểm snapshot (UTC).</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class FeatureFlagStatusDto
    {
        public bool HostDepositDiscountEnabled { get; set; }
        public bool MemberBvcPaymentEnabled { get; set; }
    }
}