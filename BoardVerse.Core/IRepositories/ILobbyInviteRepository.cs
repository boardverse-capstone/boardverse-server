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
    /// GAP-R6-BJ-FIX Fix: Atomic batch expire invite Pending quá ExpiresAt.
    /// Dùng <c>ExecuteUpdateAsync</c> — cluster-safe, không cần transaction wrap.
    /// Trả về số rows affected.
    /// </summary>
    Task<int> ExpireBatchAsync(DateTime now, int batchSize = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// GAP-R6-BJ-FIX Fix: Lấy distinct LobbyId của các invite vừa expire (Status=Expired, RespondedAt≈now).
    /// Dùng sau <see cref="ExpireBatchAsync"/> để broadcast SignalR update cho từng lobby.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetLobbyIdsForExpiredInvitesAsync(DateTime processedAtWindow, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-LOBBY-INVITE-10: Đếm số invite còn Pending trong ngày của invitee (chống spam nhận).
    /// </summary>
    Task<int> CountPendingByInviteeSinceAsync(Guid inviteeId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-LOBBY-INVITE-10: Đếm số invite đã gửi (cả status) trong ngày của inviter (chống spam gửi).
    /// </summary>
    Task<int> CountSentByInviterSinceAsync(Guid inviterId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 4 (G21): Expire tất cả pending invites của một lobby (khi lobby bị dissolve/absorbed
    /// sau khi merge thành công) và/hoặc pending invites mà một danh sách user là invitee.
    /// Dùng <c>ExecuteUpdateAsync</c> — cluster-safe, không cần transaction wrap.
    /// BR-LOBBY-INVITE-09: Lobby terminal → tất cả pending invite chuyển Expired ngay.
    /// </summary>
    /// <param name="lobbyId">Lobby đã bị dissolve/merge. Pass null để chỉ expire theo users.</param>
    /// <param name="inviteeIds">
    /// Danh sách UserId mà invitee đã được ghép vào lobby khác (không còn pending invite).
    /// Pass null hoặc empty để chỉ expire theo lobbyId.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Số rows affected.</returns>
    Task<int> ExpirePendingForUsersAsync(Guid? lobbyId, IReadOnlyList<Guid>? inviteeIds, CancellationToken cancellationToken = default);

    Task AddAsync(LobbyInvite invite, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}