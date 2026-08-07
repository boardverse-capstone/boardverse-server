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

    public async Task NotifyBookingCheckedIn(Guid bookingId, DateTime checkedInAt, Guid checkedInByUserId)
    {
        await _hubContext.Clients.Group($"booking-{bookingId}").SendAsync("BookingCheckedIn", new
        {
            BookingId = bookingId,
            CheckedInAt = checkedInAt,
            CheckedInBy = checkedInByUserId,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast BookingCheckedIn to booking {BookingId}", bookingId);
    }

    public async Task NotifyBookingCheckedOut(Guid bookingId, DateTime checkedOutAt, decimal totalAmount)
    {
        await _hubContext.Clients.Group($"booking-{bookingId}").SendAsync("BookingCheckedOut", new
        {
            BookingId = bookingId,
            CheckedOutAt = checkedOutAt,
            TotalAmount = totalAmount,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast BookingCheckedOut to booking {BookingId}", bookingId);
    }

    public async Task NotifyBookingCancelled(Guid bookingId, Guid cancelledByUserId, string reason, string refundStatus)
    {
        await _hubContext.Clients.Group($"booking-{bookingId}").SendAsync("BookingCancelled", new
        {
            BookingId = bookingId,
            CancelledBy = cancelledByUserId,
            Reason = reason,
            RefundStatus = refundStatus,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast BookingCancelled to booking {BookingId}: {Reason}", bookingId, reason);
    }

    public async Task NotifyBookingNoShowMarked(Guid bookingId, IReadOnlyList<Guid> noShowMemberIds, IReadOnlyDictionary<Guid, int> karmaDeltas)
    {
        await _hubContext.Clients.Group($"booking-{bookingId}").SendAsync("BookingNoShowMarked", new
        {
            BookingId = bookingId,
            NoShowMemberIds = noShowMemberIds,
            KarmaDeltas = karmaDeltas,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast BookingNoShowMarked to booking {BookingId}: {Count} no-shows", bookingId, noShowMemberIds.Count);
    }

    public async Task NotifyLobbyAutoCancelled(Guid lobbyId, Guid cafeId, string cafeName, DateTime? scheduledTime, string reason)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyAutoCancelled", new
        {
            Type = "LobbyAutoCancelled",
            LobbyId = lobbyId,
            CafeId = cafeId,
            CafeName = cafeName,
            ScheduledTime = scheduledTime,
            Reason = reason,
            Message = "Lobby của bạn đã bị hủy do không đủ người trước giờ hẹn.",
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast LobbyAutoCancelled to lobby {LobbyId}: {Reason}", lobbyId, reason);
    }

    public async Task NotifyCafePricingChanged(Guid cafeId, string cafeName, decimal oldFirstHourPrice, decimal newFirstHourPrice, DateTime effectiveDate, int affectedBookingsCount)
    {
        await _hubContext.Clients.Group($"cafe-{cafeId}").SendAsync("CafePricingChanged", new
        {
            Type = "CafePricingChanged",
            CafeId = cafeId,
            CafeName = cafeName,
            OldFirstHourPrice = oldFirstHourPrice,
            NewFirstHourPrice = newFirstHourPrice,
            EffectiveDate = effectiveDate,
            AffectedBookingsCount = affectedBookingsCount,
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast CafePricingChanged to cafe {CafeId}: {Old} -> {New}, affected {Count}",
            cafeId, oldFirstHourPrice, newFirstHourPrice, affectedBookingsCount);
    }

    public async Task NotifyLobbyActivated(Guid lobbyId, Guid hostUserId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyActivated", new
        {
            LobbyId = lobbyId,
            HostUserId = hostUserId,
            Message = "Lobby đã được tạo thành công. Đang tuyển người chơi!",
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast LobbyActivated to lobby {LobbyId}, host {HostId}", lobbyId, hostUserId);
    }

    public async Task NotifyLobbyConfirmed(Guid lobbyId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyConfirmed", new
        {
            LobbyId = lobbyId,
            Message = "Đã đủ người! Booking đã được xác nhận.",
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast LobbyConfirmed to lobby {LobbyId}", lobbyId);
    }

    public async Task NotifyLobbyCancelled(Guid lobbyId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyCancelled", new
        {
            LobbyId = lobbyId,
            Message = "Lobby đã bị hủy. Tiền cọc đã được xử lý theo chính sách.",
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast LobbyCancelled to lobby {LobbyId}", lobbyId);
    }

    public async Task NotifyLobbyCheckedIn(Guid lobbyId, Guid checkedInByUserId)
    {
        await _hubContext.Clients.Group(lobbyId.ToString()).SendAsync("LobbyCheckedIn", new
        {
            LobbyId = lobbyId,
            CheckedInByUserId = checkedInByUserId,
            Message = "Đã check-in tại quán. Bắt đầu phiên chơi!",
            Timestamp = DateTime.UtcNow
        });
        _logger.LogInformation("Broadcast LobbyCheckedIn to lobby {LobbyId}, checked-in by {UserId}", lobbyId, checkedInByUserId);
    }
}