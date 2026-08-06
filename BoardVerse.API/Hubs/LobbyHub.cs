using System.Security.Claims;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BoardVerse.API.Hubs;

/// <summary>
/// SignalR Hub cho real-time lobby updates.
/// Các events được push đến client khi có thay đổi lobby.
///
/// BR-07: maxMembers constraint được notify real-time
/// BR-08: Lobby timeout notify
/// BR-10: Member join/leave notify theo Karma filter
/// </summary>
[Authorize]
public class LobbyHub : Hub
{
    private readonly ILogger<LobbyHub> _logger;
    private readonly ILobbyRepository _lobbyRepository;

    public LobbyHub(ILogger<LobbyHub> logger, ILobbyRepository lobbyRepository)
    {
        _logger = logger;
        _lobbyRepository = lobbyRepository;
    }

    /// <summary>
    /// Khi user tham gia lobby - join vào SignalR group của lobby đó.
    /// R-Bug-026 Fix: chỉ cho phép nếu user là member của lobby.
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

        await Groups.AddToGroupAsync(Context.ConnectionId, lobbyId.ToString());
        _logger.LogInformation("User {UserId} joined SignalR group for lobby {LobbyId}",
            userId, lobbyId);
    }

    /// <summary>
    /// Khi user rời lobby - leave khỏi SignalR group
    /// </summary>
    public async Task LeaveLobby(Guid lobbyId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, lobbyId.ToString());
        _logger.LogInformation("User {UserId} left SignalR group for lobby {LobbyId}",
            GetUserId(), lobbyId);
    }

    /// <summary>
    /// Subscribe to nearby lobbies (location-based).
    /// Group format uses lat/lng precision F2, radius truncated to 1 decimal.
    /// </summary>
    public async Task SubscribeNearbyLobbies(double latitude, double longitude, double radiusKm)
    {
        if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180 || radiusKm <= 0 || radiusKm > 500)
        {
            throw new HubException("Invalid coordinates or radius.");
        }

        var userId = GetUserId();
        var radiusBucket = Math.Round(radiusKm, 1).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        var group = $"nearby:{latitude.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:{longitude.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:{radiusBucket}";
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        _logger.LogInformation("User {UserId} subscribed to nearby lobbies at ({Lat}, {Lng}) within {Radius}km",
            userId, latitude, longitude, radiusKm);
    }

    /// <summary>
    /// Subscribe to booking events real-time.
    /// Mobile app gọi khi mở BookingDetailPage.
    /// R-Bug-026 Fix: chỉ cho phép nếu user là participant của booking.
    /// </summary>
    public async Task JoinBookingGroup(Guid bookingId)
    {
        var userId = GetUserId();
        var isParticipant = await _lobbyRepository.IsUserBookingParticipantAsync(bookingId, userId);
        if (!isParticipant)
        {
            _logger.LogWarning("IDOR attempt: user {UserId} tried to join booking {BookingId} without participation",
                userId, bookingId);
            throw new HubException(ApiErrorMessages.Jwt.AccessDenied);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"booking-{bookingId}");
        _logger.LogInformation("User {UserId} joined SignalR group for booking {BookingId}",
            userId, bookingId);
    }

    /// <summary>
    /// Unsubscribe khi đóng BookingDetailPage.
    /// </summary>
    public async Task LeaveBookingGroup(Guid bookingId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"booking-{bookingId}");
        _logger.LogInformation("User {UserId} left SignalR group for booking {BookingId}",
            GetUserId(), bookingId);
    }

    /// <summary>
    /// Subscribe to cafe events (task #13: CafePricingChanged).
    /// Mobile app gọi khi mở CafeDetailPage.
    /// Note: cafe events are PUBLIC — anyone can subscribe to a cafe's price changes.
    /// </summary>
    public async Task JoinCafeGroup(Guid cafeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"cafe-{cafeId}");
        _logger.LogInformation("User {UserId} joined SignalR group for cafe {CafeId}",
            GetUserId(), cafeId);
    }

    public async Task LeaveCafeGroup(Guid cafeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"cafe-{cafeId}");
        _logger.LogInformation("User {UserId} left SignalR group for cafe {CafeId}",
            GetUserId(), cafeId);
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