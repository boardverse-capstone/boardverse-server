using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho Reservation (§19.2). Cung cấp:
/// - Tra cứu bằng IdempotencyKey cho BR § XVII.1 (chống double-confirm).
/// - Query theo host/cafe cho BR-USER-LIMIT-* + BR-NEW-08.
/// - Scheduler query (status + recruitmentDeadline) cho deadline hosted service (§21A.5).
/// </summary>
public interface IReservationRepository
{
    Task<Reservation?> GetByIdAsync(Guid reservationId, bool includeRelations = false, CancellationToken cancellationToken = default);

    Task<Reservation?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR §21A.7: POS scan QR check-in dùng ReservationCode (8-char alphanumeric unique).
    /// Trả null nếu không tìm thấy.
    /// </summary>
    Task<Reservation?> GetByReservationCodeAsync(string reservationCode, CancellationToken cancellationToken = default);

    Task<Reservation?> GetByLobbyIdAsync(Guid lobbyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reservation>> GetByHostAndPlayDateAsync(Guid hostId, DateOnly playDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reservation>> GetActiveByCafePlayDateAsync(Guid cafeId, DateOnly playDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Reservation>> GetActiveByCafePlayDateSlotAsync(Guid cafeId, DateOnly playDate, TimeOnly preferredStartTime, TimeOnly preferredEndTime, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reservation>> GetActiveByHostAsync(Guid hostId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reservation>> GetJoinedByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy reservation overlap với [startTime, endTime] cho 1 cafe.
    /// Dùng cho CafeBookingService.GetAvailabilityAsync (Flow B) để tính capacity chính xác
    /// khi cả Flow A (Reservation) và Flow B (Booking) cùng giữ ghế.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetOverlappingReservationsAsync(
        Guid cafeId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR §21A.5: lấy các reservation đang Holding mà recruitmentDeadline ≤ cutoff.
    /// Dùng cho RecruitmentDeadlineJob.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetDueForDeadlineAsync(DateTime cutoff, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-NEW-11 §XII: lobby pendingCafeApproval quá 24h.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetDueForCafeApprovalExpiryAsync(DateTime cutoff, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR §21A.9: lấy reservation Confirmed mà scheduledTime + grace &lt; cutoff (chưa check-in).
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetDueForNoShowAsync(DateTime cutoff, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-NEW-11: lấy lobby pending cafe approval cho manager.
    /// Filter theo danh sách CafeId mà user quản lý.
    /// </summary>
    Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetPendingCafeApprovalAsync(
        List<Guid> cafeIds,
        Guid? cafeId,
        DateOnly? playDate,
        int page,
        int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-NEW-11: Lấy 1 reservation pending cafe approval theo ID.
    /// </summary>
    Task<Reservation?> GetPendingCafeApprovalByIdAsync(Guid reservationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-NEW-05: đếm số lần tạo + hủy của host cho cùng playDate.
    /// </summary>
    Task<int> CountHostActionsForPlayDateAsync(Guid hostId, DateOnly playDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách reservation của 1 cafe cho Manager dashboard.
    /// Filter theo status và playDate, có phân trang.
    /// </summary>
    Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetByCafeAsync(
        Guid cafeId,
        List<ReservationStatus>? statuses,
        DateOnly? playDate,
        int page,
        int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách reservation upcoming cho POS staff dashboard
    /// (<c>GET /api/cafes/{cafeId}/pos/upcoming-reservations</c>).
    /// Hỗ trợ filter date range, status list, lobby status list, sort, paginate.
    ///
    /// Include relations đầy đủ cho service layer map DTO:
    /// - Host (with Profile, Wallet)
    /// - Cafe, Game
    /// - Lobby (with Members + Members.User + Members.User.Profile)
    /// - SeatInventory / GameInventory
    /// - ActiveSession (for checked-in reservations)
    ///
    /// GAP-FIX-3: Include CafeTable cho TableName.
    /// GAP-FIX-5: Include ActiveSession cho session summary.
    /// GAP-FIX-6/GAP-FIX-8: Include LobbyMember.User + Profile cho member list.
    /// GAP-FIX-11: Include Host.Wallet cho IsCoolingOff flag.
    /// </summary>
    /// <param name="cafeId">Cafe ID cần query (đã validate access ở service layer).</param>
    /// <param name="fromDate">Filter playDate ≥ fromDate (inclusive).</param>
    /// <param name="toDate">Filter playDate ≤ toDate (inclusive).</param>
    /// <param name="statuses">Filter Reservation.Status ∈ statuses. Null/missing = no status filter (service đã filter default active).</param>
    /// <param name="lobbyStatuses">Filter Lobby.Status ∈ lobbyStatuses. Null = không filter (kể cả lobby null cho legacy booking).</param>
    /// <param name="sortBy">Field sort: 0 = ScheduledStartTime, 1 = CreatedAt, 2 = PlayDate.</param>
    /// <param name="sortDir">0 = asc, 1 = desc.</param>
    /// <param name="page">1-indexed page.</param>
    /// <param name="pageSize">Item mỗi trang (clamp 1..100 ở service layer).</param>
    Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetUpcomingForCafeAsync(
        Guid cafeId,
        DateOnly fromDate,
        DateOnly toDate,
        List<ReservationStatus>? statuses,
        List<LobbyStatus>? lobbyStatuses,
        int sortBy,
        int sortDir,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

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
    /// <param name="fromDate">Filter playDate ≥ fromDate (inclusive). Null = không giới hạn.</param>
    /// <param name="toDate">Filter playDate ≤ toDate (inclusive). Null = không giới hạn.</param>
    Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetListAsync(
        Guid userId,
        bool hostedByMe,
        bool joinedByMe,
        List<ReservationStatus>? statuses,
        DateOnly? playDate,
        DateOnly? fromDate,
        DateOnly? toDate,
        Guid? cafeId,
        int page,
        int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đếm số reservation do user host + user join (member-only) áp dụng cùng filter
    /// (statuses/cafeId/fromDate/toDate) cho summary count ở <c>GET /my</c>.
    /// Trả về tuple <c>(HostedCount, JoinedCount)</c>.
    /// </summary>
    /// <remarks>
    /// Member count EXCLUDE self-hosted (tránh double-count khi user vừa host vừa join cùng reservation).
    /// </remarks>
    Task<(int HostedCount, int JoinedCount)> GetParticipationCountsAsync(
        Guid userId,
        List<ReservationStatus>? statuses,
        DateOnly? fromDate,
        DateOnly? toDate,
        Guid? cafeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm kiếm reservation theo tên game hoặc ngày tháng.
    /// BR-USER-LIMIT-01: user chỉ thấy reservation mình host hoặc có tham gia.
    /// </summary>
    Task<(IReadOnlyList<Reservation> Items, int TotalCount)> SearchAsync(
        Guid userId,
        string? gameName,
        DateOnly? fromDate,
        DateOnly? toDate,
        List<ReservationStatus>? statuses,
        Guid? cafeId,
        bool hostedByMe,
        bool joinedByMe,
        int page,
        int pageSize, CancellationToken cancellationToken = default);

    Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default);

    Task UpdateAsync(Reservation reservation, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}