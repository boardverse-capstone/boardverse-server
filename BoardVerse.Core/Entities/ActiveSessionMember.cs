using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Thành viên trong phiên chơi tại quán (ActiveSession - Individual Session).
    /// Theo boardverse-state-machine.mdc - Section 4.2.
    /// BR-12: Kiểm kê khi về sớm
    /// BR-13: Guest_Slot không chịu trách nhiệm tài sản độc lập
    /// BR-14: Phí phạt không gán vào Guest_Slot
    /// </summary>
    public class ActiveSessionMember
    {
        public Guid Id { get; set; }

        // === Relationships ===
        public Guid ActiveSessionId { get; set; }

        /// <summary>Nếu là khách vô danh (BR-13), UserId = null.</summary>
        public Guid? UserId { get; set; }
        public bool IsGuestSlot { get; set; }

        /// <summary>Tên hiển thị cho Guest_Slot.</summary>
        public string? GuestDisplayName { get; set; }

        /// <summary>
        /// Số điện thoại Guest_Slot (optional, dùng để liên hệ khi cần).
        /// Validate format VN (10-11 chữ số, đầu 03/05/07/08/09) ở service layer.
        /// </summary>
        public string? GuestPhoneNumber { get; set; }

        // === Session Link (BR-14: Tách/ghép nhóm) ===
        /// <summary>Session gốc khi member tách nhóm. Dùng để track thời gian liên tục.</summary>
        public Guid? OriginalSessionId { get; set; }

        /// <summary>
        /// Lobby gốc mà member tham gia trước khi ghép vào session này.
        /// Dùng để trace lịch sử di chuyển giữa các lobby.
        /// </summary>
        public Guid? OriginalLobbyId { get; set; }

        /// <summary>
        /// Reservation gốc của OriginalLobbyId.
        /// Dùng để trace BVC deposit khi member ghép nhóm.
        /// </summary>
        public Guid? OriginalReservationId { get; set; }

        /// <summary>
        /// Lobby cụ thể mà member ghép từ (dùng khi member ghép trực tiếp vào active session).
        /// Khác OriginalLobbyId: OriginalLobbyId là lobby đầu tiên, MergedFromLobbyId là lobby vừa rời.
        /// </summary>
        public Guid? MergedFromLobbyId { get; set; }

        /// <summary>
        /// Thời điểm member được ghép vào session này (từ lobby khác).
        /// </summary>
        public DateTime? MergedAt { get; set; }

        // === Individual Session State ===
        /// <summary>Trạng thái phiên cá nhân.</summary>
        public IndividualSessionStatus Status { get; set; } = IndividualSessionStatus.Playing;

        // === Timing (Individual) ===
        /// <summary>Thời điểm bắt đầu chơi (có thể khác StartedAt của session gốc khi ghép nhóm).</summary>
        public DateTime JoinedAt { get; set; }

        /// <summary>
        /// Thời điểm kết thúc phiên cá nhân. Được set khi:
        /// (1) Member checkout sớm (early leave) → LeftAt = checkout_time
        /// (2) Member merge sang session khác (Exception 4) → LeftAt = merge_time
        /// (3) Admin force-remove → LeftAt = removal_time
        ///
        /// Ảnh hưởng (BR-15 modified - Host Deposit Discount):
        /// - Khi session Pay: filter <c>LeftAt > payTime</c> → members đã rời EXCLUDED khỏi deposit discount
        ///   (chỉ DiscountGroup mode; DiscountHostOnly không bị ảnh hưởng)
        /// - Member đã merge: bill consolidated tại session hiện tại (Exception 4 - BR-MERGE-01)
        ///
        /// Empty và null là KHÁC NHAU:
        /// - null = member đang active (default khi tạo)
        /// - empty DateTime = data corruption; sửa trong service layer
        /// </summary>
        public DateTime? LeftAt { get; set; }

        /// <summary>Tổng phút chơi của cá nhân này.</summary>
        public int TotalMinutesPlayed { get; set; }

        // === Penalty (BR-14) ===
        /// <summary>Phí phạt thiếu linh kiện. KHÔNG gán vào Guest_Slot. (BR-14)</summary>
        public decimal PenaltyAmount { get; set; }

        /// <summary>Lý do phạt.</summary>
        public string? PenaltyReason { get; set; }

        /// <summary>Đã thanh toán phí phạt chưa.</summary>
        public bool IsPenaltyPaid { get; set; }

        // === Host role (BR-12, BR-22) ===
        /// <summary>
        /// True nếu thành viên này là host của phiên chơi.
        /// Host chịu trách nhiệm tổng quát phiên + là người đặt cọc BVC.
        /// Mỗi ActiveSession chỉ có duy nhất 1 host member.
        /// </summary>
        public bool IsHost { get; set; }

        // === Checkout ===
        /// <summary>True nếu đã thanh toán và rời nhóm (về sớm).</summary>
        public bool IsCheckedOut { get; set; }

        /// <summary>Thời điểm checkout.</summary>
        public DateTime? CheckedOutAt { get; set; }

        // === Deposit (BR-15, BR-22) ===
        /// <summary>
        /// GAP-10 Fix: Số tiền deposit đã áp dụng cho thành viên này khi thanh toán.
        /// Mỗi thành viên có deposit riêng nếu có booking.
        /// </summary>
        public decimal DepositAppliedAmount { get; set; }

        /// <summary>
        /// GAP-10 Fix: ID của deposit đã áp dụng cho thành viên này.
        /// </summary>
        public Guid? DepositId { get; set; }

        // === Billing (BR-15: hóa đơn cá nhân) ===
        /// <summary>
        /// Tiền giờ chơi cá nhân (subtotal trước penalty, trước khi áp deposit).
        /// Tính theo công thức: tổng phút chơi cá nhân × đơn giá áp dụng.
        /// </summary>
        public decimal Subtotal { get; set; }

        /// <summary>
        /// Tổng hóa đơn cuối cùng phải thanh toán.
        /// Công thức: Subtotal + PenaltyAmount - DepositAppliedAmount.
        /// </summary>
        public decimal TotalAmount { get; set; }

        // === Per-Member Payment Status (Split Bill) ===
        /// <summary>
        /// Trạng thái thanh toán của thành viên này.
        /// </summary>
        public MemberPaymentStatus PaymentStatus { get; set; } = MemberPaymentStatus.NotPaid;

        /// <summary>
        /// Phương thức thanh toán đã sử dụng (CASH, QR_CODE, BANK_TRANSFER).
        /// </summary>
        public string? PaymentMethod { get; set; }

        /// <summary>
        /// Thời điểm thanh toán thành công.
        /// </summary>
        public DateTime? PaidAt { get; set; }

        /// <summary>
        /// Transaction ID của thanh toán thành công.
        /// </summary>
        public Guid? TransactionId { get; set; }

        // === Per-Member QR Payment (Split Bill - Mobile Display) ===
        /// <summary>
        /// URL hình ảnh QR code thanh toán cho thành viên.
        /// Được set khi staff tạo QR cho member qua Split Bill.
        /// Player có thể truy vấn endpoint để hiển thị QR trên mobile.
        /// </summary>
        public string? QrImageUrl { get; set; }

        /// <summary>
        /// URL thanh toán gateway (nếu là QR).
        /// </summary>
        public string? QrPaymentUrl { get; set; }

        /// <summary>
        /// Order ID của QR payment (format: BV-MEMBER-{memberId}).
        /// Dùng để player hiển thị nội dung chuyển khoản.
        /// </summary>
        public string? QrOrderId { get; set; }

        /// <summary>
        /// Số tài khoản / nội dung chuyển khoản hiển thị trên QR.
        /// </summary>
        public string? QrTransferContent { get; set; }

        // === M1: Per-Member Deposit Refund Tracking (Option A) ===
        // docs/design/host-deposit-discount-and-bvc-payment-design.md §B1.3

        /// <summary>
        /// M1 / Option A: Thời điểm deposit được refund về wallet (khi member merge sang lobby khác - Exception 4).
        /// Nullable: null = chưa refund.
        /// Set bởi <c>MergeService.HandleMemberMergeAsync</c> + <c>WalletService.RefundMemberDepositOnMergeAsync</c>.
        /// </summary>
        public DateTime? DepositRefundedAt { get; set; }

        /// <summary>
        /// M1 / Option A: Lý do refund (e.g., "Merged từ Lobby X", "DepositConsumed_BeforeMerge", "GuestSlot_NoDeposit").
        /// Max 500 ký tự. Set cùng <see cref="DepositRefundedAt"/>.
        /// </summary>
        public string? DepositRefundReason { get; set; }

        /// <summary>
        /// M1 / Option A: FK to <c>BvcLedgerEntry.Id</c> của refund transaction.
        /// Nullable: null = chưa refund.
        /// Set bởi <c>WalletService.RefundMemberDepositOnMergeAsync</c> sau khi insert <see cref="Core.Enum.LedgerEntryType.DepositRefund_Merge"/> entry.
        /// </summary>
        public Guid? DepositRefundLedgerId { get; set; }

        // === M2: BVC Payment Tracking (Case 2 — Member BVC bill payment) ===
        // docs/design/host-deposit-discount-and-bvc-payment-design.md §C1.2

        /// <summary>
        /// M2: Số BVC đã trừ từ wallet cho bill của member này.
        /// Long (BVC integer), mặc định 0. Set bởi <c>WalletSessionPaymentService.PayMemberBillAsync</c>.
        /// </summary>
        public long PaidBvcAmount { get; set; }

        /// <summary>
        /// M2: Số VND cash còn lại khi member trả partial BVC + cash (MemberPaymentStatus.PartialBvc).
        /// Mặc định 0. Chỉ set khi status = PartialBvc.
        /// </summary>
        public decimal PaidCashRemainder { get; set; }

        /// <summary>
        /// M2: Thời điểm refund BVC về wallet (Gap #11 — bill sai / dispute).
        /// Nullable: null = chưa refund.
        /// Set bởi <c>WalletSessionPaymentService.RefundMemberBillAsync</c>.
        /// </summary>
        public DateTime? BvcRefundedAt { get; set; }

        /// <summary>
        /// M2: Lý do refund BVC (vd: "BillSai", "DisputeResolved").
        /// Max 500 ký tự. Set cùng <see cref="BvcRefundedAt"/>.
        /// </summary>
        public string? BvcRefundReason { get; set; }

        // === M2/C2.16: Force-close tracking (Gap #33) ===
        // docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.16
        // Lưu các dấu vết khi Manager force-close session có unpaid members.

        /// <summary>
        /// M2/C2.16: Thời điểm member bị mark NoShow (Manager chọn UnpaidMemberHandling = "MarkNoShow").
        /// Nullable: null = chưa bị mark NoShow.
        /// Set bởi <c>ForceCloseService</c>.
        /// </summary>
        public DateTime? NoShowAt { get; set; }

        /// <summary>
        /// M2/C2.16: Lý do mark NoShow (vd: "ForceClose_MarkNoShow").
        /// Max 500 ký tự. Set cùng <see cref="NoShowAt"/>.
        /// </summary>
        public string? NoShowReason { get; set; }

        /// <summary>
        /// M2/C2.16: Thời điểm member được host cover (Manager chọn UnpaidMemberHandling = "CompensationByHost").
        /// Nullable: null = chưa được cover.
        /// Set bởi <c>ForceCloseService</c>.
        /// </summary>
        public DateTime? PaidByHostAt { get; set; }

        /// <summary>
        /// M2/C2.16: UserId của host đã cover bill cho member (thường = session.HostId).
        /// Nullable: null = chưa được cover. Set cùng <see cref="PaidByHostAt"/>.
        /// </summary>
        public Guid? PaidByHostUserId { get; set; }

        // === Navigation ===
        public virtual ActiveSession ActiveSession { get; set; } = null!;
        public virtual User? User { get; set; }

        // === Audit ===
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Last write timestamp. Used as concurrency token for optimistic concurrency on penalty/financial updates.</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
