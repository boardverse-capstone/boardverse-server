using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Phase 7 tests: BR-KARMA-01..05 — High-level KarmaService.
/// - GetUserKarmaLevelAsync
/// - SendWarningIfNeededAsync (3-4 violations → warning)
/// - ApplyRestrictionIfNeededAsync (5+ violations → restrict 30 ngày)
/// - SubmitAppealAsync
/// - ResetMonthlyAsync
/// - IsRestrictedForShortSlots
/// </summary>
public class KarmaServiceTests : IDisposable
{
    private readonly BoardVerseDbContext _db;
    private readonly Mock<IKarmaShortPlayRecordRepository> _recordRepoMock;
    private readonly Mock<IUserProfileRepository> _userProfileRepoMock;
    private readonly Mock<ILogger<KarmaService>> _loggerMock;
    private readonly KarmaService _service;

    public KarmaServiceTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new BoardVerseDbContext(options);

        _recordRepoMock = new Mock<IKarmaShortPlayRecordRepository>();
        _userProfileRepoMock = new Mock<IUserProfileRepository>();
        _loggerMock = new Mock<ILogger<KarmaService>>();

        _service = new KarmaService(
            _recordRepoMock.Object,
            _userProfileRepoMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // ===== GetUserKarmaLevelAsync =====

    [Theory]
    [InlineData(100, KarmaLevel.Excellent)]
    [InlineData(95, KarmaLevel.Excellent)]
    [InlineData(90, KarmaLevel.Excellent)]
    [InlineData(89, KarmaLevel.Good)]
    [InlineData(70, KarmaLevel.Good)]
    [InlineData(69, KarmaLevel.Average)]
    [InlineData(50, KarmaLevel.Average)]
    [InlineData(49, KarmaLevel.Low)]
    [InlineData(30, KarmaLevel.Low)]
    [InlineData(29, KarmaLevel.Poor)]
    [InlineData(10, KarmaLevel.Poor)]
    [InlineData(9, KarmaLevel.Critical)]
    [InlineData(0, KarmaLevel.Critical)]
    public async Task GetUserKarmaLevelAsync_Should_ReturnCorrectLevel(int karmaPoints, KarmaLevel expected)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var profile = new UserProfile { UserId = userId, KarmaPoints = karmaPoints };
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        // Act
        var level = await _service.GetUserKarmaLevelAsync(userId);

        // Assert
        Assert.Equal(expected, level);
    }

    [Fact]
    public async Task GetUserKarmaLevelAsync_Should_ReturnAverage_WhenProfileNotFound()
    {
        var userId = Guid.NewGuid();
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        var level = await _service.GetUserKarmaLevelAsync(userId);

        Assert.Equal(KarmaLevel.Average, level);
    }

    // ===== SendWarningIfNeededAsync =====

    [Fact]
    public async Task SendWarningIfNeededAsync_Should_SendWarning_When3Violations_AndNoRecentWarning()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var profile = new UserProfile { UserId = userId, KarmaPoints = 60 };
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _recordRepoMock.Setup(r => r.GetActiveCountByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Act
        var result = await _service.SendWarningIfNeededAsync(userId);

        // Assert
        Assert.True(result.Sent);
        Assert.Equal(3, result.ViolationCount);
        Assert.NotNull(profile.LastWarningAt);
    }

    [Fact]
    public async Task SendWarningIfNeededAsync_Should_NotSend_WhenViolationsBelow3()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile { UserId = userId, KarmaPoints = 60 };
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _recordRepoMock.Setup(r => r.GetActiveCountByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await _service.SendWarningIfNeededAsync(userId);

        Assert.False(result.Sent);
        Assert.Null(profile.LastWarningAt);
    }

    [Fact]
    public async Task SendWarningIfNeededAsync_Should_NotSend_WhenViolationsAbove4()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile { UserId = userId, KarmaPoints = 60 };
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _recordRepoMock.Setup(r => r.GetActiveCountByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await _service.SendWarningIfNeededAsync(userId);

        Assert.False(result.Sent);
    }

    [Fact]
    public async Task SendWarningIfNeededAsync_Should_NotSend_WhenWarningSentWithin7Days()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile
        {
            UserId = userId,
            KarmaPoints = 60,
            LastWarningAt = DateTime.UtcNow.AddDays(-2)
        };
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _recordRepoMock.Setup(r => r.GetActiveCountByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await _service.SendWarningIfNeededAsync(userId);

        Assert.False(result.Sent);
        Assert.Contains("within last 7 days", result.Reason);
    }

    // ===== ApplyRestrictionIfNeededAsync =====

    [Fact]
    public async Task ApplyRestrictionIfNeededAsync_Should_Restrict_When5Violations()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile { UserId = userId, KarmaPoints = 30 };
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _recordRepoMock.Setup(r => r.GetActiveCountByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await _service.ApplyRestrictionIfNeededAsync(userId);

        Assert.True(result.Applied);
        Assert.NotNull(result.Until);
        Assert.NotNull(profile.KarmaRestrictedUntil);
        Assert.True(profile.KarmaRestrictedUntil > DateTime.UtcNow);
    }

    [Fact]
    public async Task ApplyRestrictionIfNeededAsync_Should_NotRestrict_When4Violations()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile { UserId = userId, KarmaPoints = 30 };
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _recordRepoMock.Setup(r => r.GetActiveCountByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var result = await _service.ApplyRestrictionIfNeededAsync(userId);

        Assert.False(result.Applied);
        Assert.Null(profile.KarmaRestrictedUntil);
    }

