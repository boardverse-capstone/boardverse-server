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
}