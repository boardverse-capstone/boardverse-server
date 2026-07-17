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
    Task NotifyLobbyFull(Guid lobbyId);
    Task NotifyLobbyCancelled(Guid lobbyId, string reason);
    Task NotifyLobbyTimeout(Guid lobbyId);
    Task NotifyBookingConfirmed(Guid lobbyId, Guid bookingId);
}