using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface ILobbyService
    {
        Task<LobbyResponseDto> CreateLobbyAsync(Guid hostUserId, CreateLobbyRequestDto request, CancellationToken cancellationToken = default);
        Task<LobbyResponseDto> JoinLobbyAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Join lobby bằng share code (8 ký tự). Áp dụng cho cả public/private lobby.
        /// Private lobby chỉ có thể join qua share code hoặc invite.
        /// </summary>
        Task<LobbyResponseDto> JoinLobbyByShareCodeAsync(string shareCode, Guid userId, CancellationToken cancellationToken = default);

        Task<LobbyResponseDto> LeaveLobbyAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy thông tin lobby. Nếu là private lobby, requestingUserId phải là member/host/đã được accept invite.
        /// </summary>
        Task<LobbyResponseDto> GetLobbyAsync(Guid lobbyId, Guid? requestingUserId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// BR-10: Tìm lobby public theo game + filter địa lý + karma.
        /// BR-USER-LIMIT-02: Nếu userId != null và ExcludeSelfOverlapping = true, loại bỏ các lobby trùng lịch.
        /// </summary>
        Task<IReadOnlyList<LobbyResponseDto>> SearchLobbiesAsync(SearchLobbiesRequestDto request, Guid? requestingUserId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy danh sách lobby public đang mở (status=Open, IsPrivate=false)
        /// để player khác có thể thấy và join. Hỗ trợ filter optional theo game và khu vực.
        /// BR-USER-LIMIT-02: Nếu userId != null và ExcludeSelfOverlapping = true, loại bỏ các lobby trùng lịch.
        /// </summary>
        /// <param name="gameTemplateId">Optional: chỉ lấy lobby của game này.</param>
        /// <param name="latitude">Optional: latitude user để sort theo khoảng cách.</param>
        /// <param name="longitude">Optional: longitude user.</param>
        /// <param name="radiusKm">Optional: chỉ lấy lobby trong bán kính này (km).</param>
        /// <param name="limit">Số lobby tối đa trả về (default 50).</param>
        /// <param name="requestingUserId">UserId để filter overlapping (BR-USER-LIMIT-02).</param>
        Task<IReadOnlyList<LobbyResponseDto>> GetDiscoverableLobbiesAsync(
            Guid? gameTemplateId,
            double? latitude,
            double? longitude,
            double? radiusKm,
            int limit = 50,
            Guid? requestingUserId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Host đóng lobby (Closed status). Có thể kèm lý do.
        /// </summary>
        Task<LobbyResponseDto> CloseLobbyAsync(Guid lobbyId, Guid hostUserId, string? reason = null);

        /// <summary>
        /// Host giải tán lobby — soft delete (row vẫn còn để audit + risk signals).
        /// Tính refund BVC theo BR-REFUND-02/03 (grace 15p / 24h / 6h) + giải phóng SeatInventory/GameInventory.
        /// Chỉ áp dụng khi lobby chưa check-in tại quán.
        /// </summary>
        Task<DissolveLobbyResponseDto> DissolveLobbyAsync(Guid lobbyId, Guid hostUserId, string? reason = null, CancellationToken cancellationToken = default);

        Task<LobbyResponseDto> LockLobbyAsync(Guid lobbyId, Guid hostUserId, CancellationToken cancellationToken = default);
        Task<LobbyResponseDto> OpenKarmaWindowAsync(Guid lobbyId, Guid hostUserId, CancellationToken cancellationToken = default);
        Task<LobbyResponseDto> TransitionToInProgressAsync(Guid lobbyId, Guid? activeSessionId, CancellationToken cancellationToken = default);
        Task<LobbyResponseDto> TransitionToClosedAsync(Guid lobbyId, CancellationToken cancellationToken = default);

        /// <summary>Host chuyển quyền host cho thành viên khác.</summary>
        Task<LobbyResponseDto> TransferHostAsync(Guid lobbyId, Guid currentHostUserId, Guid newHostUserId, CancellationToken cancellationToken = default);

        /// <summary>L-03: Host tạo mã chia sẻ mới, invalidate mã cũ.</summary>
        Task<LobbyResponseDto> RegenerateShareCodeAsync(Guid lobbyId, Guid hostUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// BR-NEW-14 (b): Host đổi timeSlot và/hoặc preferred times của lobby.
        /// Chỉ áp dụng khi lobby chưa check-in (status = Open/Viable/Full/PendingCafeApproval).
        /// Update cả Reservation.TimeSlot + Lobby.TimeSlot (mirror) + recalculate RecruitmentDeadline.
        /// BR-RES-07/08/09: preferredStartTime/EndTime phải nằm trong slot range.
        /// </summary>
        Task<LobbyResponseDto> ChangeTimeAsync(Guid lobbyId, Guid hostUserId, Core.DTOs.Lobby.ChangeTimeSlotRequestDto request, CancellationToken cancellationToken = default);

        /// <summary>
        /// BR-NEW-14 (d): Boost lobby — tăng visibility trong search/discovery.
        /// Chỉ áp dụng khi lobby đang Open và chưa được boost trong 6 giờ gần nhất.
        /// </summary>
        Task<LobbyResponseDto> BoostLobbyAsync(Guid lobbyId, Guid hostUserId, CancellationToken cancellationToken = default);

        /// <summary>Host kick thành viên khác khỏi lobby.</summary>
        Task<LobbyResponseDto> KickMemberAsync(Guid lobbyId, Guid hostUserId, Guid targetUserId, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>Host cập nhật thông tin lobby (description, MaxMembers, v.v.) trước khi start.</summary>
        Task<LobbyResponseDto> UpdateLobbyAsync(Guid lobbyId, Guid hostUserId, UpdateLobbyRequestDto request, CancellationToken cancellationToken = default);

        /// <summary>Member bấm Ready/Unready khi lobby FULL.</summary>
        Task<LobbyResponseDto> SetMemberReadyAsync(Guid lobbyId, Guid userId, bool isReady, CancellationToken cancellationToken = default);

        /// <summary>Lấy danh sách lobby mà user này host.</summary>
        Task<IReadOnlyList<LobbyResponseDto>> GetLobbiesByHostAsync(Guid hostUserId, CancellationToken cancellationToken = default);

        /// <summary>Lấy tất cả lobby của user (host hoặc member, active).</summary>
        Task<IReadOnlyList<LobbyResponseDto>> GetMyLobbiesAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>User report lobby vi phạm.</summary>
        Task<LobbyResponseDto> ReportLobbyAsync(Guid lobbyId, Guid reporterId, CreateLobbyReportDto request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy danh sách lobby của 1 cafe cho Manager.
        /// Filter theo status, playDate, có phân trang.
        /// </summary>
        Task<CafeLobbiesResponseDto> GetCafeLobbiesAsync(
            Guid cafeManagerUserId,
            Guid cafeId,
            CafeLobbiesRequestDto request, CancellationToken cancellationToken = default);
    }
}