using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Constants;

/// <summary>
/// Lịch cố định cho 4 timeSlot (BR-NEW-15 §7.1).
/// Enum là cố định, không cho phép cafe thêm khung giờ mới —
/// chỉ override tên hiển thị qua i18n / cafe config UI.
/// </summary>
public static class CafeSchedule
{
    public static TimeOnly GetStartTime(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(9, 0),
        TimeSlot.Afternoon => new TimeOnly(13, 0),
        TimeSlot.Evening => new TimeOnly(18, 0),
        TimeSlot.Night => new TimeOnly(19, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    public static TimeOnly GetEndTime(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(13, 0),
        TimeSlot.Afternoon => new TimeOnly(18, 0),
        TimeSlot.Evening => new TimeOnly(23, 0),
        TimeSlot.Night => new TimeOnly(24, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    /// <summary>
    /// BR-NEW-15b: preferredStartTime phải nằm trong [startTime, endTime].
    /// </summary>
    public static bool IsPreferredStartTimeValid(TimeSlot slot, TimeOnly? preferred)
    {
        if (preferred is null)
        {
            return true;
        }

        var start = GetStartTime(slot);
        var end = GetEndTime(slot);
        return preferred >= start && preferred <= end;
    }
}
