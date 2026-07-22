using BoardVerse.Core.DTOs.Lobby;

namespace BoardVerse.Services.IServices
{
    public interface ILobbyService
    {
        Task<LobbyResponseDto> CreateLobbyAsync(Guid hostUserId, CreateLobbyRequestDto request);
        Task<LobbyResponseDto> JoinLobbyAsync(Guid lobbyId, Guid userId);

        /// <summary>
        /// Join lobby bằng share code (8 ký tự). Áp dụng cho cả public/private lobby.
        /// Private lobby chỉ có thể join qua share code hoặc invite.
        /// </summary>
        Task<LobbyResponseDto> JoinLobbyByShareCodeAsync(string shareCode, Guid userId);

        Task<LobbyResponseDto> LeaveLobbyAsync(Guid lobbyId, Guid userId);

        /// <summary>
        /// Lấy thông tin lobby. Nếu là private lobby, requestingUserId phải là member/host/đã được accept invite.
        /// </summary>
        Task<LobbyResponseDto> GetLobbyAsync(Guid lobbyId, Guid? requestingUserId = null);

        /// <summary>
        /// BR-10: Tìm lobby public theo game + filter địa lý + karma.
        /// </summary>
        Task<IReadOnlyList<LobbyResponseDto>> SearchLobbiesAsync(SearchLobbiesRequestDto request);

        /// <summary>
        /// Lấy danh sách lobby public đang mở (status=Open, IsPrivate=false)
        /// để player khác có thể thấy và join. Hỗ trợ filter optional theo game và khu vực.
        /// </summary>
        /// <param name="gameTemplateId">Optional: chỉ lấy lobby của game này.</param>
        /// <param name="latitude">Optional: latitude user để sort theo khoảng cách.</param>
        /// <param name="longitude">Optional: longitude user.</param>
        /// <param name="radiusKm">Optional: chỉ lấy lobby trong bán kính này (km).</param>
        /// <param name="limit">Số lobby tối đa trả về (default 50).</param>
        Task<IReadOnlyList<LobbyResponseDto>> GetDiscoverableLobbiesAsync(
            Guid? gameTemplateId,
            double? latitude,
            double? longitude,
            double? radiusKm,
            int limit = 50);

        /// <summary>
        /// Host đóng lobby (Closed status). Có thể kèm lý do.
        /// </summary>
        Task<LobbyResponseDto> CloseLobbyAsync(Guid lobbyId, Guid hostUserId, string? reason = null);

        Task<LobbyResponseDto> LockLobbyAsync(Guid lobbyId, Guid hostUserId);
        Task<LobbyResponseDto> OpenKarmaWindowAsync(Guid lobbyId, Guid hostUserId);
        Task<LobbyResponseDto> TransitionToInProgressAsync(Guid lobbyId, Guid? activeSessionId);
        Task<LobbyResponseDto> TransitionToClosedAsync(Guid lobbyId);

        /// <summary>Host chuyển quyền host cho thành viên khác.</summary>
        Task<LobbyResponseDto> TransferHostAsync(Guid lobbyId, Guid currentHostUserId, Guid newHostUserId);

        /// <summary>Host kick thành viên khác khỏi lobby.</summary>
        Task<LobbyResponseDto> KickMemberAsync(Guid lobbyId, Guid hostUserId, Guid targetUserId, string? reason = null);

        /// <summary>Host cập nhật thông tin lobby (description, MaxMembers, v.v.) trước khi start.</summary>
        Task<LobbyResponseDto> UpdateLobbyAsync(Guid lobbyId, Guid hostUserId, UpdateLobbyRequestDto request);

        /// <summary>Member bấm Ready/Unready khi lobby FULL.</summary>
        Task<LobbyResponseDto> SetMemberReadyAsync(Guid lobbyId, Guid userId, bool isReady);

        /// <summary>Lấy danh sách lobby mà user này host.</summary>
        Task<IReadOnlyList<LobbyResponseDto>> GetLobbiesByHostAsync(Guid hostUserId);

        /// <summary>Lấy danh sách lobby mà user đang tham gia.</summary>
        Task<IReadOnlyList<LobbyResponseDto>> GetJoinedLobbiesAsync(Guid userId);

        /// <summary>User report lobby vi phạm.</summary>
        Task<LobbyResponseDto> ReportLobbyAsync(Guid lobbyId, Guid reporterId, CreateLobbyReportDto request);
    }
}