using System.Security.Claims;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
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
    private readonly ILobbyRepository _lobbyRepository;
    private readonly IActiveSessionRepository _activeSessionRepository;

    public PosHub(
        ILogger<PosHub> logger,
        ILobbyRepository lobbyRepository,
        IActiveSessionRepository activeSessionRepository)
    {
        _logger = logger;
        _lobbyRepository = lobbyRepository;
        _activeSessionRepository = activeSessionRepository;
    }

    /// <summary>
    /// Subscribe to a session to receive real-time updates.
    /// R-Bug-026 Fix: verify user is part of the session (host, member, or check-in staff).
    /// </summary>
    public async Task JoinSession(Guid sessionId)
    {
        var userId = GetUserId();
        var isParticipant = await _activeSessionRepository.IsUserSessionParticipantAsync(sessionId, userId);
        if (!isParticipant)
        {
            _logger.LogWarning("IDOR attempt: user {UserId} tried to join session {SessionId} without membership",
                userId, sessionId);
            throw new HubException(ApiErrorMessages.Jwt.AccessDenied);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _logger.LogInformation("User {UserId} joined SignalR group for session {SessionId}",
            userId, sessionId);
    }

    /// <summary>
    /// Unsubscribe from session updates.
    /// </summary>
    public async Task LeaveSession(Guid sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _logger.LogInformation("User {UserId} left SignalR group for session {SessionId}",
            GetUserId(), sessionId);
    }

    /// <summary>
    /// Subscribe to user-specific notifications.
    /// R-Bug-026 Fix: user can only subscribe to their own notifications (prevent IDOR).
    /// </summary>
    public async Task JoinUserNotifications(Guid userId)
    {
        var currentUserId = GetUserId();
        if (currentUserId != userId)
        {
            _logger.LogWarning("IDOR attempt: user {CurrentUserId} tried to subscribe to user {TargetUserId} notifications",
                currentUserId, userId);
            throw new HubException(ApiErrorMessages.Jwt.AccessDenied);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        _logger.LogInformation("User {UserId} subscribed to their own notifications", userId);
    }

    /// <summary>
    /// Subscribe to a lobby's updates.
    /// R-Bug-026 Fix: verify user is a member of the lobby.
    /// </summary>
    public async Task JoinLobby(Guid lobbyId)
    {
        var userId = GetUserId();
        var isMember = await _lobbyRepository.IsUserLobbyMemberAsync(lobbyId, userId);
        if (!isMember)
        {
            _logger.LogWarning("IDOR attempt: user {UserId} tried to join lobby {LobbyId} without membership",
                userId, lobbyId);
            throw new HubException(ApiErrorMessages.Jwt.AccessDenied);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"lobby:{lobbyId}");
        _logger.LogInformation("User {UserId} joined SignalR group for lobby {LobbyId}",
            userId, lobbyId);
    }

    /// <summary>
    /// Unsubscribe from lobby updates.
    /// </summary>
    public async Task LeaveLobby(Guid lobbyId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"lobby:{lobbyId}");
        _logger.LogInformation("User {UserId} left SignalR group for lobby {LobbyId}",
            GetUserId(), lobbyId);
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

    private Guid GetUserId()
    {
        var claim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(claim, out var userId))
        {
            throw new HubException(ApiErrorMessages.Jwt.AuthenticationFailed);
        }
        return userId;
    }
}