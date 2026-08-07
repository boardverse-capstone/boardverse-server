using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories
{
/// <summary>
/// Repository cho Lobby — mở rộng với BR-NEW-* (Reservation flow):
/// - ActiveLobbiesByHostAsync: BR-USER-LIMIT-01 (1 host lobby active).
/// - ActiveLobbiesByMemberAsync: BR-USER-LIMIT-01 (1 member lobby active).
/// - CountHostCreatesAsync: BR-NEW-05 (max 5 lần tạo+hủy / playDate).
/// </summary>
public interface ILobbyRepository
{
    Task<Lobby?> GetByIdAsync(Guid lobbyId);
    Task<Lobby?> GetByIdWithMembersAsync(Guid lobbyId);
    Task<Lobby?> GetByActiveSessionIdAsync(Guid activeSessionId);

    /// <summary>
    /// Tra cứu lobby bằng share code (dùng cho join lobby private qua link).
    /// </summary>
    Task<Lobby?> GetByShareCodeAsync(string shareCode);

    Task<IReadOnlyList<Lobby>> GetActiveLobbiesForGameAsync(Guid gameTemplateId, Guid? excludeLobbyId);

    /// <summary>
    /// Lấy các lobby public đang mở (status=Open, IsPrivate=false) mà bất kỳ player nào cũng có thể thấy/join.
    /// Lọc theo game (optional), khoảng cách địa lý (optional).
    /// </summary>
    Task<IReadOnlyList<Lobby>> GetDiscoverablePublicLobbiesAsync(
        Guid? gameTemplateId,
        double? latitude,
        double? longitude,
        double? radiusKm,
        int limit);

    Task<IReadOnlyList<Lobby>> SearchLobbiesNearbyAsync(Guid gameTemplateId, double latitude, double longitude, double radiusKm, int? minKarmaScore);

    /// <summary>
    /// Lấy tất cả lobby do user này host (còn active + đã đóng).
    /// </summary>
    Task<IReadOnlyList<Lobby>> GetLobbiesByHostAsync(Guid hostUserId);

    /// <summary>
    /// Lấy các lobby user đang tham gia (active, chưa đóng).
    /// </summary>
    Task<IReadOnlyList<Lobby>> GetJoinedLobbiesAsync(Guid userId);

    // ===== BR-NEW-* mở rộng cho Reservation flow =====

    /// <summary>
    /// BR-USER-LIMIT-01: lobby do user này host mà status ∈ (Open, Viable, Full, PendingCafeApproval, PendingActivation, InProgress).
    /// </summary>
    Task<IReadOnlyList<Lobby>> GetActiveLobbiesByHostAsync(Guid hostUserId);

    /// <summary>
    /// BR-NEW-02: lobby active của host cho 1 playDate cụ thể.
    /// </summary>
    Task<IReadOnlyList<Lobby>> GetActiveLobbiesByHostAsync(Guid hostUserId, DateOnly playDate);

    /// <summary>
    /// BR-USER-LIMIT-01: lobby user đang làm member mà status ∈ active.
    /// </summary>
    Task<IReadOnlyList<Lobby>> GetActiveLobbiesByMemberAsync(Guid userId);

    /// <summary>
    /// BR-NEW-08: lobby active của user trong cùng (cafe, playDate, timeSlot).
    /// </summary>
    Task<IReadOnlyList<Lobby>> GetActiveLobbiesByCafeDateSlotAsync(Guid cafeId, DateOnly playDate, Core.Enum.TimeSlot timeSlot);

    /// <summary>
    /// BR-NEW-08: lobby active của user cụ thể trong cùng (cafe, playDate, timeSlot).
    /// </summary>
    Task<IReadOnlyList<Lobby>> GetActiveLobbiesByCafeDateSlotAsync(Guid userId, Guid cafeId, DateOnly playDate, Core.Enum.TimeSlot timeSlot);

    /// <summary>
    /// BR-USER-LIMIT-02: lobby của user có overlap với khung [startTime, endTime] (+30p buffer).
    /// Lấy từ Reservation.PlayDate + TimeSlot để so sánh.
    /// </summary>
    Task<IReadOnlyList<Lobby>> GetOverlappingLobbiesAsync(
        Guid userId,
        DateOnly playDate,
        Core.Enum.TimeSlot timeSlot,
        DateTime newRecruitmentDeadline,
        DateTime newScheduledTime);

    /// <summary>
    /// BR-NEW-05: đếm số lobby user đã tạo (status không phải terminal) cho 1 playDate.
    /// </summary>
    Task<int> CountActiveOrTerminalByHostPlayDateAsync(Guid hostUserId, DateOnly playDate);

    Task<BookingDeposit?> GetBookingByIdAsync(Guid bookingId);

    Task<IReadOnlyList<LobbyMember>> GetMembersAsync(Guid lobbyId);

    /// <summary>
    /// R-Bug-026 Fix: kiểm tra user có phải thành viên active của lobby không.
    /// Dùng cho SignalR PosHub/LobbyHub authorization.
    /// </summary>
    Task<bool> IsUserLobbyMemberAsync(Guid lobbyId, Guid userId);

    /// <summary>
    /// R-Bug-026 Fix: kiểm tra user có phải member của booking không.
    /// Dùng cho SignalR LobbyHub.JoinBookingGroup authorization.
    /// </summary>
    Task<bool> IsUserBookingParticipantAsync(Guid bookingId, Guid userId);

    /// <summary>
    /// Tìm lobby theo ReservationId — dùng để self-heal orphan reservation khi
    /// Reservation.LobbyId = null nhưng Lobby.ReservationId tồn tại (R-Bug-029).
    /// </summary>
    Task<Lobby?> GetByReservationIdAsync(Guid reservationId);

    Task AddAsync(Lobby lobby);
    Task AddMemberAsync(LobbyMember member);
    Task AddReportAsync(LobbyReport report);

    Task UpdateAsync(Lobby lobby);

    /// <summary>
    /// Hard-delete lobby và toàn bộ records phụ thuộc (members, messages, invites, reports).
    /// Dùng cho dissolve — chỉ host mới được gọi.
    /// </summary>
    Task RemoveAsync(Lobby lobby);

    Task SaveChangesAsync();

    // === Admin: Reports ===
    /// <summary>
    /// Đếm lobby failures theo loại trong khoảng thời gian.
    /// </summary>
    Task<int> CountFailuresByTypeAsync(
        DateTime? fromUtc, DateTime? toUtc,
        LobbyStatus? failureType);
    /// <summary>
    /// Lấy danh sách lobby failures có phân trang.
    /// </summary>
    Task<(IReadOnlyList<Lobby> Items, int TotalCount)> GetAdminLobbyFailuresAsync(
        int page, int pageSize,
        DateTime? fromUtc, DateTime? toUtc,
        LobbyStatus? failureType);
}
}