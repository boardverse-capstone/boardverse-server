using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.SignalR;

namespace BoardVerse.API.Hubs;

/// <summary>
/// Implementation của <see cref="ILobbyHubService"/> dùng SignalR.
/// Inject vào các service để broadcast lobby events real-time.
/// </summary>
public class LobbyHubService : ILobbyHubService
{
    private readonly IHubContext<LobbyHub> _hubContext;
    private readonly ILogger<LobbyHubService> _logger;

    public LobbyHubService(IHubContext<LobbyHub> hubContext, ILogger<LobbyHubService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyMemberJoined(Guid lobbyId, LobbyMemberDto member)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("MemberJoined", new
        {
            LobbyId = lobbyId,
            Member = member,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast MemberJoined to lobby {LobbyId}: {MemberName}", lobbyId, member.UserName);
    }

    public async Task NotifyMemberLeft(Guid lobbyId, Guid memberId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("MemberLeft", new
        {
            LobbyId = lobbyId,
            MemberId = memberId,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast MemberLeft to lobby {LobbyId}: member {MemberId}", lobbyId, memberId);
    }

    public async Task NotifyLobbyFull(Guid lobbyId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyFull", new
        {
            LobbyId = lobbyId,
            Message = "Lobby is now full. Ready for booking.",
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast LobbyFull to lobby {LobbyId}", lobbyId);
    }

    public async Task NotifyLobbyCancelled(Guid lobbyId, string reason)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyCancelled", new
        {
            LobbyId = lobbyId,
            Reason = reason,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast LobbyCancelled to lobby {LobbyId}: {Reason}", lobbyId, reason);
    }

    public async Task NotifyLobbyTimeout(Guid lobbyId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyTimeout", new
        {
            LobbyId = lobbyId,
            Message = "Lobby has timed out due to insufficient members.",
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast LobbyTimeout to lobby {LobbyId}", lobbyId);
    }

    public async Task NotifyBookingConfirmed(Guid lobbyId, Guid bookingId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("BookingConfirmed", new
        {
            LobbyId = lobbyId,
            BookingId = bookingId,
            Message = "Booking confirmed. Proceed to cafe.",
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast BookingConfirmed to lobby {LobbyId}: booking {BookingId}", lobbyId, bookingId);
    }

    public async Task NotifyMemberKicked(Guid lobbyId, Guid userId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("MemberKicked", new
        {
            LobbyId = lobbyId,
            UserId = userId,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast MemberKicked to lobby {LobbyId}: user {UserId}", lobbyId, userId);
    }

    public async Task NotifyMemberReady(Guid lobbyId, Guid userId, bool isReady)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("MemberReady", new
        {
            LobbyId = lobbyId,
            UserId = userId,
            IsReady = isReady,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast MemberReady to lobby {LobbyId}: user {UserId} ready={IsReady}", lobbyId, userId, isReady);
    }

    public async Task NotifyHostChanged(Guid lobbyId, Guid newHostUserId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("HostChanged", new
        {
            LobbyId = lobbyId,
            NewHostUserId = newHostUserId,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast HostChanged to lobby {LobbyId}: new host {UserId}", lobbyId, newHostUserId);
    }

    public async Task NotifyLobbyUpdated(Guid lobbyId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyUpdated", new
        {
            LobbyId = lobbyId,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task NotifyLobbyInProgress(Guid lobbyId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyInProgress", new
        {
            LobbyId = lobbyId,
            Message = "All members ready. Lobby transitioned to InProgress.",
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast LobbyInProgress to lobby {LobbyId}", lobbyId);
    }

    public async Task NotifyMessagePosted(Guid lobbyId, LobbyMessageDto message)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("MessagePosted", message);
        _logger.LogInformation("Broadcast MessagePosted to lobby {LobbyId} from {SenderId}", lobbyId, message.SenderId);
    }
}