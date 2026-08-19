namespace BoardVerse.Core.DTOs.Receipt
{
    /// <summary>
    /// Báo cáo doanh thu theo kỳ cho quán.
    /// P-02: Revenue Report API
    /// </summary>
    public class RevenueReportDto
    {
        public Guid CafeId { get; set; }
        public string CafeName { get; set; } = string.Empty;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public string Granularity { get; set; } = "daily";
        public decimal TotalRevenue { get; set; }
        public decimal TotalDepositsApplied { get; set; }
        public decimal TotalPenalties { get; set; }
        public int TotalSessions { get; set; }
        public int TotalMembers { get; set; }
        public List<RevenuePeriodDto> Periods { get; set; } = [];
    }

    /// <summary>
    /// Doanh thu theo từng kỳ (ngày/tuần/tháng).
    /// </summary>
    public class RevenuePeriodDto
    {
        public string PeriodKey { get; set; } = string.Empty;
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
        public decimal Revenue { get; set; }
        public decimal DepositsApplied { get; set; }
        public decimal Penalties { get; set; }
        public int SessionCount { get; set; }
        public int MemberCount { get; set; }
        public List<RevenueGameBreakdownDto> ByGame { get; set; } = [];
    }

    /// <summary>
    /// Doanh thu chi tiết theo từng game trong một kỳ.
    /// </summary>
    public class RevenueGameBreakdownDto
    {
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public int SessionCount { get; set; }
        public decimal Revenue { get; set; }
    }
}
