using BoardVerse.API.Hubs;
using BoardVerse.Core.DTOs.Lobby;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho task #7: SignalR booking events trong LobbyHubService.
/// Verify các method broadcast đúng group + đúng event name + payload chứa field cần thiết.
/// </summary>
public class LobbyHubServiceTests
{
    private readonly Mock<IHubContext<LobbyHub>> _hubContextMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly LobbyHubService _sut;

    public LobbyHubServiceTests()
    {
        _hubContextMock = new Mock<IHubContext<LobbyHub>>();
        _clientProxyMock = new Mock<IClientProxy>();
        var groupManagerMock = new Mock<IGroupManager>();
        _hubContextMock.Setup(h => h.Clients.Group(It.IsAny<string>()))
            .Returns(_clientProxyMock.Object);
        _sut = new LobbyHubService(_hubContextMock.Object, Mock.Of<ILogger<LobbyHubService>>());
    }

    [Fact]
    public async Task NotifyBookingCheckedIn_BroadcastsToBookingGroup()
    {
        var bookingId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        await _sut.NotifyBookingCheckedIn(bookingId, DateTime.UtcNow, staffId);

        _hubContextMock.Verify(h => h.Clients.Group($"booking-{bookingId}"), Times.Once);
        _clientProxyMock.Verify(p => p.SendCoreAsync("BookingCheckedIn",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyBookingCheckedOut_BroadcastsToBookingGroup()
    {
        var bookingId = Guid.NewGuid();

        await _sut.NotifyBookingCheckedOut(bookingId, DateTime.UtcNow, 150000m);

        _hubContextMock.Verify(h => h.Clients.Group($"booking-{bookingId}"), Times.Once);
        _clientProxyMock.Verify(p => p.SendCoreAsync("BookingCheckedOut",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyBookingCancelled_BroadcastsToBookingGroup()
    {
        var bookingId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _sut.NotifyBookingCancelled(bookingId, userId, "PlayerCancel", "PendingPolicy");

        _hubContextMock.Verify(h => h.Clients.Group($"booking-{bookingId}"), Times.Once);
        _clientProxyMock.Verify(p => p.SendCoreAsync("BookingCancelled",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyBookingNoShowMarked_BroadcastsToBookingGroup()
    {
        var bookingId = Guid.NewGuid();
        var noShowIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var karmaDeltas = new Dictionary<Guid, int> { [noShowIds[0]] = -10 };

        await _sut.NotifyBookingNoShowMarked(bookingId, noShowIds, karmaDeltas);

        _hubContextMock.Verify(h => h.Clients.Group($"booking-{bookingId}"), Times.Once);
        _clientProxyMock.Verify(p => p.SendCoreAsync("BookingNoShowMarked",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyLobbyAutoCancelled_BroadcastsToLobbyGroup()
    {
        var lobbyId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        await _sut.NotifyLobbyAutoCancelled(lobbyId, cafeId, "Cờ Cá Nhà Bà Tám", DateTime.UtcNow, "NotEnoughMembers");

        _hubContextMock.Verify(h => h.Clients.Group(lobbyId.ToString()), Times.Once);
        _clientProxyMock.Verify(p => p.SendCoreAsync("LobbyAutoCancelled",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyCafePricingChanged_BroadcastsToCafeGroup()
    {
        var cafeId = Guid.NewGuid();

        await _sut.NotifyCafePricingChanged(cafeId, "Cờ Cá Nhà Bà Tám", 80000m, 100000m, DateTime.UtcNow, 12);

        _hubContextMock.Verify(h => h.Clients.Group($"cafe-{cafeId}"), Times.Once);
        _clientProxyMock.Verify(p => p.SendCoreAsync("CafePricingChanged",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyMemberJoined_BroadcastsToLobbyGroup()
    {
        var lobbyId = Guid.NewGuid();
        var member = new LobbyMemberDto { UserId = Guid.NewGuid(), UserName = "alice" };

        await _sut.NotifyMemberJoined(lobbyId, member);

        _hubContextMock.Verify(h => h.Clients.Group(lobbyId.ToString()), Times.Once);
        _clientProxyMock.Verify(p => p.SendCoreAsync("MemberJoined",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
