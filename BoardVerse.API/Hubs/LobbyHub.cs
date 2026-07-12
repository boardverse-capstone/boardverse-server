using BoardVerse.Core.DTOs.Lobby;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace BoardVerse.API.Hubs;

/// <summary>
/// SignalR Hub cho real-time lobby updates.
/// Các events được push đến client khi có thay đổi trạng thái lobby.
/// 
/// BR-07: maxMembers constraint được notify real-time
/// BR-08: Lobby timeout notify
/// BR-10: Member join/leave notify theo Karma filter
/// </summary>
[Authorize]
public class LobbyHub : Hub
{
    private readonly ILogger<LobbyHub> _logger;

    public LobbyHub(ILogger<LobbyHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Khi user tham gia lobby - join vào SignalR group của lobby đó
    /// </summary>
    public async Task JoinLobby(Guid lobbyId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, lobbyId.ToString());
        _logger.LogInformation("User {UserId} joined SignalR group for lobby {LobbyId}", 
            Context.UserIdentifier, lobbyId);
    }

    /// <summary>
    /// Khi user rời lobby - leave khỏi SignalR group
    /// </summary>
    public async Task LeaveLobby(Guid lobbyId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, lobbyId.ToString());
        _logger.LogInformation("User {UserId} left SignalR group for lobby {LobbyId}", 
            Context.UserIdentifier, lobbyId);
    }

    /// <summary>
    /// Subscribe to nearby lobbies (location-based)
    /// </summary>
    public async Task SubscribeNearbyLobbies(double latitude, double longitude, double radiusKm)
    {
        var userId = Context.UserIdentifier ?? Context.ConnectionId;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"nearby:{latitude:F2}:{longitude:F2}:{radiusKm}");
        _logger.LogInformation("User {UserId} subscribed to nearby lobbies at ({Lat}, {Lng}) within {Radius}km",
            userId, latitude, longitude, radiusKm);
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier ?? "anonymous";
        _logger.LogInformation("Client connected: {ConnectionId} for user {UserId}", 
            Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier ?? "anonymous";
        _logger.LogInformation("Client disconnected: {ConnectionId} for user {UserId}. Exception: {Exception}",
            Context.ConnectionId, userId, exception?.Message ?? "none");
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Server-side service để broadcast lobby events
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

/// <summary>
/// Implementation của LobbyHubService
/// Inject vào services để broadcast events
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
}
