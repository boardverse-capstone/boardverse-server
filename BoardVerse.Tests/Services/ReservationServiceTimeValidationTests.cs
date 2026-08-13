using BoardVerse.Core.Constants;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Phase 1 tests: BR-RES-07/08/09 — Reservation timing validation.
/// </summary>
public class ReservationServiceTimeValidationTests : IDisposable
{
    private readonly BoardVerseDbContext _db;
    private readonly Mock<IReservationRepository> _resRepoMock;
    private readonly Mock<ILogger<ReservationService>> _loggerMock;

    public ReservationServiceTimeValidationTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new BoardVerseDbContext(options);

        _resRepoMock = new Mock<IReservationRepository>();
        _loggerMock = new Mock<ILogger<ReservationService>>();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // ===== BR-RES-07: Mandatory start AND end times =====

    [Fact]
    public void BuildScheduledStartEnd_Should_Throw_WhenEndTimeMissing()
    {
        // BR-RES-07: Reservation bắt buộc phải có startTime VÀ endTime.
        // Verify via ValidateReservationTimeWindow.
        var playDate = new DateOnly(2026, 8, 15);
        var startTime = new TimeOnly(9, 0);

        // Simulate scenario: null endTime
        Assert.ThrowsAny<Exception>(() =>
            ReservationService.ValidateReservationTimeWindow(
                playDate.ToDateTime(startTime),
                DateTime.MinValue, // invalid
                TimeSlot.Morning));
    }

    // ===== BR-RES-08: Same day only =====

    [Fact]
    public void BuildScheduledStartEnd_Should_Throw_WhenEndTimeDifferentDay()
    {
        // BR-RES-08: endTime phải cùng ngày với startTime.
        // Test by passing endTime that overflows to next day via endTime.Date > playDate
        // (since BuildScheduledStartEnd uses playDate for both, the only way to test this is
        // to call ValidateReservationTimeWindow which validates based on dates)
        var playDate = new DateOnly(2026, 8, 15);
        var startTime = new DateTime(2026, 8, 15, 19, 0, 0); // 19:00 playDate
        var endTime = new DateTime(2026, 8, 16, 0, 0, 0); // next day 00:00

        var ex = Assert.Throws<BadRequestException>(() =>
            ReservationService.ValidateReservationTimeWindow(startTime, endTime, TimeSlot.Night));

        Assert.Contains("cùng ngày", ex.Message);
    }

    [Fact]
    public void BuildScheduledStartEnd_Should_Succeed_WhenSameDay()
    {
        var playDate = new DateOnly(2026, 8, 15);
        var startTime = new TimeOnly(19, 0);
        var endTime = new TimeOnly(23, 59, 59);

        var (start, end) = ReservationService.BuildScheduledStartEnd(playDate, startTime, endTime);

        Assert.Equal(start.Date, end.Date);
        Assert.Equal(playDate.ToDateTime(TimeOnly.MinValue).Date, start.Date);
    }

    [Fact]
    public void ValidateReservationTimeWindow_Should_Throw_WhenStartTimeDefault()
    {
        var playDate = new DateOnly(2026, 8, 15);
        var startTime = default(DateTime); // 0001-01-01
        var endTime = new DateTime(2026, 8, 15, 23, 0, 0);

        var ex = Assert.Throws<BadRequestException>(() =>
            ReservationService.ValidateReservationTimeWindow(startTime, endTime, TimeSlot.Morning));

        Assert.Contains("bắt buộc", ex.Message);
    }

    // ===== BR-RES-09: TimeSlot enum validation =====

    [Theory]
    [InlineData(TimeSlot.Morning)] // 09:00 - 13:00
    [InlineData(TimeSlot.Afternoon)] // 13:00 - 18:00
    [InlineData(TimeSlot.Evening)] // 18:00 - 23:00
    [InlineData(TimeSlot.Night)] // 19:00 - 23:59:59
    public void CafeSchedule_Should_ReturnValidRange_For_AllTimeSlots(TimeSlot slot)
    {
        var start = CafeSchedule.GetStartTime(slot);
        var end = CafeSchedule.GetEndTime(slot);

        Assert.True(start < end, $"TimeSlot {slot}: start {start} phải trước end {end}");
    }

    [Fact]
    public void CafeSchedule_NightSlot_Should_BeSameDay()
    {
        // BR-RES-08: Night slot phải cùng ngày
        var playDate = new DateOnly(2026, 8, 15);
        var start = playDate.ToDateTime(CafeSchedule.GetStartTime(TimeSlot.Night));
        var end = playDate.ToDateTime(CafeSchedule.GetEndTime(TimeSlot.Night));

        Assert.Equal(start.Date, end.Date);
    }

    [Fact]
    public void CafeSchedule_MorningSlot_Should_StartAt9AM()
    {
        var start = CafeSchedule.GetStartTime(TimeSlot.Morning);
        Assert.Equal(new TimeOnly(9, 0), start);
    }

    [Fact]
    public void CafeSchedule_AfternoonSlot_Should_StartAt1PM()
    {
        var start = CafeSchedule.GetStartTime(TimeSlot.Afternoon);
        Assert.Equal(new TimeOnly(13, 0), start);
    }

    [Fact]
    public void CafeSchedule_EveningSlot_Should_EndAt11PM()
    {
        var end = CafeSchedule.GetEndTime(TimeSlot.Evening);
        Assert.Equal(new TimeOnly(23, 0), end);
    }

    [Fact]
    public void CafeSchedule_NightSlot_Should_StartAt7PM_EndAtMidnight()
    {
        var start = CafeSchedule.GetStartTime(TimeSlot.Night);
        var end = CafeSchedule.GetEndTime(TimeSlot.Night);
        Assert.Equal(new TimeOnly(19, 0), start);
        Assert.Equal(new TimeOnly(23, 59, 59), end);
    }
}
