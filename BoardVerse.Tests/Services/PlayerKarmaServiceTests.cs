using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho PlayerKarmaService — BR-KARMA-01..04 (docs/karma.md).
/// Short-play (ratio &lt; 50%) → -5 karma; no-show → -10; host dissolve → -8/-15.
/// Idempotent: đã có record → skip.
/// </summary>
public class PlayerKarmaServiceTests
{
    private readonly Mock<IKarmaShortPlayRecordRepository> _recordRepo = new();
    private readonly Mock<IUserProfileRepository> _userProfileRepo = new();

    private PlayerKarmaService CreateService() => new(
        _recordRepo.Object,
        _userProfileRepo.Object,
        NullLogger<PlayerKarmaService>.Instance);

    private static UserProfile BuildProfile(Guid userId, int karma = 100) => new()
    {
        UserId = userId,
        KarmaPoints = karma
    };

    #region RecordShortPlayAsync

    [Fact]
    public async Task RecordShortPlayAsync_ScheduledZero_ReturnsFalse()
    {
        var sut = CreateService();
        var result = await sut.RecordShortPlayAsync(Guid.NewGuid(), Guid.NewGuid(), playedMinutes: 10, scheduledMinutes: 0);

        Assert.False(result);
        _recordRepo.Verify(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordShortPlayAsync_RatioAtLeast50Percent_ReturnsFalse()
    {
        var sut = CreateService();
        var result = await sut.RecordShortPlayAsync(Guid.NewGuid(), Guid.NewGuid(), playedMinutes: 60, scheduledMinutes: 60);

        Assert.False(result);
    }

    [Fact]
    public async Task RecordShortPlayAsync_RatioAbove50Percent_ReturnsFalse()
    {
        var sut = CreateService();
        var result = await sut.RecordShortPlayAsync(Guid.NewGuid(), Guid.NewGuid(), playedMinutes: 70, scheduledMinutes: 100);

        Assert.False(result);
    }

    [Fact]
    public async Task RecordShortPlayAsync_ExistingRecord_IdempotentSkip()
    {
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KarmaShortPlayRecord { Id = Guid.NewGuid() });

        var sut = CreateService();
        var result = await sut.RecordShortPlayAsync(reservationId, userId, 10, 100);

        Assert.False(result);
        _recordRepo.Verify(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordShortPlayAsync_NoProfile_Skip()
    {
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KarmaShortPlayRecord?)null);
        _userProfileRepo.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        var sut = CreateService();
        var result = await sut.RecordShortPlayAsync(reservationId, userId, 10, 100);

        Assert.False(result);
    }

    [Fact]
    public async Task RecordShortPlayAsync_Below50Percent_Deducts5KarmaAndPersists()
    {
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KarmaShortPlayRecord?)null);
        _userProfileRepo.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildProfile(userId, karma: 80));

        KarmaShortPlayRecord? captured = null;
        _recordRepo.Setup(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()))
            .Callback<KarmaShortPlayRecord, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var sut = CreateService();
        var result = await sut.RecordShortPlayAsync(reservationId, userId, playedMinutes: 30, scheduledMinutes: 100);

        Assert.True(result);
        Assert.NotNull(captured);
        Assert.Equal(-5, captured!.KarmaDelta);
        Assert.Equal(75, captured.TotalKarmaScore);
        Assert.Equal(0.30m, captured.PlayedRatio);
        Assert.Equal(KarmaRecordStatus.Active, captured.Status);
        _userProfileRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordShortPlayAsync_RatioClampedTo1_HandledSafely()
    {
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KarmaShortPlayRecord?)null);
        _userProfileRepo.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildProfile(userId));

        var sut = CreateService();
        // played > scheduled → ratio clamps to 1 → above 0.5 → skip
        var result = await sut.RecordShortPlayAsync(reservationId, userId, playedMinutes: 200, scheduledMinutes: 100);

        Assert.False(result);
    }

    #endregion

    #region RecordNoShowAsync

    [Fact]
    public async Task RecordNoShowAsync_ExistingRecord_IdempotentSkip()
    {
        var reservationId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, hostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KarmaShortPlayRecord { Id = Guid.NewGuid() });

        var sut = CreateService();
        var result = await sut.RecordNoShowAsync(reservationId, hostId);

        Assert.False(result);
    }

    [Fact]
    public async Task RecordNoShowAsync_NoProfile_Skip()
    {
        var reservationId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, hostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KarmaShortPlayRecord?)null);
        _userProfileRepo.Setup(r => r.GetProfileByUserIdAsync(hostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        var sut = CreateService();
        var result = await sut.RecordNoShowAsync(reservationId, hostId);

        Assert.False(result);
    }

    [Fact]
    public async Task RecordNoShowAsync_FirstTime_Deducts10Karma()
    {
        var reservationId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, hostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KarmaShortPlayRecord?)null);
        _userProfileRepo.Setup(r => r.GetProfileByUserIdAsync(hostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildProfile(hostId, karma: 70));

        KarmaShortPlayRecord? captured = null;
        _recordRepo.Setup(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()))
            .Callback<KarmaShortPlayRecord, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var sut = CreateService();
        var result = await sut.RecordNoShowAsync(reservationId, hostId);

        Assert.True(result);
        Assert.Equal(-10, captured!.KarmaDelta);
        Assert.Equal(60, captured.TotalKarmaScore);
        Assert.Equal(KarmaRecordStatus.Active, captured.Status);
    }

    #endregion

    #region RecordEarlyCheckoutAsync

    [Fact]
    public async Task RecordEarlyCheckoutAsync_ExistingRecord_IdempotentSkip()
    {
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KarmaShortPlayRecord { Id = Guid.NewGuid() });

        var sut = CreateService();
        var result = await sut.RecordEarlyCheckoutAsync(reservationId, userId, 30, 100);

        Assert.False(result);
    }

    [Fact]
    public async Task RecordEarlyCheckoutAsync_NoExisting_AddsClearedRecord()
    {
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KarmaShortPlayRecord?)null);

        KarmaShortPlayRecord? captured = null;
        _recordRepo.Setup(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()))
            .Callback<KarmaShortPlayRecord, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var sut = CreateService();
        var result = await sut.RecordEarlyCheckoutAsync(reservationId, userId, 30, 100);

        Assert.True(result);
        Assert.Equal(0, captured!.KarmaDelta);
        Assert.Equal(KarmaRecordStatus.Cleared, captured.Status);
        Assert.Equal(0.30m, captured.PlayedRatio);
    }

    [Fact]
    public async Task RecordEarlyCheckoutAsync_ScheduledZero_RatioIsZero()
    {
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _recordRepo.Setup(r => r.GetByReservationAndUserAsync(reservationId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KarmaShortPlayRecord?)null);

        KarmaShortPlayRecord? captured = null;
        _recordRepo.Setup(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()))
            .Callback<KarmaShortPlayRecord, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var sut = CreateService();
        var result = await sut.RecordEarlyCheckoutAsync(reservationId, userId, 30, scheduledMinutes: 0);

        Assert.True(result);
        Assert.Equal(0m, captured!.PlayedRatio);
    }

    #endregion

    #region RecordHostDissolveAsync (GAP-4 / BR-REFUND-02)

    [Theory]
    [InlineData(48.0)]
    [InlineData(24.0)]
    [InlineData(100.0)]
    public async Task RecordHostDissolveAsync_AtLeast24Hours_NoPenalty(double hours)
    {
        var sut = CreateService();
        var result = await sut.RecordHostDissolveAsync(Guid.NewGuid(), Guid.NewGuid(), hours, "policy-x");

        Assert.False(result);
        _userProfileRepo.Verify(r => r.GetProfileByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _recordRepo.Verify(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(12.0)]
    [InlineData(6.0)]
    [InlineData(7.5)]
    public async Task RecordHostDissolveAsync_6To24Hours_Deducts8Karma(double hours)
    {
        var hostId = Guid.NewGuid();
        _userProfileRepo.Setup(r => r.GetProfileByUserIdAsync(hostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildProfile(hostId, karma: 100));

        KarmaShortPlayRecord? captured = null;
        _recordRepo.Setup(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()))
            .Callback<KarmaShortPlayRecord, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var sut = CreateService();
        var result = await sut.RecordHostDissolveAsync(Guid.NewGuid(), hostId, hours, "policy-6to24");

        Assert.True(result);
        Assert.Equal(-8, captured!.KarmaDelta);
        Assert.Equal(92, captured.TotalKarmaScore);
        Assert.Contains("host-dissolve:policy=policy-6to24", captured.AppealReason);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(5.99)]
    public async Task RecordHostDissolveAsync_LessThan6Hours_Deducts15Karma(double hours)
    {
        var hostId = Guid.NewGuid();
        _userProfileRepo.Setup(r => r.GetProfileByUserIdAsync(hostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildProfile(hostId, karma: 100));

        KarmaShortPlayRecord? captured = null;
        _recordRepo.Setup(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()))
            .Callback<KarmaShortPlayRecord, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var sut = CreateService();
        var result = await sut.RecordHostDissolveAsync(Guid.NewGuid(), hostId, hours, "policy-less6");

        Assert.True(result);
        Assert.Equal(-15, captured!.KarmaDelta);
        Assert.Equal(85, captured.TotalKarmaScore);
    }

    [Fact]
    public async Task RecordHostDissolveAsync_NoProfile_Skip()
    {
        var hostId = Guid.NewGuid();
        _userProfileRepo.Setup(r => r.GetProfileByUserIdAsync(hostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        var sut = CreateService();
        var result = await sut.RecordHostDissolveAsync(Guid.NewGuid(), hostId, 2.0, "policy-x");

        Assert.False(result);
        _recordRepo.Verify(r => r.AddAsync(It.IsAny<KarmaShortPlayRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region GetLatestByUserAsync

    [Fact]
    public async Task GetLatestByUserAsync_DelegatesToRepository()
    {
        var userId = Guid.NewGuid();
        var record = new KarmaShortPlayRecord { Id = Guid.NewGuid(), UserId = userId };
        _recordRepo.Setup(r => r.GetLatestByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var sut = CreateService();
        var result = await sut.GetLatestByUserAsync(userId);

        Assert.Same(record, result);
    }

    #endregion
}
