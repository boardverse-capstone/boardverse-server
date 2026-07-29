using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BoardVerse.API.Hubs;

/// <summary>
/// SignalR Hub cho real-time POS notifications.
/// Các events được push đến mobile app khi có thay đổi trạng thái session.
///
/// AC 1.4: Phát tín hiệu đồng bộ thông báo cho các thiết bị di động
/// của người chơi để cập nhật trạng thái UI.
/// </summary>
[Authorize]
public class PosHub : Hub
{
    private readonly ILogger<PosHub> _logger;

    public PosHub(ILogger<PosHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to a session to receive real-time updates.
    /// </summary>
    public async Task JoinSession(Guid sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _logger.LogInformation("User {UserId} joined SignalR group for session {SessionId}",
            Context.UserIdentifier, sessionId);
    }

    /// <summary>
    /// Unsubscribe from session updates.
    /// </summary>
    public async Task LeaveSession(Guid sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _logger.LogInformation("User {UserId} left SignalR group for session {SessionId}",
            Context.UserIdentifier, sessionId);
    }

    /// <summary>
    /// Subscribe to user-specific notifications.
    /// </summary>
    public async Task JoinUserNotifications(Guid userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        _logger.LogInformation("User {UserId} subscribed to their own notifications", userId);
    }

    /// <summary>
    /// Subscribe to a lobby's updates.
    /// </summary>
    public async Task JoinLobby(Guid lobbyId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"lobby:{lobbyId}");
        _logger.LogInformation("User {UserId} joined SignalR group for lobby {LobbyId}",
            Context.UserIdentifier, lobbyId);
    }

    /// <summary>
    /// Unsubscribe from lobby updates.
    /// </summary>
    public async Task LeaveLobby(Guid lobbyId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"lobby:{lobbyId}");
        _logger.LogInformation("User {UserId} left SignalR group for lobby {LobbyId}",
            Context.UserIdentifier, lobbyId);
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
