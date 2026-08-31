using BoardVerse.Core.Constants;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.Helpers;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests for GAP-5 fix: LobbyService.ChangeTimeAsync validates CafeScheduleOverride.
/// </summary>
public class LobbyServiceCafeScheduleValidationTests
{
    [Fact]
    public async Task ValidatePreferredTimesWithCafeScheduleAsync_CafeClosed_ThrowsBadRequest()
    {
        // Arrange
        var mockResolver = new Mock<IScheduleResolver>();
        var cafeId = Guid.NewGuid();
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // Cafe đóng cửa ngày playDate
        mockResolver.Setup(r => r.ResolveAsync(cafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(6, 0),
                CloseTime: new TimeOnly(23, 0),
                IsClosed: true,
                HasOverride: true));

        // Act & Assert
        await Assert.ThrowsAsync<BadRequestException>(() =>
            CafeScheduleValidator.ValidatePreferredTimesWithCafeScheduleAsync(
                mockResolver.Object,
                cafeId,
                playDate,
                new TimeOnly(18, 0),
                new TimeOnly(22, 0)));
    }

    [Fact]
    public async Task ValidatePreferredTimesWithCafeScheduleAsync_StartBeforeCafeOpen_ThrowsBadRequest()
    {
        // Arrange
        var mockResolver = new Mock<IScheduleResolver>();
        var cafeId = Guid.NewGuid();
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // Cafe mở cửa 08:00-23:00 (override)
        mockResolver.Setup(r => r.ResolveAsync(cafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(8, 0),
                CloseTime: new TimeOnly(23, 0),
                IsClosed: false,
                HasOverride: true));

        // Act & Assert - preferredStart 07:00 < 08:00
        await Assert.ThrowsAsync<BadRequestException>(() =>
            CafeScheduleValidator.ValidatePreferredTimesWithCafeScheduleAsync(
                mockResolver.Object,
                cafeId,
                playDate,
                new TimeOnly(7, 0),
                new TimeOnly(22, 0)));
    }

    [Fact]
    public async Task ValidatePreferredTimesWithCafeScheduleAsync_EndAfterCafeClose_ThrowsBadRequest()
    {
        // Arrange
        var mockResolver = new Mock<IScheduleResolver>();
        var cafeId = Guid.NewGuid();
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // Cafe đóng cửa 22:00 (override)
        mockResolver.Setup(r => r.ResolveAsync(cafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(6, 0),
                CloseTime: new TimeOnly(22, 0),
                IsClosed: false,
                HasOverride: true));

        // Act & Assert - preferredEnd 23:00 > 22:00
        await Assert.ThrowsAsync<BadRequestException>(() =>
            CafeScheduleValidator.ValidatePreferredTimesWithCafeScheduleAsync(
                mockResolver.Object,
                cafeId,
                playDate,
                new TimeOnly(18, 0),
                new TimeOnly(23, 0)));
    }

    [Fact]
    public async Task ValidatePreferredTimesWithCafeScheduleAsync_OvernightSession_ValidatesNextDay()
    {
        // Arrange
        var mockResolver = new Mock<IScheduleResolver>();
        var cafeId = Guid.NewGuid();
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var nextDay = playDate.AddDays(1);

        // Ngày bắt đầu: mở cửa 06:00-23:00
        mockResolver.Setup(r => r.ResolveAsync(cafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(6, 0),
                CloseTime: new TimeOnly(23, 0),
                IsClosed: false,
                HasOverride: false));

        // Ngày kế tiếp: đóng cửa
        mockResolver.Setup(r => r.ResolveAsync(cafeId, nextDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(6, 0),
                CloseTime: new TimeOnly(23, 0),
                IsClosed: true,
                HasOverride: true));

        // Act & Assert - overnight session (22:00 -> 02:00 ngày kế)
        await Assert.ThrowsAsync<BadRequestException>(() =>
            CafeScheduleValidator.ValidatePreferredTimesWithCafeScheduleAsync(
                mockResolver.Object,
                cafeId,
                playDate,
                new TimeOnly(22, 0),
                new TimeOnly(2, 0)));
    }

    [Fact]
    public async Task ValidatePreferredTimesWithCafeScheduleAsync_OvernightSession_EndAfterNextDayClose_ThrowsBadRequest()
    {
        // Arrange
        var mockResolver = new Mock<IScheduleResolver>();
        var cafeId = Guid.NewGuid();
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var nextDay = playDate.AddDays(1);

        // Ngày bắt đầu: mở cửa 06:00-23:00
        mockResolver.Setup(r => r.ResolveAsync(cafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(6, 0),
                CloseTime: new TimeOnly(23, 0),
                IsClosed: false,
                HasOverride: false));

        // Ngày kế tiếp: đóng cửa 01:00 (overnight cafe)
        mockResolver.Setup(r => r.ResolveAsync(cafeId, nextDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(6, 0),
                CloseTime: new TimeOnly(1, 0),
                IsClosed: false,
                HasOverride: true));

        // Act & Assert - overnight session end 02:00 > 01:00
        await Assert.ThrowsAsync<BadRequestException>(() =>
            CafeScheduleValidator.ValidatePreferredTimesWithCafeScheduleAsync(
                mockResolver.Object,
                cafeId,
                playDate,
                new TimeOnly(22, 0),
                new TimeOnly(2, 0)));
    }

    [Fact]
    public async Task ValidatePreferredTimesWithCafeScheduleAsync_ValidRange_DoesNotThrow()
    {
        // Arrange
        var mockResolver = new Mock<IScheduleResolver>();
        var cafeId = Guid.NewGuid();
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // Cafe mở cửa 06:00-23:00
        mockResolver.Setup(r => r.ResolveAsync(cafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(6, 0),
                CloseTime: new TimeOnly(23, 0),
                IsClosed: false,
                HasOverride: false));

        // Act & Assert - valid range 18:00-22:00
        await CafeScheduleValidator.ValidatePreferredTimesWithCafeScheduleAsync(
            mockResolver.Object,
            cafeId,
            playDate,
            new TimeOnly(18, 0),
            new TimeOnly(22, 0));
        // No exception = success
    }

    [Fact]
    public async Task ValidatePreferredTimesWithCafeScheduleAsync_OvernightValid_DoesNotThrow()
    {
        // Arrange
        var mockResolver = new Mock<IScheduleResolver>();
        var cafeId = Guid.NewGuid();
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var nextDay = playDate.AddDays(1);

        // Ngày bắt đầu: mở cửa 06:00-23:00
        mockResolver.Setup(r => r.ResolveAsync(cafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(6, 0),
                CloseTime: new TimeOnly(23, 0),
                IsClosed: false,
                HasOverride: false));

        // Ngày kế tiếp: mở cửa 06:00-06:00 (24h)
        mockResolver.Setup(r => r.ResolveAsync(cafeId, nextDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule(
                OpenTime: new TimeOnly(6, 0),
                CloseTime: new TimeOnly(6, 0),
                IsClosed: false,
                HasOverride: false));

        // Act & Assert - overnight session 22:00 -> 02:00
        await CafeScheduleValidator.ValidatePreferredTimesWithCafeScheduleAsync(
            mockResolver.Object,
            cafeId,
            playDate,
            new TimeOnly(22, 0),
            new TimeOnly(2, 0));
        // No exception = success
    }
}
