using BoardVerse.Core.Constants;
using BoardVerse.Core.Enum;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="CafeSchedule"/> — default time slot windows (BR-NEW-15, cover 24/7).
/// </summary>
public class CafeScheduleTests
{
    [Fact]
    public void GetStartTime_Morning_Returns6AM()
    {
        Assert.Equal(new TimeOnly(6, 0), CafeSchedule.GetStartTime(TimeSlot.Morning));
    }

    [Fact]
    public void GetEndTime_Morning_Returns12PM()
    {
        Assert.Equal(new TimeOnly(12, 0), CafeSchedule.GetEndTime(TimeSlot.Morning));
    }

    [Fact]
    public void GetStartTime_Afternoon_Returns12PM()
    {
        Assert.Equal(new TimeOnly(12, 0), CafeSchedule.GetStartTime(TimeSlot.Afternoon));
    }

    [Fact]
    public void GetEndTime_Evening_Returns11PM()
    {
        Assert.Equal(new TimeOnly(23, 0), CafeSchedule.GetEndTime(TimeSlot.Evening));
    }

    [Fact]
    public void GetStartTime_LateNight_Returns11PM()
    {
        Assert.Equal(new TimeOnly(23, 0), CafeSchedule.GetStartTime(TimeSlot.LateNight));
    }

    [Fact]
    public void GetEndTime_LateNight_Returns6AM()
    {
        Assert.Equal(new TimeOnly(6, 0), CafeSchedule.GetEndTime(TimeSlot.LateNight));
    }

