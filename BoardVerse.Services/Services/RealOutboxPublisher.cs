using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BoardVerse.Services.Services;

/// <summary>
/// BR-REQUIRED §17.5: Real implementation của IOutboxEventPublisher.
/// Dispatch events to SignalR hubs + FCM push notifications.
/// </summary>
public class RealOutboxPublisher : IOutboxEventPublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RealOutboxPublisher> _logger;

    public RealOutboxPublisher(IServiceProvider serviceProvider, ILogger<RealOutboxPublisher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        switch (outboxEvent.EventType)
        {
            case OutboxEventType.LobbyActivated:
                await PublishLobbyActivatedAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.LobbyCheckedIn:
                await PublishLobbyCheckedInAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.SessionCompleted:
                await PublishSessionCompletedAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.LobbyTimeout:
                await PublishLobbyTimeoutAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.LobbyConfirmed:
                await PublishLobbyConfirmedAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.ReservationHeld:
                await PublishReservationHeldAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.DepositHeld:
                await PublishDepositHeldAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.DepositReleased:
                await PublishDepositReleasedAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.DepositCaptured:
                await PublishDepositCapturedAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.LobbyCancelledByHost:
                await PublishLobbyCancelledByHostAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.LobbyCancelledByCafe:
                await PublishLobbyCancelledByCafeAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.LobbyRejectedByCafe:
                await PublishLobbyRejectedByCafeAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.LobbyExpiredByCafe:
                await PublishLobbyExpiredByCafeAsync(scope, outboxEvent, cancellationToken);
                break;

            case OutboxEventType.LobbyNoShow:
                await PublishLobbyNoShowAsync(scope, outboxEvent, cancellationToken);
                break;

            default:
                _logger.LogWarning(
                    "[Outbox] Unknown event type {EventType} for {EventId}. No handler.",
                    outboxEvent.EventType, outboxEvent.Id);
                break;
        }

        _logger.LogInformation(
            "[Outbox] Published {EventType} for UserId={UserId}. IdempotencyKey={Key}",
            outboxEvent.EventType, outboxEvent.UserId, outboxEvent.IdempotencyKey);
    }

    private async Task PublishLobbyActivatedAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var lobbyHub = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        if (evt.LobbyId.HasValue)
        {
            await lobbyHub.NotifyLobbyActivated(evt.LobbyId.Value, evt.UserId ?? Guid.Empty);
        }

        if (evt.UserId.HasValue)
        {
            await pushService.SendAsync(evt.UserId.Value, "Phòng chờ đã được tạo",
                "Lobby của bạn đã sẵn sàng tuyển người!", new Dictionary<string, string>
                {
                    ["lobbyId"] = evt.LobbyId?.ToString() ?? "",
                    ["event"] = "LobbyActivated"
                });
        }
    }

    private async Task PublishLobbyCheckedInAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var lobbyHub = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        if (evt.LobbyId.HasValue)
        {
            await lobbyHub.NotifyLobbyCheckedIn(evt.LobbyId.Value, evt.UserId ?? Guid.Empty);
        }

        var payload = ParsePayload(evt.Payload);
        var cafeName = payload.GetValueOrDefault("cafeName") ?? "quán";

        if (evt.UserId.HasValue)
        {
            await pushService.SendAsync(evt.UserId.Value, "Đã check-in tại quán",
                $"Bạn đã check-in tại {cafeName}. Chúc các bạn chơi vui vẻ!",
                new Dictionary<string, string>
                {
                    ["lobbyId"] = evt.LobbyId?.ToString() ?? "",
                    ["event"] = "LobbyCheckedIn"
                });
        }
    }

    private async Task PublishSessionCompletedAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var posHub = scope.ServiceProvider.GetRequiredService<IPosHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        var payload = ParsePayload(evt.Payload);

        // GAP-XX Fix: Đọc sessionId từ payload JSON (ReservationService ghi "activeSessionId",
        // ActiveSessionService ghi "sessionId"). MapSignalR theo sessionId — group session:{sessionId}
        // là group PosHub.JoinSession tạo ra → FE subscribe group này nhận SessionPaid event.
        //
        // Vấn đề cũ: code dưới đây dùng evt.LobbyId cho group "session:{lobbyId}" — SAI vì:
        //   1. Group PosHub.JoinSession dùng SESSION id, không phải lobby id.
        //   2. Walk-in session (LobbyId = null) miss hoàn toàn → không ai nhận SignalR.
        //   3. Lobby:Session 1:N có thể có nhiều session liên kết 1 lobby → broadcast nhầm.
        Guid? sessionId = null;
        if (payload.TryGetValue("sessionId", out var sid) && Guid.TryParse(sid, out var parsedSessionId))
        {
            sessionId = parsedSessionId;
        }
        else if (payload.TryGetValue("activeSessionId", out var aid) && Guid.TryParse(aid, out var parsedActiveSessionId))
        {
            sessionId = parsedActiveSessionId;
        }

        // Notify qua session group (preferred) + legacy lobby group (back-compat cho lobby có FE cũ).
        if (sessionId.HasValue)
        {
            // Đọc thêm cafeId, totalAmount, paidAt từ payload cho SignalR notification.
            Guid? cafeId = null;
            if (payload.TryGetValue("cafeId", out var cid) && Guid.TryParse(cid, out var parsedCafeId))
            {
                cafeId = parsedCafeId;
            }
            decimal totalAmount = 0m;
            if (payload.TryGetValue("totalAmount", out var ta) && decimal.TryParse(ta, out var parsedTotal))
            {
                totalAmount = parsedTotal;
            }
            DateTime paidAt = DateTime.UtcNow;
            if (payload.TryGetValue("paidAt", out var pa) && DateTime.TryParse(pa, out var parsedPaidAt))
            {
                paidAt = parsedPaidAt;
            }

            await posHub.NotifySessionPaidAsync(
                sessionId.Value,
                cafeId ?? Guid.Empty,
                evt.LobbyId,
                totalAmount,
                paidAt);
        }
        else if (evt.LobbyId.HasValue)
        {
            // Fallback: chỉ có lobbyId (event cũ chưa có sessionId trong payload) → dùng
            // NotifySessionCompleted cũ (group session:{lobbyId} sai tên nhưng đã có FE subscribe).
            await posHub.NotifySessionCompleted(evt.LobbyId.Value);
        }
        else
        {
            _logger.LogWarning(
                "[Outbox] SessionCompleted event {EventId} không có sessionId/activeSessionId trong payload " +
                "và không có LobbyId → skip SignalR push. EventType={EventType}",
                evt.Id, evt.EventType);
        }

        // Push notification (giữ nguyên logic cũ — chỉ push khi có UserId).
        if (evt.UserId.HasValue)
        {
                await pushService.SendAsync(evt.UserId.Value, "Phiên chơi đã kết thúc",
                    "Cảm ơn bạn đã sử dụng BoardVerse! Hãy đánh giá các thành viên nhé.",
                new Dictionary<string, string>
                {
                    ["sessionId"] = sessionId?.ToString() ?? evt.LobbyId?.ToString() ?? "",
                    ["event"] = "SessionCompleted"
                });
        }
    }

    private async Task PublishLobbyTimeoutAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var lobbyHub = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        if (evt.LobbyId.HasValue)
        {
            await lobbyHub.NotifyLobbyTimeout(evt.LobbyId.Value);
        }

        if (evt.UserId.HasValue)
        {
            await pushService.SendAsync(evt.UserId.Value, "Phòng chờ đã hết hạn",
                "Phòng chờ của bạn đã hết hạn tuyển người. Tiền cọc đã được hoàn.",
                new Dictionary<string, string>
                {
                    ["lobbyId"] = evt.LobbyId?.ToString() ?? "",
                    ["event"] = "LobbyTimeout"
                });
        }
    }

    private async Task PublishLobbyConfirmedAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var lobbyHub = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        if (evt.LobbyId.HasValue)
        {
            await lobbyHub.NotifyLobbyConfirmed(evt.LobbyId.Value);
        }

        if (evt.UserId.HasValue)
        {
            await pushService.SendAsync(evt.UserId.Value, "Phòng chờ đã xác nhận",
                "Đã đủ người! Hãy đến quán đúng giờ nhé.",
                new Dictionary<string, string>
                {
                    ["lobbyId"] = evt.LobbyId?.ToString() ?? "",
                    ["event"] = "LobbyConfirmed"
                });
        }
    }

    private async Task PublishReservationHeldAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        if (evt.UserId.HasValue)
        {
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushService.SendAsync(evt.UserId.Value, "Đặt chỗ thành công",
                "Chỗ của bạn đã được giữ. Sẵn sàng tuyển người!",
                new Dictionary<string, string>
                {
                    ["reservationId"] = evt.ReservationId?.ToString() ?? "",
                    ["event"] = "ReservationHeld"
                });
        }
    }

    private async Task PublishDepositHeldAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var payload = ParsePayload(evt.Payload);
        var amount = payload.GetValueOrDefault("amount") ?? "0";

        if (evt.UserId.HasValue)
        {
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushService.SendAsync(evt.UserId.Value, "Đã giữ tiền cọc",
                $"Đã giữ {amount} BVC cho đặt chỗ. Sẽ hoàn khi kết thúc.",
                new Dictionary<string, string>
                {
                    ["event"] = "DepositHeld"
                });
        }
    }

    private async Task PublishDepositReleasedAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var payload = ParsePayload(evt.Payload);
        var amount = payload.GetValueOrDefault("amount") ?? "0";

        if (evt.UserId.HasValue)
        {
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushService.SendAsync(evt.UserId.Value, "Đã hoàn tiền cọc",
                $"{amount} BVC đã được hoàn vào ví của bạn.",
                new Dictionary<string, string>
                {
                    ["event"] = "DepositReleased"
                });
        }
    }

    private async Task PublishDepositCapturedAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        _logger.LogDebug("[Outbox] DepositCaptured event {EventId} processed (no push needed)", evt.Id);
    }

    private async Task PublishLobbyCancelledByHostAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var lobbyHub = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        if (evt.LobbyId.HasValue)
        {
            await lobbyHub.NotifyLobbyCancelled(evt.LobbyId.Value);
        }

        if (evt.UserId.HasValue)
        {
            await pushService.SendAsync(evt.UserId.Value, "Phòng chờ đã bị hủy",
                "Host đã hủy phòng chờ. Tiền cọc đã được xử lý theo chính sách.",
                new Dictionary<string, string>
                {
                    ["lobbyId"] = evt.LobbyId?.ToString() ?? "",
                    ["event"] = "LobbyCancelledByHost"
                });
        }
    }

    private async Task PublishLobbyCancelledByCafeAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var lobbyHub = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        if (evt.LobbyId.HasValue)
        {
            await lobbyHub.NotifyLobbyCancelled(evt.LobbyId.Value);
        }

        if (evt.UserId.HasValue)
        {
            await pushService.SendAsync(evt.UserId.Value, "Phòng chờ đã bị hủy",
                "Quán đã hủy phòng chờ. Tiền cọc đã được hoàn 100%.",
                new Dictionary<string, string>
                {
                    ["lobbyId"] = evt.LobbyId?.ToString() ?? "",
                    ["event"] = "LobbyCancelledByCafe"
                });
        }
    }

    private async Task PublishLobbyRejectedByCafeAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var lobbyHub = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
        var payload = ParsePayload(evt.Payload);

        if (evt.LobbyId.HasValue)
        {
            await lobbyHub.NotifyLobbyAutoCancelled(
                evt.LobbyId.Value,
                Guid.TryParse(payload.GetValueOrDefault("cafeId"), out var cafeId) ? cafeId : Guid.Empty,
                payload.GetValueOrDefault("cafeName") ?? "Quán",
                null,
                "Quán từ chối duyệt phòng chờ.");
        }

        if (evt.UserId.HasValue)
        {
            await pushService.SendAsync(evt.UserId.Value, "Phòng chờ bị từ chối",
                "Quán đã từ chối duyệt phòng chờ. Tiền cọc đã được hoàn.",
                new Dictionary<string, string>
                {
                    ["lobbyId"] = evt.LobbyId?.ToString() ?? "",
                    ["event"] = "LobbyRejectedByCafe"
                });
        }
    }

    private async Task PublishLobbyExpiredByCafeAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var lobbyHub = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
        var payload = ParsePayload(evt.Payload);

        if (evt.LobbyId.HasValue)
        {
            await lobbyHub.NotifyLobbyAutoCancelled(
                evt.LobbyId.Value,
                Guid.TryParse(payload.GetValueOrDefault("cafeId"), out var cafeId) ? cafeId : Guid.Empty,
                payload.GetValueOrDefault("cafeName") ?? "Quán",
                null,
                "Quán không duyệt trong 24 giờ.");
        }

        if (evt.UserId.HasValue)
        {
            await pushService.SendAsync(evt.UserId.Value, "Phòng chờ đã hết hạn",
                "Quán không phản hồi trong 24 giờ. Tiền cọc đã được hoàn.",
                new Dictionary<string, string>
                {
                    ["lobbyId"] = evt.LobbyId?.ToString() ?? "",
                    ["event"] = "LobbyExpiredByCafe"
                });
        }
    }

    private async Task PublishLobbyNoShowAsync(IServiceScope scope, OutboxEvent evt, CancellationToken ct)
    {
        var lobbyHub = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        if (evt.LobbyId.HasValue)
        {
            await lobbyHub.NotifyLobbyCancelled(evt.LobbyId.Value);
        }

        if (evt.UserId.HasValue)
        {
            await pushService.SendAsync(evt.UserId.Value, "No-show được ghi nhận",
                "Bạn không check-in đúng giờ. Tiền cọc đã bị tịch thu.",
                new Dictionary<string, string>
                {
                    ["lobbyId"] = evt.LobbyId?.ToString() ?? "",
                    ["event"] = "LobbyNoShow"
                });
        }
    }

    private static Dictionary<string, string> ParsePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(payload)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}