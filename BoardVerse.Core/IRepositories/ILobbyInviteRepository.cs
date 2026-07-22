using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho lời mời tham gia lobby.
/// </summary>
public interface ILobbyInviteRepository
{
    Task<LobbyInvite?> GetByIdAsync(Guid id);

    Task<LobbyInvite?> GetPendingInviteAsync(Guid lobbyId, Guid inviteeId);

    /// <summary>
    /// Lấy invite đã được Accept cho (lobby, invitee). Dùng để check quyền join private lobby.
    /// </summary>
    Task<LobbyInvite?> GetAcceptedInviteAsync(Guid lobbyId, Guid inviteeId);

    Task<IReadOnlyList<LobbyInvite>> GetByLobbyAsync(Guid lobbyId, LobbyInviteStatus? status = null);

    Task<IReadOnlyList<LobbyInvite>> GetPendingByInviteeAsync(Guid inviteeId);

    Task<IReadOnlyList<LobbyInvite>> GetAllByInviteeAsync(Guid inviteeId, LobbyInviteStatus? status = null);

    /// <summary>
    /// Hủy tất cả Pending invite giữa inviter và invitee (cả 2 chiều). Dùng khi unfriend.
    /// </summary>
    Task<IReadOnlyList<LobbyInvite>> CancelPendingBetweenAsync(Guid userAId, Guid userBId);

    /// <summary>
    /// Hủy tất cả Pending invite của một lobby (khi lobby bị đóng/hủy).
    /// </summary>
    Task<int> CancelAllPendingForLobbyAsync(Guid lobbyId);

    /// <summary>
    /// Hủy pending invite của một invitee cho một lobby cụ thể (khi user đã join lobby).
    /// </summary>
    Task<int> CancelPendingForLobbyAndInviteeAsync(Guid lobbyId, Guid inviteeId);

    /// <summary>
    /// Auto-expire các invite quá ExpiresAt nhưng chưa được đánh dấu Expired.
    /// </summary>
    Task<IReadOnlyList<LobbyInvite>> GetExpiredPendingAsync(DateTime now);

    Task AddAsync(LobbyInvite invite);
    Task SaveChangesAsync();
}