    [Fact]
    public void IsPreferredStartTimeValid_Null_ReturnsTrue()
    {
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.Morning, null));
    }

    [Fact]
    public void IsPreferredStartTimeValid_MorningWithinRange_ReturnsTrue()
    {
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.Morning, new TimeOnly(10, 0)));
    }

    [Fact]
    public void IsPreferredStartTimeValid_MorningOutOfRange_ReturnsFalse()
    {
        Assert.False(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.Morning, new TimeOnly(5, 0)));
    }

    [Fact]
    public void IsPreferredStartTimeValid_LateNightWithinRange_ReturnsTrue()
    {
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(2, 0)));
    }

    [Fact]
    public void IsPreferredStartTimeValid_LateNightOutOfRange_ReturnsFalse()
    {
        Assert.False(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(12, 0)));
    }

    [Fact]
    public void TimeSlots_CoverFull24Hours()
    {
        // Verify 4 default slot cover cả 24h liên tục (BR-NEW-15, cover 24/7).
        // Morning 06-12, Afternoon 12-17, Evening 17-23, LateNight 23-06 (next day).
        var morning = (CafeSchedule.GetStartTime(TimeSlot.Morning), CafeSchedule.GetEndTime(TimeSlot.Morning));
        var afternoon = (CafeSchedule.GetStartTime(TimeSlot.Afternoon), CafeSchedule.GetEndTime(TimeSlot.Afternoon));
        var evening = (CafeSchedule.GetStartTime(TimeSlot.Evening), CafeSchedule.GetEndTime(TimeSlot.Evening));
        var lateNight = (CafeSchedule.GetStartTime(TimeSlot.LateNight), CafeSchedule.GetEndTime(TimeSlot.LateNight));

        Assert.Equal(new TimeOnly(6, 0), morning.Item1);
        Assert.Equal(new TimeOnly(12, 0), morning.Item2);
        Assert.Equal(new TimeOnly(12, 0), afternoon.Item1);
        Assert.Equal(new TimeOnly(17, 0), afternoon.Item2);
        Assert.Equal(new TimeOnly(17, 0), evening.Item1);
        Assert.Equal(new TimeOnly(23, 0), evening.Item2);
        Assert.Equal(new TimeOnly(23, 0), lateNight.Item1);
        Assert.Equal(new TimeOnly(6, 0), lateNight.Item2);
    }

    [Fact]
    public void BuildScheduledStartEnd_LateNight_ReturnsNextDayEnd()
    {
        var playDate = new DateOnly(2026, 8, 14);
        var (start, end) = CafeSchedule.BuildScheduledStartEnd(playDate, TimeSlot.LateNight);

        Assert.Equal(new DateTime(2026, 8, 14, 23, 0, 0), start);
        Assert.Equal(new DateTime(2026, 8, 15, 6, 0, 0), end); // next day
    }

    [Fact]
    public void BuildScheduledStartEnd_Morning_ReturnsSameDayEnd()
    {
        var playDate = new DateOnly(2026, 8, 14);
        var (start, end) = CafeSchedule.BuildScheduledStartEnd(playDate, TimeSlot.Morning);

        Assert.Equal(new DateTime(2026, 8, 14, 6, 0, 0), start);
        Assert.Equal(new DateTime(2026, 8, 14, 12, 0, 0), end);
    }

    [Fact]
    public void GetDurationMinutes_LateNight_Returns420Minutes()
    {
        // 23:00 - 06:00 next day = 7 hours = 420 minutes
        Assert.Equal(420, CafeSchedule.GetDurationMinutes(TimeSlot.LateNight));
    }

    [Fact]
    public void GetDurationMinutes_Morning_Returns360Minutes()
    {
        // 06:00 - 12:00 = 6 hours = 360 minutes
        Assert.Equal(360, CafeSchedule.GetDurationMinutes(TimeSlot.Morning));
    }

    [Fact]
    public void GetDurationMinutes_Afternoon_Returns300Minutes()
    {
        // 12:00 - 17:00 = 5 hours = 300 minutes
        Assert.Equal(300, CafeSchedule.GetDurationMinutes(TimeSlot.Afternoon));
    }

    [Fact]
    public void GetDurationMinutes_Evening_Returns360Minutes()
    {
        // 17:00 - 23:00 = 6 hours = 360 minutes
        Assert.Equal(360, CafeSchedule.GetDurationMinutes(TimeSlot.Evening));
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_LateNightNullEnd_ReturnsNextDayEnd()
    {
        // preferredEnd = null → dùng default 06:00 → phải là next day
        var playDate = new DateOnly(2026, 8, 14);
        var (start, end) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, TimeSlot.LateNight, null, null);

        Assert.Equal(new DateTime(2026, 8, 14, 23, 0, 0), start);
        Assert.Equal(new DateTime(2026, 8, 15, 6, 0, 0), end); // next day!
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_LateNightWithPreferredEnd_WrapsToNextDay()
    {
        // LateNight với preferredEnd < preferredStart → wrap-around sang ngày hôm sau.
        var playDate = new DateOnly(2026, 8, 14);
        var (start, end) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, TimeSlot.LateNight, new TimeOnly(23, 30), new TimeOnly(4, 0));

        Assert.Equal(new DateTime(2026, 8, 14, 23, 30, 0), start);
        Assert.Equal(new DateTime(2026, 8, 15, 4, 0, 0), end); // next day khi wrap
    }

    [Theory]
    [InlineData(23, 0, 3, 0, true)]   // 23:00-03:00 valid
    [InlineData(0, 0, 5, 0, true)]    // 00:00-05:00 valid
    [InlineData(10, 0, 14, 0, false)]  // 10:00 invalid (not in 23:00-06:00)
    public void ValidatePreferredTimeRange_LateNight_ReturnsExpected(
        int startHour, int startMin, int endHour, int endMin, bool expectedValid)
    {
        var preferredStart = new TimeOnly(startHour, startMin);
        var preferredEnd = new TimeOnly(endHour, endMin);

        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            TimeSlot.LateNight, preferredStart, preferredEnd);

        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void ValidatePreferredTimeRange_LateNight_EndBeforeStart_ReturnsFalse()
    {
        // End time không được <= start time
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            TimeSlot.LateNight, new TimeOnly(3, 0), new TimeOnly(2, 0));

        Assert.False(isValid);
        Assert.Contains("lớn hơn", error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_Evening_SameDay_Works()
    {
        // Verify các slot same-day không bị ảnh hưởng
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            TimeSlot.Evening, new TimeOnly(19, 0), new TimeOnly(22, 0));

        Assert.True(isValid);
    }

    // ===== Boundary tests cho tất cả 4 TimeSlot =====

    [Theory]
    // Morning (06:00 - 12:00)
    [InlineData(TimeSlot.Morning, 6, 0, true)]   // 06:00 = start
    [InlineData(TimeSlot.Morning, 9, 0, true)]   // middle
    [InlineData(TimeSlot.Morning, 12, 0, true)]  // 12:00 = end
    [InlineData(TimeSlot.Morning, 5, 59, false)] // before start
    [InlineData(TimeSlot.Morning, 12, 1, false)] // after end
    [InlineData(TimeSlot.Morning, 13, 0, false)] // after end
    [InlineData(TimeSlot.Morning, 17, 0, false)] // Afternoon start → không thuộc Morning
    [InlineData(TimeSlot.Morning, 23, 0, false)] // LateNight start → không thuộc Morning
    // Afternoon (12:00 - 17:00)
    [InlineData(TimeSlot.Afternoon, 12, 0, true)] // 12:00 = start
    [InlineData(TimeSlot.Afternoon, 14, 0, true)]
    [InlineData(TimeSlot.Afternoon, 17, 0, true)] // 17:00 = end
    [InlineData(TimeSlot.Afternoon, 11, 59, false)]
    [InlineData(TimeSlot.Afternoon, 17, 1, false)]
    [InlineData(TimeSlot.Afternoon, 6, 0, false)]  // Morning start
    [InlineData(TimeSlot.Afternoon, 23, 0, false)] // LateNight start
    // Evening (17:00 - 23:00)
    [InlineData(TimeSlot.Evening, 17, 0, true)]   // 17:00 = start
    [InlineData(TimeSlot.Evening, 20, 0, true)]
    [InlineData(TimeSlot.Evening, 23, 0, true)]   // 23:00 = end (đặc biệt: trùng với LateNight start)
    [InlineData(TimeSlot.Evening, 16, 59, false)]
    [InlineData(TimeSlot.Evening, 23, 1, false)]
    [InlineData(TimeSlot.Evening, 0, 0, false)]   // 00:00 → không thuộc Evening
    [InlineData(TimeSlot.Evening, 6, 0, false)]   // LateNight early morning
    public void IsPreferredStartTimeValid_AllSlots_Boundaries(
        TimeSlot slot, int hour, int minute, bool expected)
    {
        var result = CafeSchedule.IsPreferredStartTimeValid(slot, new TimeOnly(hour, minute));
        Assert.Equal(expected, result);
    }

    [Theory]
    // Morning zero-duration (boundary bug fix)
    [InlineData(TimeSlot.Morning, 6, 0, 6, 0, false)]   // 06:00-06:00 zero
    [InlineData(TimeSlot.Morning, 12, 0, 12, 0, false)] // 12:00-12:00 zero
    [InlineData(TimeSlot.Morning, 10, 0, 10, 0, false)] // middle zero
    [InlineData(TimeSlot.Morning, 10, 0, 11, 0, true)]  // OK
    // Afternoon zero-duration
    [InlineData(TimeSlot.Afternoon, 12, 0, 12, 0, false)]
    [InlineData(TimeSlot.Afternoon, 17, 0, 17, 0, false)]
    [InlineData(TimeSlot.Afternoon, 14, 0, 15, 0, true)]
    // Evening zero-duration (đặc biệt: Evening 23:00 = LateNight 23:00)
    [InlineData(TimeSlot.Evening, 17, 0, 17, 0, false)]
    [InlineData(TimeSlot.Evening, 23, 0, 23, 0, false)] // zero-duration at boundary
    [InlineData(TimeSlot.Evening, 19, 0, 22, 0, true)]
    public void ValidatePreferredTimeRange_ZeroDuration_ReturnsFalse(
        TimeSlot slot, int startH, int startM, int endH, int endM, bool expected)
    {
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            slot, new TimeOnly(startH, startM), new TimeOnly(endH, endM));
        Assert.Equal(expected, isValid);
    }

    [Theory]
    // Morning out-of-range preferredStart
    [InlineData(TimeSlot.Morning, 5, 0, 10, 0, false)]   // start before slot
    [InlineData(TimeSlot.Morning, 13, 0, 14, 0, false)]  // start after slot
    [InlineData(TimeSlot.Morning, 6, 0, 13, 0, false)]   // end after slot end
    [InlineData(TimeSlot.Morning, 11, 0, 12, 0, true)]   // OK
    // Afternoon
    [InlineData(TimeSlot.Afternoon, 11, 0, 14, 0, false)]
    [InlineData(TimeSlot.Afternoon, 18, 0, 19, 0, false)]
    [InlineData(TimeSlot.Afternoon, 16, 0, 17, 0, true)]
    // Evening
    [InlineData(TimeSlot.Evening, 16, 0, 20, 0, false)]
    [InlineData(TimeSlot.Evening, 23, 30, 22, 0, false)] // end < start same-day
    [InlineData(TimeSlot.Evening, 21, 0, 23, 0, true)]
    public void ValidatePreferredTimeRange_OutOfRange_ReturnsFalse(
        TimeSlot slot, int startH, int startM, int endH, int endM, bool expected)
    {
        var (isValid, _) = CafeSchedule.ValidatePreferredTimeRange(
            slot, new TimeOnly(startH, startM), new TimeOnly(endH, endM));
        Assert.Equal(expected, isValid);
    }

    [Fact]
    public void IsPreferredStartTimeValid_EveningBoundary_23_IsValid()
    {
        // Edge case: 23:00 vừa là Evening.endTime vừa là LateNight.startTime.
        // Với TimeSlot.Evening → 23:00 = slotEnd → valid.
        // Với TimeSlot.LateNight → 23:00 = slotStart → valid.
        // Đây là intentional overlap - FE nên validate trước khi cho user chọn.
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.Evening, new TimeOnly(23, 0)));
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(23, 0)));
    }

    [Fact]
    public void IsPreferredStartTimeValid_LateNightBoundary_ReturnsExpected()
    {
        // LateNight 23:00-06:00 (overnight)
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(23, 0)));   // start boundary
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(23, 59)));  // late evening
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(0, 0)));     // midnight
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(3, 0)));     // middle
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(6, 0)));    // end boundary
        Assert.False(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(6, 1)));   // after end
        Assert.False(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(12, 0)));  // noon (middle of day)
        Assert.False(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.LateNight, new TimeOnly(22, 59)));  // just before slot start
    }
}
