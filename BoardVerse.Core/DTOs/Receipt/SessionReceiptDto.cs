namespace BoardVerse.Core.DTOs.Receipt
{
    /// <summary>
    /// Receipt chi tiết cho một phiên chơi đã thanh toán.
    /// P-01: Receipt Generation API
    /// </summary>
    public class SessionReceiptDto
    {
        public Guid SessionId { get; set; }
        public string CafeName { get; set; } = string.Empty;
        public string CafeAddress { get; set; } = string.Empty;
        public DateTime SessionStart { get; set; }
        public DateTime SessionEnd { get; set; }
        public int DurationMinutes { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string? TableName { get; set; }
        public List<MemberReceiptItemDto> Members { get; set; } = [];
        public decimal TotalSubtotal { get; set; }
        public decimal TotalDepositApplied { get; set; }
        public decimal TotalPenalty { get; set; }
        public decimal GrandTotal { get; set; }
        public DateTime PaidAt { get; set; }
    }

    /// <summary>
    /// Chi tiết thanh toán của từng thành viên trong phiên.
    /// </summary>
    public class MemberReceiptItemDto
    {
        public Guid MemberId { get; set; }
        public Guid? UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsGuestSlot { get; set; }
        public int DurationMinutes { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DepositApplied { get; set; }
        public decimal Penalty { get; set; }
        public decimal Total { get; set; }
    }
}