    [Fact]
    public async Task ApplyRestrictionIfNeededAsync_Should_NotRestrict_WhenAlreadyRestricted()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile
        {
            UserId = userId,
            KarmaPoints = 30,
            KarmaRestrictedUntil = DateTime.UtcNow.AddDays(15)
        };
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _recordRepoMock.Setup(r => r.GetActiveCountByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var result = await _service.ApplyRestrictionIfNeededAsync(userId);

        Assert.False(result.Applied);
        Assert.Contains("Already restricted", result.Reason);
    }

    // ===== SubmitAppealAsync =====

    [Fact]
    public async Task SubmitAppealAsync_Should_SetAppeal_WhenRecordExistsAndNotReviewed()
    {
        var userId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var record = new KarmaShortPlayRecord
        {
            Id = recordId,
            UserId = userId,
            Status = KarmaRecordStatus.Active
        };
        _recordRepoMock.Setup(r => r.GetByIdAsync(recordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await _service.SubmitAppealAsync(userId, recordId, "Tôi đã hoàn thành đầy đủ phiên chơi");

        Assert.True(result);
        Assert.True(record.AppealRequested);
        Assert.Equal("Tôi đã hoàn thành đầy đủ phiên chơi", record.AppealReason);
    }

    [Fact]
    public async Task SubmitAppealAsync_Should_ReturnFalse_WhenReasonEmpty()
    {
        var userId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var result = await _service.SubmitAppealAsync(userId, recordId, "");

        Assert.False(result);
    }

    [Fact]
    public async Task SubmitAppealAsync_Should_ReturnFalse_WhenRecordNotFound()
    {
        var userId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        _recordRepoMock.Setup(r => r.GetByIdAsync(recordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KarmaShortPlayRecord?)null);

        var result = await _service.SubmitAppealAsync(userId, recordId, "Some reason");

        Assert.False(result);
    }

    [Fact]
    public async Task SubmitAppealAsync_Should_ReturnFalse_WhenRecordBelongsToDifferentUser()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var record = new KarmaShortPlayRecord
        {
            Id = recordId,
            UserId = otherUserId,
            Status = KarmaRecordStatus.Active
        };
        _recordRepoMock.Setup(r => r.GetByIdAsync(recordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await _service.SubmitAppealAsync(userId, recordId, "Reason");

        Assert.False(result);
    }

    [Fact]
    public async Task SubmitAppealAsync_Should_ReturnFalse_WhenAlreadyReviewed()
    {
        var userId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var record = new KarmaShortPlayRecord
        {
            Id = recordId,
            UserId = userId,
            Status = KarmaRecordStatus.Active,
            AppealReviewedAt = DateTime.UtcNow.AddDays(-1)
        };
        _recordRepoMock.Setup(r => r.GetByIdAsync(recordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await _service.SubmitAppealAsync(userId, recordId, "Reason");

        Assert.False(result);
    }

    // ===== ResetMonthlyAsync =====

    [Fact]
    public async Task ResetMonthlyAsync_Should_CallRepositoryExpire()
    {
        _recordRepoMock.Setup(r => r.ExpireOldRecordsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var count = await _service.ResetMonthlyAsync();

        Assert.Equal(7, count);
    }

    // ===== IsRestrictedForShortSlots =====

    [Fact]
    public void IsRestrictedForShortSlots_Should_ReturnFalse_WhenNotRestricted()
    {
        var profile = new UserProfile { UserId = Guid.NewGuid(), KarmaRestrictedUntil = null };
        Assert.False(_service.IsRestrictedForShortSlots(profile, 60));
    }

    [Fact]
    public void IsRestrictedForShortSlots_Should_ReturnFalse_WhenRestrictionExpired()
    {
        var profile = new UserProfile
        {
            UserId = Guid.NewGuid(),
            KarmaRestrictedUntil = DateTime.UtcNow.AddDays(-1)
        };
        Assert.False(_service.IsRestrictedForShortSlots(profile, 60));
    }

    [Fact]
    public void IsRestrictedForShortSlots_Should_ReturnTrue_WhenRestricted_AndShortSlot()
    {
        var profile = new UserProfile
        {
            UserId = Guid.NewGuid(),
            KarmaRestrictedUntil = DateTime.UtcNow.AddDays(15)
        };
        Assert.True(_service.IsRestrictedForShortSlots(profile, 120));
    }

    [Fact]
    public void IsRestrictedForShortSlots_Should_ReturnFalse_WhenRestricted_AndLongSlot()
    {
        var profile = new UserProfile
        {
            UserId = Guid.NewGuid(),
            KarmaRestrictedUntil = DateTime.UtcNow.AddDays(15)
        };
        Assert.False(_service.IsRestrictedForShortSlots(profile, 240));
    }

    // ===== GetUserKarmaPointsAsync =====

    [Fact]
    public async Task GetUserKarmaPointsAsync_Should_ReturnProfilePoints()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile { UserId = userId, KarmaPoints = 75 };
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var points = await _service.GetUserKarmaPointsAsync(userId);

        Assert.Equal(75, points);
    }

    [Fact]
    public async Task GetUserKarmaPointsAsync_Should_ReturnDefault100_WhenProfileNotFound()
    {
        var userId = Guid.NewGuid();
        _userProfileRepoMock.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        var points = await _service.GetUserKarmaPointsAsync(userId);

        Assert.Equal(100, points);
    }
}
