using BoardVerse.Core.Constants;
using BoardVerse.Core.Enum;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Tests for CafeSchedule static utility class (BR-NEW-15).
/// TimeSlot enum retained for backward compat; new API uses TimeOnly.
/// </summary>
public class TimeSlotServiceTests
{
    [Fact]
    public void DefaultOpenTime_Is6AM()
    {
        Assert.Equal(new TimeOnly(6, 0), CafeSchedule.DefaultOpenTime);
    }

    [Fact]
    public void DefaultCloseTime_Is11PM()
    {
        Assert.Equal(new TimeOnly(23, 0), CafeSchedule.DefaultCloseTime);
    }

    [Theory]
    [InlineData(6, 0, 12, 0, true)]
    [InlineData(12, 0, 17, 0, true)]
    [InlineData(17, 0, 23, 0, true)]
    [InlineData(5, 0, 8, 0, false)] // before open
    [InlineData(22, 0, 23, 30, false)] // after close (23:30 > 23:00)
    [InlineData(21, 0, 0, 0, true)] // overnight to midnight
    [InlineData(14, 0, 10, 0, true)] // overnight
    [InlineData(10, 0, 10, 0, false)] // same time
    public void ValidatePreferredTimeRange_ReturnsExpectedResult(
        int startH, int startM, int endH, int endM, bool expectedValid)
    {
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(startH, startM),
            new TimeOnly(endH, endM));
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_ReturnsSameDayDateTime()
    {
        var playDate = new DateOnly(2026, 8, 15);
        var (start, end) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(10, 0), new TimeOnly(14, 0));

        Assert.Equal(playDate, DateOnly.FromDateTime(start));
        Assert.Equal(playDate, DateOnly.FromDateTime(end));
        Assert.Equal(10, start.Hour);
        Assert.Equal(14, end.Hour);
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_Overnight_UsesNextDayForEnd()
    {
        var playDate = new DateOnly(2026, 8, 18);
        var (start, end) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(21, 0), new TimeOnly(0, 0));

        Assert.Equal(new DateTime(2026, 8, 18, 21, 0, 0), start);
        Assert.Equal(new DateTime(2026, 8, 19, 0, 0, 0), end);
    }

    [Theory]
    [InlineData(TimeSlot.Afternoon, 12, 0, 17, 0)]
    [InlineData(TimeSlot.Evening, 17, 0, 23, 0)]
    [InlineData(TimeSlot.LateNight, 23, 0, 6, 0)]
    public void TimeSlotExtensions_GetStartEnd_ReturnsCorrectValues(
        TimeSlot slot, int startH, int startM, int endH, int endM)
    {
        Assert.Equal(new TimeOnly(startH, startM), slot.GetStartTime());
        Assert.Equal(new TimeOnly(endH, endM), slot.GetEndTime());
    }

    [Theory]
    [InlineData(TimeSlot.Morning, false)]
    [InlineData(TimeSlot.Afternoon, false)]
    [InlineData(TimeSlot.Evening, false)]
    [InlineData(TimeSlot.LateNight, true)]
    public void TimeSlotExtensions_IsOvernight_ReturnsCorrectValue(TimeSlot slot, bool expected)
    {
        Assert.Equal(expected, slot.IsOvernight());
    }
}
