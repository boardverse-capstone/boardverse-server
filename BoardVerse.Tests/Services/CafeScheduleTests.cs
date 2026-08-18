using BoardVerse.Core.Constants;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="CafeSchedule"/> — BR-NEW-15 TimeOnly-based schedule (2026-08-18).
/// TimeSlot enum đã bị loại bỏ. CafeSchedule giờ chỉ có:
/// - DefaultOpenTime (06:00)
/// - DefaultCloseTime (23:00)
/// - ValidatePreferredTimeRange(TimeOnly preferredStart, TimeOnly preferredEnd)
/// - BuildScheduledStartEndFromPreferred(DateOnly playDate, TimeOnly preferredStart, TimeOnly preferredEnd)
/// </summary>
public class CafeScheduleTests
{
    // ===== Default constants =====

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

    // ===== ValidatePreferredTimeRange =====

    [Fact]
    public void ValidatePreferredTimeRange_ValidRange_ReturnsTrue()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(10, 0), new TimeOnly(14, 0));

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_EndBeforeStart_AllowsOvernight()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(21, 0), new TimeOnly(0, 0));

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_EndEqualsStart_ReturnsFalse()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(12, 0), new TimeOnly(12, 0));

        Assert.False(isValid);
    }

    [Fact]
    public void ValidatePreferredTimeRange_StartBeforeOpenTime_ReturnsFalse()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(5, 0), new TimeOnly(10, 0));

        Assert.False(isValid);
        Assert.Contains("mở cửa", error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_EndAfterCloseTime_ReturnsFalse()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(22, 0), new TimeOnly(23, 30));

        Assert.False(isValid);
        Assert.Contains("đóng cửa", error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_ExactlyAtBoundary_ReturnsTrue()
    {
        // Start at open time, end at close time - both valid boundaries
        var (isValid1, _) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(6, 0), new TimeOnly(23, 0));

        Assert.True(isValid1);
    }

    [Fact]
    public void ValidatePreferredTimeRange_EndEqualsCloseTime_ReturnsTrue()
    {
        // End at exactly close time is valid
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(10, 0), new TimeOnly(23, 0));

        Assert.True(isValid);
    }

    [Fact]
    public void ValidatePreferredTimeRange_StartJustBeforeOpen_ReturnsFalse()
    {
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(5, 59), new TimeOnly(10, 0));

        Assert.False(isValid);
    }

    [Fact]
    public void ValidatePreferredTimeRange_EndJustAfterClose_ReturnsFalse()
    {
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(20, 0), new TimeOnly(23, 1));

        Assert.False(isValid);
    }

    // ===== BuildScheduledStartEndFromPreferred =====

    [Fact]
    public void BuildScheduledStartEndFromPreferred_ReturnsCorrectDateTime()
    {
        var playDate = new DateOnly(2026, 8, 14);

        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(10, 0), new TimeOnly(14, 0));

        Assert.Equal(new DateTime(2026, 8, 14, 10, 0, 0), scheduledStart);
        Assert.Equal(new DateTime(2026, 8, 14, 14, 0, 0), scheduledEnd);
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_SameDay()
    {
        var playDate = new DateOnly(2026, 8, 14);

        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(6, 0), new TimeOnly(23, 0));

        Assert.Equal(new DateTime(2026, 8, 14, 6, 0, 0), scheduledStart);
        Assert.Equal(new DateTime(2026, 8, 14, 23, 0, 0), scheduledEnd);
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_EveningSlot()
    {
        var playDate = new DateOnly(2026, 8, 14);

        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(19, 0), new TimeOnly(22, 0));

        Assert.Equal(new DateTime(2026, 8, 14, 19, 0, 0), scheduledStart);
        Assert.Equal(new DateTime(2026, 8, 14, 22, 0, 0), scheduledEnd);
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_Overnight_UsesNextDayForEnd()
    {
        var playDate = new DateOnly(2026, 8, 18);
        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(21, 0), new TimeOnly(0, 0));

        Assert.Equal(new DateTime(2026, 8, 18, 21, 0, 0), scheduledStart);
        Assert.Equal(new DateTime(2026, 8, 19, 0, 0, 0), scheduledEnd);
    }

    // ===== Edge cases =====

    [Theory]
    [InlineData(6, 0, 6, 0, false)]   // zero duration
    [InlineData(6, 0, 6, 30, true)]  // 30 minutes
    [InlineData(10, 0, 10, 0, false)]  // zero duration
    [InlineData(21, 0, 0, 0, true)]   // overnight to midnight
    [InlineData(22, 0, 23, 0, true)]   // exactly at close
    public void ValidatePreferredTimeRange_ZeroDuration_ReturnsFalse(
        int startH, int startM, int endH, int endM, bool expected)
    {
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(startH, startM), new TimeOnly(endH, endM));

        Assert.Equal(expected, isValid);
    }

    [Fact]
    public void ValidatePreferredTimeRange_MinimalValidRange()
    {
        // 1 minute is the minimum valid range
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(6, 0), new TimeOnly(6, 1));

        Assert.True(isValid);
    }

    [Fact]
    public void ValidatePreferredTimeRange_FullDay_ReturnsTrue()
    {
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(6, 0), new TimeOnly(23, 0));

        Assert.True(isValid);
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_DifferentDates()
    {
        var playDate = new DateOnly(2026, 8, 14);

        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(22, 30), new TimeOnly(23, 0));

        Assert.Equal(new DateTime(2026, 8, 14, 22, 30, 0), scheduledStart);
        Assert.Equal(new DateTime(2026, 8, 14, 23, 0, 0), scheduledEnd);
    }
}
