using System.Text.Json.Serialization;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Preview hóa đơn của một thành viên trong group session — dùng cho Member BVC payment flow (Case 2).
    /// M2 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.6.
    ///
    /// <para>Công thức:</para>
    /// <code>totalDue = member.Subtotal + (IsGuestSlot ? 0 : member.PenaltyAmount) - member.DepositAppliedAmount</code>
    /// <para>BR-14: Guest_Slot không bị charge penalty (mặc định, BR §III.3).</para>
    /// </summary>
    public class MemberBillPreviewDto
    {
        /// <summary>ID ActiveSession.</summary>
        public Guid SessionId { get; set; }

        /// <summary>ID ActiveSessionMember.</summary>
        public Guid MemberId { get; set; }

        /// <summary>UserId của member (null nếu Guest_Slot).</summary>
        public Guid? UserId { get; set; }

        /// <summary>Tên hiển thị (username hoặc GuestDisplayName).</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>True nếu là khách vô danh (BR-13).</summary>
        public bool IsGuestSlot { get; set; }

        /// <summary>Tiền giờ chơi cá nhân.</summary>
        public decimal Subtotal { get; set; }

        /// <summary>Phí phạt linh kiện (đã set 0 cho Guest_Slot).</summary>
        public decimal PenaltyAmount { get; set; }

        /// <summary>Deposit đã áp dụng cho member này (BR-15).</summary>
        public decimal DepositAppliedAmount { get; set; }

        /// <summary>Tổng bill phải trả (= Subtotal + Penalty - DepositApplied).</summary>
        public decimal TotalDue { get; set; }

        /// <summary>
        /// Cờ cho UI biết có cho phép trả penalty bằng BVC không.
        /// Mặc định: false cho Guest_Slot (BR-14), true cho member có UserId.
        /// </summary>
        public bool AllowBvcPenalty { get; set; }

        /// <summary>
        /// Trạng thái thanh toán hiện tại của member.
        /// Cho UI biết member đã paid hay chưa.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MemberPaymentStatus PaymentStatus { get; set; }
    }
}