using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho RealOutboxPublisher — BR-REQUIRED §17.5 (Transactional Outbox).
/// Verify event dispatch đến đúng handler theo EventType + unknown type skip + payload parsing.
/// </summary>
public class RealOutboxPublisherTests
{
    private static OutboxEvent BuildEvent(
        OutboxEventType type,
        Guid? lobbyId = null,
        Guid? userId = null,
        string payload = "{}") => new()
    {
        Id = Guid.NewGuid(),
        EventType = type,
        LobbyId = lobbyId,
        UserId = userId,
        Payload = payload,
        IdempotencyKey = $"k-{Guid.NewGuid()}",
        CreatedAt = DateTime.UtcNow
    };

    private static OutboxEvent BuildSessionCompletedWithSessionId(Guid sessionId, Guid cafeId, decimal totalAmount)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            sessionId = sessionId.ToString(),
            cafeId = cafeId.ToString(),
            totalAmount = totalAmount,
            paidAt = DateTime.UtcNow
        });
        return BuildEvent(OutboxEventType.SessionCompleted, lobbyId: Guid.NewGuid(), userId: Guid.NewGuid(), payload: payload);
    }

    private static (Mock<ILobbyHubService> hub, Mock<IPushNotificationService> push, Mock<IPosHubService> pos)
        CreateMockHubs()
    {
        var lobbyHubMock = new Mock<ILobbyHubService>();
        lobbyHubMock.Setup(x => x.NotifyLobbyActivated(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);
        lobbyHubMock.Setup(x => x.NotifyLobbyCheckedIn(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);

        var pushMock = new Mock<IPushNotificationService>();
        pushMock.Setup(x => x.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(1));

        var posMock = new Mock<IPosHubService>();
        posMock.Setup(x => x.NotifySessionPaidAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(),
            It.IsAny<decimal>(), It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        return (lobbyHubMock, pushMock, posMock);
    }

    private static IServiceProvider BuildServiceProvider(ILobbyHubService? hub = null, IPushNotificationService? push = null, IPosHubService? pos = null)
    {
        var services = new ServiceCollection();
        if (hub != null) services.AddSingleton(hub);
        if (push != null) services.AddSingleton(push);
        if (pos != null) services.AddSingleton(pos);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PublishAsync_UnknownEventType_CompletesWithoutThrowing()
    {
        var sp = BuildServiceProvider();
        var sut = new RealOutboxPublisher(sp, NullLogger<RealOutboxPublisher>.Instance);

        var evt = BuildEvent((OutboxEventType)999);

        await sut.PublishAsync(evt, CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsync_LobbyActivatedEvent_NoLobbyOrUser_DoesNotPush()
    {
        var (hub, push, pos) = CreateMockHubs();
        var sp = BuildServiceProvider(hub.Object, push.Object, pos.Object);

        var sut = new RealOutboxPublisher(sp, NullLogger<RealOutboxPublisher>.Instance);
        var evt = BuildEvent(OutboxEventType.LobbyActivated);

        await sut.PublishAsync(evt, CancellationToken.None);

        hub.Verify(x => x.NotifyLobbyActivated(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        push.Verify(x => x.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_LobbyActivatedEvent_DispatchesToLobbyHubAndPush()
    {
        var (hub, push, pos) = CreateMockHubs();
        var sp = BuildServiceProvider(hub.Object, push.Object, pos.Object);

        var sut = new RealOutboxPublisher(sp, NullLogger<RealOutboxPublisher>.Instance);

        var lobbyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = BuildEvent(OutboxEventType.LobbyActivated, lobbyId: lobbyId, userId: userId);

        await sut.PublishAsync(evt, CancellationToken.None);

        hub.Verify(x => x.NotifyLobbyActivated(lobbyId, userId), Times.Once);
        push.Verify(x => x.SendAsync(userId, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_SessionCompletedEvent_WithSessionIdInPayload_DispatchesToPosHub()
    {
        var (hub, push, pos) = CreateMockHubs();
        var sp = BuildServiceProvider(hub.Object, push.Object, pos.Object);

        var sut = new RealOutboxPublisher(sp, NullLogger<RealOutboxPublisher>.Instance);

        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var evt = BuildSessionCompletedWithSessionId(sessionId, cafeId, totalAmount: 80_000m);

        await sut.PublishAsync(evt, CancellationToken.None);

        pos.Verify(x => x.NotifySessionPaidAsync(sessionId, cafeId, evt.LobbyId, It.IsAny<decimal>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_SessionCompletedEvent_MissingSessionIdAndLobbyId_SkipsPosHub()
    {
        var (hub, push, pos) = CreateMockHubs();
        var sp = BuildServiceProvider(hub.Object, push.Object, pos.Object);

        var sut = new RealOutboxPublisher(sp, NullLogger<RealOutboxPublisher>.Instance);

        var evt = BuildEvent(OutboxEventType.SessionCompleted, lobbyId: null, userId: Guid.NewGuid(), payload: "{}");

        await sut.PublishAsync(evt, CancellationToken.None);

        pos.Verify(x => x.NotifySessionPaidAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(),
            It.IsAny<decimal>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_ReservationHeldEvent_NoLobbyOrUser_DoesNotPush()
    {
        var (hub, push, pos) = CreateMockHubs();
        var sp = BuildServiceProvider(hub.Object, push.Object, pos.Object);

        var sut = new RealOutboxPublisher(sp, NullLogger<RealOutboxPublisher>.Instance);

        var evt = BuildEvent(OutboxEventType.ReservationHeld);

        await sut.PublishAsync(evt, CancellationToken.None);

        // No LobbyId → no push
        push.Verify(x => x.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_InvalidPayload_DoesNotThrow()
    {
        var (hub, push, pos) = CreateMockHubs();
        var sp = BuildServiceProvider(hub.Object, push.Object, pos.Object);

        var sut = new RealOutboxPublisher(sp, NullLogger<RealOutboxPublisher>.Instance);

        var evt = BuildEvent(OutboxEventType.LobbyCheckedIn, lobbyId: Guid.NewGuid(), userId: Guid.NewGuid(), payload: "not-json");

        await sut.PublishAsync(evt, CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsync_DepositReleasedEvent_WithUserId_PushesNotification()
    {
        var (hub, push, pos) = CreateMockHubs();
        var sp = BuildServiceProvider(hub.Object, push.Object, pos.Object);

        var sut = new RealOutboxPublisher(sp, NullLogger<RealOutboxPublisher>.Instance);

        var userId = Guid.NewGuid();
        var evt = BuildEvent(OutboxEventType.DepositReleased, userId: userId);

        await sut.PublishAsync(evt, CancellationToken.None);

        // UserId set + DepositReleased always pushes to user (even without LobbyId)
        push.Verify(x => x.SendAsync(userId, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_DepositReleasedEvent_NoUserId_DoesNotPush()
    {
        var (hub, push, pos) = CreateMockHubs();
        var sp = BuildServiceProvider(hub.Object, push.Object, pos.Object);

        var sut = new RealOutboxPublisher(sp, NullLogger<RealOutboxPublisher>.Instance);

        var evt = BuildEvent(OutboxEventType.DepositReleased, userId: null);

        await sut.PublishAsync(evt, CancellationToken.None);

        push.Verify(x => x.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
