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
}