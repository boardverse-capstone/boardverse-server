using BoardVerse.Core.DTOs.Reservation;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service orchestration cho Reservation flow (BR §21A.2..21A.6).
/// Triển khai:
/// - 21A.2: Quote — validate user eligibility + tính cọc + trả quote.
/// - 21A.3: Confirm — atomic transaction hold BVC + seat + game + create reservation + lobby.
/// - 21A.6: Cancel — host hủy lobby, áp dụng BR-REFUND-02/03.
/// - 21A.9: No-show/mark-expired — scheduler invoke Phase 4.
/// - BR-NEW-11: Cafe approve/reject.
/// </summary>
public interface IReservationService
{
    /// <summary>
    /// Tạo quote cho 1 reservation. Idempotent theo IdempotencyKey.
    /// Validate toàn bộ BR-USER-LIMIT-* + BR-LOBBY-01 + BR-NEW-01.
    /// </summary>
    Task<ReservationQuoteDto> CreateQuoteAsync(Guid hostId, ReservationQuoteRequestDto request);

    /// <summary>
    /// Confirm reservation — atomic transaction:
    /// 1. Re-validate eligibility + quote not expired.
    /// 2. Lock seat_inventory + game_inventory (SELECT FOR UPDATE).
    /// 3. Lock wallet + hold BVC (DEPOSIT_HOLD).
    /// 4. Insert reservation (Holding) + lobby (PendingActivation hoặc PendingCafeApproval).
    /// 5. Commit + save.
    /// Nếu bất kỳ step nào fail → rollback toàn bộ.
    /// Idempotent theo IdempotencyKey.
    /// </summary>
    Task<ReservationConfirmResponseDto> ConfirmAsync(Guid hostId, ReservationConfirmRequestDto request);

    /// <summary>
    /// Host hủy reservation (BR-REFUND-02/03): áp dụng % hoàn theo mốc 24h/6h, grace 15p.
    /// </summary>
    Task<CancelReservationResponseDto> CancelAsync(Guid hostId, CancelReservationRequestDto request);

    /// <summary>
    /// BR-REFUND-08 (walk-in-override-design §2.3):
    /// Host hủy reservation SAU khi đã check-in tại quán.
    /// Áp dụng soft-release refund 30% nếu playedRatio ≥ 50% slot, forfeit toàn bộ nếu &lt; 50%.
    /// Transition: Reservation.Status CheckedIn → CancelledByPlayer.
    /// </summary>
    /// <param name="hostId">UserId của host (chỉ host mới được hủy).</param>
    /// <param name="request">Chứa ReservationId + Reason.</param>
    /// <returns>Refund/Forfeit breakdown theo playedRatio.</returns>
    /// <exception cref="NotFoundException">Reservation không tồn tại.</exception>
    /// <exception cref="ForbiddenException">User không phải host.</exception>
    /// <exception cref="ConflictException">Reservation chưa check-in (status ≠ CheckedIn).</exception>
    Task<CancelAfterCheckinResponseDto> CancelAfterCheckinAsync(
        Guid hostId,
        CancelAfterCheckinRequestDto request);

    /// <summary>
    /// Cafe duyệt/từ chối lobby pending (BR-NEW-11 §XII).
    /// </summary>
    Task<CafeApprovalResponseDto> HandleCafeApprovalAsync(
        Guid cafeManagerUserId,
        CafeApprovalRequestDto request);

    /// <summary>
    /// Scheduler: xử lý reservation đến recruitmentDeadline (BR-LOBBY-02).
    /// </summary>
    Task<int> ProcessDeadlineReservationsAsync(DateTime cutoff, int batchSize, CancellationToken ct);

    /// <summary>
    /// Scheduler: xử lý lobby pendingCafeApproval quá 24h (BR-NEW-11 §XII).
    /// </summary>
    Task<int> ProcessCafeApprovalExpiryAsync(DateTime cutoff, int batchSize, CancellationToken ct);

    /// <summary>
    /// Scheduler: xử lý no-show sau scheduledTime + grace period (BR §21A.9).
    /// </summary>
    Task<int> ProcessNoShowAsync(DateTime cutoff, int batchSize, CancellationToken ct);

    /// <summary>
    /// GAP-9 Fix: Retry BVC capture cho các phiên đã PAID nhưng capture thất bại.
    /// Chạy qua background job mỗi 5 phút.
    /// </summary>
    Task<int> ProcessBvcCaptureRetryAsync(DateTime cutoff, int batchSize, CancellationToken ct);

