using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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