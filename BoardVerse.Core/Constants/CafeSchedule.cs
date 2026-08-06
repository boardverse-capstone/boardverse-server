using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Constants;

/// <summary>
/// Lịch mặc định cho 4 timeSlot (BR-NEW-15 §7.1, cập nhật cover 24h).
/// Enum là cố định, không cho phép cafe thêm khung giờ mới.
/// Cafe có thể override start/end/IsClosed qua <c>CafeScheduleOverride</c>.
/// </summary>
/// <remarks>
/// Mapping mặc định (cập nhật cover toàn bộ 24 giờ):
/// <list type="bullet">
/// <item><description><c>Morning</c>    08:00 – 13:00</description></item>
/// <item><description><c>Afternoon</c> 13:00 – 18:00</description></item>
/// <item><description><c>Evening</c>   18:00 – 24:00</description></item>
/// <item><description><c>Night</c>     00:00 – 08:00 (qua đêm, scheduledStart = playDate 00:00, endTime = playDate+1 08:00)</description></item>
/// </list>
/// </remarks>
public static class CafeSchedule
{
    public static TimeOnly GetStartTime(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(8, 0),
        TimeSlot.Afternoon => new TimeOnly(13, 0),
        TimeSlot.Evening => new TimeOnly(18, 0),
        TimeSlot.Night => new TimeOnly(0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    public static TimeOnly GetEndTime(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(13, 0),
        TimeSlot.Afternoon => new TimeOnly(18, 0),
        // Evening kết thúc lúc 24:00 = 00:00 của ngày hôm sau; TimeOnly không nhận hour=24
        // nên encode bằng 00:00 và để logic nghiệp vụ (deadline, end-of-session) hiểu là cuối ngày.
        TimeSlot.Evening => new TimeOnly(0, 0),
        TimeSlot.Night => new TimeOnly(8, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    /// <summary>
    /// BR-NEW-15b: preferredStartTime phải nằm trong [startTime, endTime].
    /// Lưu ý: với <c>Night</c> (00:00 – 08:00), <c>endTime = 08:00</c> nhỏ hơn <c>startTime = 00:00</c>;
    /// logic kiểm tra phải xử lý range qua đêm bằng <see cref="IsPreferredStartTimeValidOvernight"/>.
    /// </summary>
    public static bool IsPreferredStartTimeValid(TimeSlot slot, TimeOnly? preferred)
    {
        if (preferred is null)
        {
            return true;
        }

        return slot switch
        {
            TimeSlot.Night => IsPreferredStartTimeValidOvernight(preferred.Value),
            TimeSlot.Evening => preferred >= new TimeOnly(18, 0) && preferred <= new TimeOnly(23, 59),
            _ => preferred >= GetStartTime(slot) && preferred <= GetEndTime(slot)
        };
    }

    /// <summary>
    /// Xử lý range qua đêm cho slot <c>Night</c> (00:00 – 08:00).
    /// preferredStartTime hợp lệ khi nằm trong khoảng [00:00, 08:00].
    /// </summary>
    private static bool IsPreferredStartTimeValidOvernight(TimeOnly preferred)
    {
        return preferred >= new TimeOnly(0, 0) && preferred <= new TimeOnly(8, 0);
    }
}