    /// <summary>
    /// BR §21A.7 + BR-REVENUE-01 stub: POS scan QR check-in.
    /// Atomic transaction:
    /// 1. Validate ReservationCode (unique, 8-char alphanumeric).
    /// 2. Validate status = Confirmed (đã đạt minPlayers), chưa CheckedIn.
    /// 3. Validate cafe ownership + time window.
    /// 4. Update Reservation.Status = CheckedIn + Lobby.Status = InProgress.
    /// 5. Move seat: held → inUse.
    /// 6. Move game copy: held → inUse (gán barcode cụ thể).
    /// 7. Outbox event LobbyCheckedIn để mobile + POS biết.
    /// Idempotent theo ReservationCode (gọi 2 lần cùng code → trả kết quả cũ).
    /// </summary>
    Task<ReservationCheckInResponseDto> CheckInAsync(Guid staffUserId, ReservationCheckInRequestDto request);

    /// <summary>
    /// BR §21A.8 + BR-REVENUE-01: POS đóng phiên (ActiveSession → Paid) → capture BVC deposit
    /// về doanh thu quán. Lookup Reservation theo lobbyId, ghi DEPOSIT_CAPTURE ledger entry,
    /// chuyển Reservation.Status = Completed, giải phóng seat + game inventory inUse → Available,
    /// phát Outbox event SessionCompleted.
    ///
    /// Idempotent theo (lobbyId): nếu reservation đã Completed → skip capture (trả null BvcHoldResult).
    /// Không throw nếu reservation không tồn tại (legacy session không có lobbyId → cũ).
    /// </summary>
    Task CompleteAndCaptureAsync(Guid lobbyId, Guid activeSessionId, CancellationToken ct = default);

    /// <summary>
    /// BR-END-01..05 (docs/time-slot-fixed-end-design (1).md §3.4 + §21A.8):
    /// POS end session + settle deposit + có thể tạo WalkInWindow + ghi Karma violation.
    /// </summary>
    /// <param name="staffUserId">UserId của staff (CafeStaff/Manager).</param>
    /// <param name="request">ReservationId + ActualEndAt optional + Reason optional.</param>
    /// <returns>RefundBvc, ForfeitBvc, PlayedRatio, WalkInWindowId, KarmaRecorded.</returns>
    /// <exception cref="NotFoundException">Reservation không tồn tại.</exception>
    /// <exception cref="ForbiddenException">User không phải staff của cafe.</exception>
    /// <exception cref="ConflictException">Reservation chưa check-in (status ≠ CheckedIn).</exception>
    Task<EndReservationResponseDto> EndAndSettleAsync(Guid staffUserId, EndReservationRequestDto request);

    /// <summary>
    /// Lấy chi tiết 1 reservation.
    /// Validate: user phải là host hoặc member của lobby.
    /// </summary>
    Task<ReservationDetailDto?> GetByIdAsync(Guid userId, Guid reservationId);

    /// <summary>
    /// Lấy danh sách reservation với filter + phân trang.
    /// BR-USER-LIMIT-01: user chỉ thấy reservation mình host hoặc có tham gia.
    /// </summary>
    Task<ReservationListResponseDto> GetListAsync(Guid userId, ReservationListRequestDto request);

    /// <summary>
    /// BR-NEW-11: Lấy chi tiết một reservation pending cafe approval.
    /// </summary>
    Task<LobbyPendingApprovalItemDto?> GetPendingCafeApprovalDetailAsync(
        Guid managerUserId,
        Guid reservationId);

    /// <summary>
    /// BR-NEW-11: Lấy danh sách lobby pending cafe approval cho manager.
    /// </summary>
    Task<LobbyPendingApprovalListResponseDto> GetPendingCafeApprovalAsync(
        Guid managerUserId,
        LobbyPendingApprovalRequestDto request);

    /// <summary>
    /// BR-REFUND-07: Admin override refund amount cho reservation đã completed.
    /// Cho phép refund một phần hoặc toàn bộ số BVC đã capture.
    /// Ghi AdminCredit ledger entry + PlayerActionHistory audit.
    /// </summary>
    Task<AdminOverrideRefundResultDto> AdminOverrideRefundAsync(
        Guid adminUserId,
        Guid reservationId,
        AdminOverrideRefundRequestDto request,
        string idempotencyKey);
}
