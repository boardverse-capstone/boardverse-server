using System.Text.Json.Serialization;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Hóa đơn cá nhân của từng thành viên trong phiên chơi.
    /// BR-15: Invoice cá nhân = Subtotal + Penalty - DepositAppliedAmount
    /// GAP-33 Fix: Trả thông tin chi tiết per-member trong PaySession
    /// </summary>
    public class MemberInvoiceDto
    {
        public Guid MemberId { get; set; }
        public Guid? UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsGuestSlot { get; set; }

        /// <summary>Số phút thực tế thành viên tham gia.</summary>
        public int PlayedMinutes { get; set; }

        /// <summary>Thời gian tham gia (JoinedAt hoặc StartedAt).</summary>
        public DateTime JoinedAt { get; set; }

        /// <summary>Tiền giờ chơi (theo thời gian thực tế).</summary>
        public decimal Subtotal { get; set; }

        /// <summary>Phí phạt linh kiện (nếu có).</summary>
        public decimal PenaltyAmount { get; set; }

        /// <summary>Số tiền cọc đã áp dụng (chỉ dành cho thành viên có booking).</summary>
        public decimal DepositAppliedAmount { get; set; }

        /// <summary>Tổng tiền thành viên phải trả.</summary>
        public decimal TotalAmount { get; set; }

        /// <summary>BVC đã capture chưa (khi check-in hoặc quá hạn).</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BvcCaptureStatus? BvcCaptureStatus { get; set; }

        /// <summary>Chi tiết phí phạt (component name + amount).</summary>
        public List<PenaltyDetailDto> PenaltyDetails { get; set; } = [];
    }

    /// <summary>
    /// Chi tiết phí phạt cho từng linh kiện.
    /// </summary>
    public class PenaltyDetailDto
    {
        public Guid ComponentId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public int MissingQuantity { get; set; }
        public int DamagedQuantity { get; set; }
        public decimal PenaltyFee { get; set; }
        public decimal TotalPenalty { get; set; }
        public Guid? ResponsibleMemberId { get; set; }
    }

    /// <summary>
    /// Trạng thái capture BVC.
    /// </summary>
    public enum BvcCaptureStatus
    {
        Pending,
        Captured,
        Failed,
        NotApplicable,
        // Fix #J: Lobby đã terminal (Closed/TimeoutFailed/HostCancelled) trước khi PaySession
        // chạy → KHÔNG capture (đã refund/release ở BR-REFUND-01) nhưng vẫn commit payment
        // cho ActiveSession (cash vẫn thu được từ khách, chỉ BVC deposit không capture).
        SkippedLobbyTerminal
    }
}
