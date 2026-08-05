using BoardVerse.Core.Constants;
using BoardVerse.Core.Enum;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="CafeSchedule"/> — default time slot windows (BR-NEW-15, cập nhật cover 24h).
/// </summary>
public class CafeScheduleTests
{
    [Fact]
    public void GetStartTime_Morning_Returns8AM()
    {
        Assert.Equal(new TimeOnly(8, 0), CafeSchedule.GetStartTime(TimeSlot.Morning));
    }

    [Fact]
    public void GetEndTime_Morning_Returns1PM()
    {
        Assert.Equal(new TimeOnly(13, 0), CafeSchedule.GetEndTime(TimeSlot.Morning));
    }

    [Fact]
    public void GetStartTime_Afternoon_Returns1PM()
    {
        Assert.Equal(new TimeOnly(13, 0), CafeSchedule.GetStartTime(TimeSlot.Afternoon));
    }

    [Fact]
    public void GetEndTime_Evening_ReturnsMidnight()
    {
        // 24:00 = 00:00 ngày hôm sau (TimeOnly không nhận hour=24)
        Assert.Equal(new TimeOnly(0, 0), CafeSchedule.GetEndTime(TimeSlot.Evening));
    }

    [Fact]
    public void GetStartTime_Night_ReturnsMidnight()
    {
        Assert.Equal(new TimeOnly(0, 0), CafeSchedule.GetStartTime(TimeSlot.Night));
    }

    [Fact]
    public void GetEndTime_Night_Returns8AM_NextDay()
    {
        Assert.Equal(new TimeOnly(8, 0), CafeSchedule.GetEndTime(TimeSlot.Night));
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
        Assert.False(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.Morning, new TimeOnly(7, 0)));
    }

    [Fact]
    public void IsPreferredStartTimeValid_Night0200_ReturnsTrue()
    {
        Assert.True(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.Night, new TimeOnly(2, 0)));
    }

    [Fact]
    public void IsPreferredStartTimeValid_Night0900_ReturnsFalse()
    {
        Assert.False(CafeSchedule.IsPreferredStartTimeValid(TimeSlot.Night, new TimeOnly(9, 0)));
    }

    [Fact]
    public void TimeSlots_CoverFull24Hours()
    {
        // Verify 4 default slot cover cả 24h liên tục (BR-NEW-15 §7.1, cập nhật cover 24h).
        // Morning 08-13, Afternoon 13-18, Evening 18-24, Night 00-08.
        var morning = (CafeSchedule.GetStartTime(TimeSlot.Morning), CafeSchedule.GetEndTime(TimeSlot.Morning));
        var afternoon = (CafeSchedule.GetStartTime(TimeSlot.Afternoon), CafeSchedule.GetEndTime(TimeSlot.Afternoon));
        var evening = (CafeSchedule.GetStartTime(TimeSlot.Evening), CafeSchedule.GetEndTime(TimeSlot.Evening));
        var night = (CafeSchedule.GetStartTime(TimeSlot.Night), CafeSchedule.GetEndTime(TimeSlot.Night));

        Assert.Equal(new TimeOnly(8, 0), morning.Item1);
        Assert.Equal(new TimeOnly(13, 0), morning.Item2);
        Assert.Equal(new TimeOnly(13, 0), afternoon.Item1);
        Assert.Equal(new TimeOnly(18, 0), afternoon.Item2);
        Assert.Equal(new TimeOnly(18, 0), evening.Item1);
        Assert.Equal(new TimeOnly(0, 0), evening.Item2);
        Assert.Equal(new TimeOnly(0, 0), night.Item1);
        Assert.Equal(new TimeOnly(8, 0), night.Item2);
    }
}
