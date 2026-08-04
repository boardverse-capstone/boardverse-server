using BoardVerse.Core.Entities;

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
}
}