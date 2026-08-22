using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho lời mời tham gia lobby.
/// </summary>
public interface ILobbyInviteRepository
{
    Task<LobbyInvite?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LobbyInvite?> GetPendingInviteAsync(Guid lobbyId, Guid inviteeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy invite đã được Accept cho (lobby, invitee). Dùng để check quyền join private lobby.
    /// </summary>
    Task<LobbyInvite?> GetAcceptedInviteAsync(Guid lobbyId, Guid inviteeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LobbyInvite>> GetByLobbyAsync(Guid lobbyId, LobbyInviteStatus? status = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LobbyInvite>> GetPendingByInviteeAsync(Guid inviteeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LobbyInvite>> GetAllByInviteeAsync(Guid inviteeId, LobbyInviteStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy tất cả Pending invite giữa inviter và invitee (cả 2 chiều). Dùng khi unfriend.
    /// </summary>
    Task<IReadOnlyList<LobbyInvite>> CancelPendingBetweenAsync(Guid userAId, Guid userBId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy tất cả Pending invite của một lobby (khi lobby bị đóng/hủy).
    /// </summary>
    Task<int> CancelAllPendingForLobbyAsync(Guid lobbyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy pending invite của một invitee cho một lobby cụ thể (khi user đã join lobby).
    /// </summary>
    Task<int> CancelPendingForLobbyAndInviteeAsync(Guid lobbyId, Guid inviteeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Auto-expire các invite quá ExpiresAt nhưng chưa được đánh dấu Expired.
    /// </summary>
    Task<IReadOnlyList<LobbyInvite>> GetExpiredPendingAsync(DateTime now, int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-LOBBY-INVITE-10: Đếm số invite còn Pending trong ngày của invitee (chống spam nhận).
    /// </summary>
    Task<int> CountPendingByInviteeSinceAsync(Guid inviteeId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-LOBBY-INVITE-10: Đếm số invite đã gửi (cả status) trong ngày của inviter (chống spam gửi).
    /// </summary>
    Task<int> CountSentByInviterSinceAsync(Guid inviterId, DateTime since, CancellationToken cancellationToken = default);

    Task AddAsync(LobbyInvite invite, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}