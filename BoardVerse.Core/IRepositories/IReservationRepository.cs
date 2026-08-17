using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho Reservation (§19.2). Cung cấp:
/// - Tra cứu bằng IdempotencyKey cho BR § XVII.1 (chống double-confirm).
/// - Query theo host/cafe cho BR-USER-LIMIT-* + BR-NEW-08.
/// - Scheduler query (status + recruitmentDeadline) cho deadline hosted service (§21A.5).
/// </summary>
public interface IReservationRepository
{
    Task<Reservation?> GetByIdAsync(Guid reservationId, bool includeRelations = false);

    Task<Reservation?> GetByIdempotencyKeyAsync(string idempotencyKey);

    /// <summary>
    /// BR §21A.7: POS scan QR check-in dùng ReservationCode (8-char alphanumeric unique).
    /// Trả null nếu không tìm thấy.
    /// </summary>
    Task<Reservation?> GetByReservationCodeAsync(string reservationCode);

    Task<Reservation?> GetByLobbyIdAsync(Guid lobbyId);

    Task<IReadOnlyList<Reservation>> GetByHostAndPlayDateAsync(Guid hostId, DateOnly playDate);

    Task<IReadOnlyList<Reservation>> GetActiveByCafePlayDateSlotAsync(Guid cafeId, DateOnly playDate, TimeSlot timeSlot);

    Task<IReadOnlyList<Reservation>> GetActiveByHostAsync(Guid hostId);

    Task<IReadOnlyList<Reservation>> GetJoinedByUserAsync(Guid userId);

    /// <summary>
    /// Lấy reservation overlap với [startTime, endTime] cho 1 cafe.
    /// Dùng cho CafeBookingService.GetAvailabilityAsync (Flow B) để tính capacity chính xác
    /// khi cả Flow A (Reservation) và Flow B (Booking) cùng giữ ghế.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetOverlappingReservationsAsync(
        Guid cafeId, DateTime startTime, DateTime endTime);

    /// <summary>
    /// BR §21A.5: lấy các reservation đang Holding mà recruitmentDeadline ≤ cutoff.
    /// Dùng cho RecruitmentDeadlineJob.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetDueForDeadlineAsync(DateTime cutoff, int limit = 100);

    /// <summary>
    /// BR-NEW-11 §XII: lobby pendingCafeApproval quá 24h.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetDueForCafeApprovalExpiryAsync(DateTime cutoff, int limit = 100);

    /// <summary>
    /// BR §21A.9: lấy reservation Confirmed mà scheduledTime + grace &lt; cutoff (chưa check-in).
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetDueForNoShowAsync(DateTime cutoff, int limit = 100);

    /// <summary>
    /// BR-NEW-11: lấy lobby pending cafe approval cho manager.
    /// Filter theo danh sách CafeId mà user quản lý.
    /// </summary>
    Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetPendingCafeApprovalAsync(
        List<Guid> cafeIds,
        Guid? cafeId,
        DateOnly? playDate,
        int page,
        int pageSize);

    /// <summary>
    /// BR-NEW-11: Lấy 1 reservation pending cafe approval theo ID.
    /// </summary>
    Task<Reservation?> GetPendingCafeApprovalByIdAsync(Guid reservationId);

    /// <summary>
    /// BR-NEW-05: đếm số lần tạo + hủy của host cho cùng playDate.
    /// </summary>
    Task<int> CountHostActionsForPlayDateAsync(Guid hostId, DateOnly playDate);

    /// <summary>
    /// Lấy danh sách reservation của 1 cafe cho Manager dashboard.
    /// Filter theo status và playDate, có phân trang.
    /// </summary>
    Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetByCafeAsync(
        Guid cafeId,
        List<ReservationStatus>? statuses,
        DateOnly? playDate,
        int page,
        int pageSize);

    /// <summary>
    /// BR-CHECKIN-02: Lấy danh sách Reservation Confirmed nhưng đã quá 30 phút
    /// sau ScheduledStartTime mà chưa check-in → candidate cho NoShow.
    /// Dùng index IX_Reservations_ScheduledStartTime_Status.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetNoShowCandidatesAsync(DateTime cutoff, CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách reservation với filter + phân trang.
    /// BR-USER-LIMIT-01: user chỉ thấy reservation mình host hoặc có tham gia.
    /// </summary>
    Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetListAsync(
        Guid userId,
        bool hostedByMe,
        bool joinedByMe,
        List<ReservationStatus>? statuses,
        DateOnly? playDate,
        Guid? cafeId,
        int page,
        int pageSize);

    Task AddAsync(Reservation reservation);

    Task UpdateAsync(Reservation reservation);

    Task SaveChangesAsync();
}