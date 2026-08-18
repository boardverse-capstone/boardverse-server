using BoardVerse.Core.Constants;
using BoardVerse.Core.Enum;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests for CafeSchedule utilities and TimeSlotExtensions.
/// BR-NEW-15: TimeSlot enum retained for backward compat; new API uses TimeOnly.
/// </summary>
public class ReservationServiceTimeValidationTests
{
    // ===== BR-NEW-15 / CafeSchedule.ValidatePreferredTimeRange =====

    [Fact]
    public void ValidatePreferredTimeRange_ValidRange_ReturnsTrue()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(10, 0), new TimeOnly(14, 0));
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_OvernightRange_ReturnsTrue()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(21, 0), new TimeOnly(0, 0));
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_StartBeforeOpen_ReturnsFalse()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(5, 0), new TimeOnly(8, 0));
        Assert.False(isValid);
        Assert.Contains("mở cửa", error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_EndAfterClose_ReturnsFalse()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(22, 0), new TimeOnly(23, 30));
        Assert.False(isValid);
        Assert.Contains("đóng cửa", error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_SameTime_ReturnsFalse()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(10, 0), new TimeOnly(10, 0));
        Assert.False(isValid);
    }

    // ===== BR-NEW-15 / CafeSchedule.BuildScheduledStartEndFromPreferred =====

    [Fact]
    public void BuildScheduledStartEndFromPreferred_ReturnsCorrectDateTime()
    {
        var playDate = new DateOnly(2026, 8, 15);
        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(10, 0), new TimeOnly(14, 0));

        Assert.Equal(new DateTime(2026, 8, 15, 10, 0, 0), scheduledStart);
        Assert.Equal(new DateTime(2026, 8, 15, 14, 0, 0), scheduledEnd);
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_SameDayOnly()
    {
        var playDate = new DateOnly(2026, 8, 15);
        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(17, 0), new TimeOnly(23, 0));

        Assert.Equal(playDate, DateOnly.FromDateTime(scheduledStart));
        Assert.Equal(playDate, DateOnly.FromDateTime(scheduledEnd));
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_Overnight_UsesNextDayForEnd()
    {
        var playDate = new DateOnly(2026, 8, 18);
        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(21, 0), new TimeOnly(0, 0));

        Assert.Equal(new DateTime(2026, 8, 18, 21, 0, 0), scheduledStart);
        Assert.Equal(new DateTime(2026, 8, 19, 0, 0, 0), scheduledEnd);
        Assert.Equal(TimeSpan.FromHours(3), scheduledEnd - scheduledStart);
    }

    // ===== TimeSlotExtensions (backward compat) =====

    [Theory]
    [InlineData(TimeSlot.Morning, 6, 0)]
    [InlineData(TimeSlot.Afternoon, 12, 0)]
    [InlineData(TimeSlot.Evening, 17, 0)]
    [InlineData(TimeSlot.LateNight, 23, 0)]
    public void TimeSlotExtensions_GetStartTime_ReturnsCorrectHour(TimeSlot slot, int hour, int minute)
    {
        var start = slot.GetStartTime();
        Assert.Equal(new TimeOnly(hour, minute), start);
    }

    [Theory]
    [InlineData(TimeSlot.Morning, 12, 0)]
    [InlineData(TimeSlot.Afternoon, 17, 0)]
    [InlineData(TimeSlot.Evening, 23, 0)]
    [InlineData(TimeSlot.LateNight, 6, 0)]
    public void TimeSlotExtensions_GetEndTime_ReturnsCorrectHour(TimeSlot slot, int hour, int minute)
    {
        var end = slot.GetEndTime();
        Assert.Equal(new TimeOnly(hour, minute), end);
    }

    [Theory]
    [InlineData(TimeSlot.Morning, 6)]
    [InlineData(TimeSlot.Afternoon, 5)]
    [InlineData(TimeSlot.Evening, 6)]
    [InlineData(TimeSlot.LateNight, 7)]
    public void TimeSlotExtensions_GetDurationMinutes_ReturnsCorrectDuration(TimeSlot slot, int expectedHours)
    {
        var duration = slot.GetDurationMinutes();
        Assert.Equal(expectedHours * 60, duration);
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

    [Theory]
    [InlineData(TimeSlot.Morning, "Sáng (06:00-12:00)")]
    [InlineData(TimeSlot.Afternoon, "Chiều (12:00-17:00)")]
    [InlineData(TimeSlot.Evening, "Tối (17:00-23:00)")]
    [InlineData(TimeSlot.LateNight, "Khuya (23:00-06:00)")]
    public void TimeSlotExtensions_GetDisplayName_ReturnsCorrectValue(TimeSlot slot, string expected)
    {
        Assert.Equal(expected, slot.GetDisplayName());
    }
}
