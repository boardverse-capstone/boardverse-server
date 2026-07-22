using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
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

        Task<BookingDeposit?> GetBookingByIdAsync(Guid bookingId);

        Task AddAsync(Lobby lobby);
        Task AddMemberAsync(LobbyMember member);
        Task AddReportAsync(LobbyReport report);

        Task SaveChangesAsync();
    }
}