using BoardVerse.Core.DTOs.Lobby;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Realtime broadcaster cho lobby events. Implementation lives in BoardVerse.API (SignalR).
/// Service layer chỉ depend vào interface để tránh tham chiếu ngược API.
/// </summary>
public interface ILobbyHubService
{
    Task NotifyMemberJoined(Guid lobbyId, LobbyMemberDto member);
    Task NotifyMemberLeft(Guid lobbyId, Guid memberId);
    Task NotifyMemberKicked(Guid lobbyId, Guid userId);
    Task NotifyMemberReady(Guid lobbyId, Guid userId, bool isReady);
    Task NotifyHostChanged(Guid lobbyId, Guid newHostUserId);
    Task NotifyLobbyUpdated(Guid lobbyId);
    Task NotifyLobbyInProgress(Guid lobbyId);
    Task NotifyLobbyFull(Guid lobbyId);
    Task NotifyLobbyCancelled(Guid lobbyId, string reason);
    Task NotifyLobbyTimeout(Guid lobbyId);
    Task NotifyBookingConfirmed(Guid lobbyId, Guid bookingId);
    Task NotifyMessagePosted(Guid lobbyId, LobbyMessageDto message);

    /// <summary>Broadcast sau POST /bookings/{id}/check-in (Staff) — task #7.</summary>
    Task NotifyBookingCheckedIn(Guid bookingId, DateTime checkedInAt, Guid checkedInByUserId);

    /// <summary>Broadcast sau POST /bookings/{id}/check-out (Staff) — task #7.</summary>
    Task NotifyBookingCheckedOut(Guid bookingId, DateTime checkedOutAt, decimal totalAmount);

    /// <summary>Broadcast sau DELETE /bookings/{id} hoặc manager cancel — task #7.</summary>
    Task NotifyBookingCancelled(Guid bookingId, Guid cancelledByUserId, string reason, string refundStatus);

    /// <summary>Broadcast sau khi Staff check-out + aggregate no-show votes — task #7.</summary>
    Task NotifyBookingNoShowMarked(Guid bookingId, IReadOnlyList<Guid> noShowMemberIds, IReadOnlyDictionary<Guid, int> karmaDeltas);

    /// <summary>Mobile task #9: Broadcast lobby auto-cancel với payload chi tiết (cafeName, scheduledTime, reason).</summary>
    Task NotifyLobbyAutoCancelled(Guid lobbyId, Guid cafeId, string cafeName, DateTime? scheduledTime, string reason);

    /// <summary>Mobile task #13: Broadcast CafePricingChanged (BR-04).</summary>
    Task NotifyCafePricingChanged(Guid cafeId, string cafeName, decimal oldFirstHourPrice, decimal newFirstHourPrice, DateTime effectiveDate, int affectedBookingsCount);

    /// <summary>BR-REQUIRED §17.5: Lobby đã được tạo thành công + atomic hold BVC/seat/game.</summary>
    Task NotifyLobbyActivated(Guid lobbyId, Guid hostUserId);

    /// <summary>BR-REQUIRED §17.5: Lobby đạt minPlayers → booking confirmed.</summary>
    Task NotifyLobbyConfirmed(Guid lobbyId);

    /// <summary>Tất cả members ready → lobby đang chờ check-in tại quán.</summary>
    Task NotifyLobbyWaitingCheckIn(Guid lobbyId);

    /// <summary>BR-REQUIRED §17.5: Lobby bị hủy (host/cafe/no-show/timeout).</summary>
    Task NotifyLobbyCancelled(Guid lobbyId);

    /// <summary>BR-REQUIRED §17.5: Lobby đã check-in tại quán.</summary>
    Task NotifyLobbyCheckedIn(Guid lobbyId, Guid checkedInByUserId);

    // === Phase 4: Lobby Merge ===

    /// <summary>
    /// Phase 4 (G19): Lobby nguồn (Nhóm A) bị hấp thu vào lobby đích (Nhóm B) sau khi merge duyệt.
    /// Broadcast đến SignalR group của lobby nguồn để client biết lobby đã bị dissolve/absorbed.
    /// </summary>
    /// <param name="sourceLobbyId">Lobby nguồn (Nhóm A) đã bị hấp thu.</param>
    /// <param name="targetLobbyId">Lobby đích (Nhóm B) mà lobby nguồn được ghép vào.</param>
    /// <param name="mergeRequestId">Mã yêu cầu ghép đã duyệt.</param>
    /// <param name="membersTransferred">Số member đã chuyển.</param>
    Task NotifyLobbyMergedInto(Guid sourceLobbyId, Guid targetLobbyId, Guid mergeRequestId, int membersTransferred);

    /// <summary>
    /// Phase 4 (G20): Thành viên mới gia nhập lobby đích (Nhóm B) từ merge.
    /// Broadcast đến SignalR group của lobby đích để client biết member mới từ Nhóm A.
    /// </summary>
    /// <param name="targetLobbyId">Lobby đích (Nhóm B) nhận member mới.</param>
    /// <param name="memberUserIds">Danh sách UserId của các member vừa được ghép.</param>
    /// <param name="sourceLobbyId">Lobby nguồn (Nhóm A) mà các member rời khỏi.</param>
    /// <param name="mergeRequestId">Mã yêu cầu ghép đã duyệt.</param>
    Task NotifyMemberJoinedFromMerge(Guid targetLobbyId, IReadOnlyList<Guid> memberUserIds, Guid sourceLobbyId, Guid mergeRequestId);
}