using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Constants;

/// <summary>
/// Lịch mặc định cho 4 timeSlot (BR-NEW-15 §7.1, đồng bộ docs/time-slot-fixed-end-design (1).md §13).
/// Enum là cố định, không cho phép cafe thêm khung giờ mới.
/// Cafe có thể override start/end/IsClosed qua <c>CafeScheduleOverride</c>.
/// </summary>
/// <remarks>
/// Mapping mặc định (BR-RES-07/08/09 — start + end bắt buộc, end cùng ngày, end auto-resolve):
/// <list type="bullet">
/// <item><description><c>Morning</c>    09:00 – 13:00</description></item>
/// <item><description><c>Afternoon</c> 13:00 – 18:00</description></item>
/// <item><description><c>Evening</c>   18:00 – 23:00</description></item>
/// <item><description><c>Night</c>     19:00 – 24:00 (cùng playDate — BR-RES-08)</description></item>
/// </list>
/// </remarks>
public static class CafeSchedule
{
    /// <summary>
    /// BR-RES-09 + BR-NEW-15: timeSlot Morning bắt đầu lúc 09:00 (thay vì 08:00 như bản draft cũ).
    /// </summary>
    public static TimeOnly GetStartTime(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(9, 0),
        TimeSlot.Afternoon => new TimeOnly(13, 0),
        TimeSlot.Evening => new TimeOnly(18, 0),
        TimeSlot.Night => new TimeOnly(19, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    /// <summary>
    /// BR-RES-07/08/09: endTime auto-resolve từ timeSlot, cùng ngày với startTime (BR-RES-08).
    /// Night 24:00 encode bằng <see cref="TimeOnly.MaxValue"/> = 23:59:59.9999999.
    /// Service xử lý Night 24:00 = cùng playDate (không cộng thêm 1 ngày).
    /// </summary>
    public static TimeOnly GetEndTime(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(13, 0),
        TimeSlot.Afternoon => new TimeOnly(18, 0),
        TimeSlot.Evening => new TimeOnly(23, 0),
        TimeSlot.Night => new TimeOnly(23, 59, 59), // 23:59:59 ≈ 24:00, cùng playDate theo BR-RES-08
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    /// <summary>
    /// BR-RES-07/08/09 helper: Build (startTime, endTime) DateTime từ playDate + timeSlot.
    /// </summary>
    /// <param name="playDate">DateOnly (BR-NEW-04 — chỉ ngày, không giờ).</param>
    /// <param name="slot">TimeSlot enum.</param>
    /// <returns>(startTime, endTime) cùng ngày playDate. Night endTime = 23:59:59.9999999 của playDate.</returns>
    public static (DateTime startTime, DateTime endTime) BuildScheduledStartEnd(DateOnly playDate, TimeSlot slot)
    {
        var start = playDate.ToDateTime(GetStartTime(slot));
        var end = playDate.ToDateTime(GetEndTime(slot));
        return (start, end);
    }

    /// <summary>
    /// BR-NEW-15b: preferredStartTime phải nằm trong [startTime, endTime].
    /// Lưu ý: tất cả 4 slot đều same-day → không còn overnight logic.
    /// </summary>
    public static bool IsPreferredStartTimeValid(TimeSlot slot, TimeOnly? preferred)
    {
        if (preferred is null)
        {
            return true;
        }

        return preferred >= GetStartTime(slot) && preferred <= GetEndTime(slot);
    }

    /// <summary>
    /// BR-RESV-02: Validate preferred start + end time nằm trong slot range.
    /// Nếu preferredStart/End là null → skip validation (API validate bắt buộc riêng).
    /// Returns (isValid, errorMessage).
    /// </summary>
    public static (bool isValid, string? error) ValidatePreferredTimeRange(
        TimeSlot slot,
        TimeOnly? preferredStart,
        TimeOnly? preferredEnd)
    {
        // API bắt buộc nhập → nếu null → coi như hợp lệ (để API validate)
        if (preferredStart == null || preferredEnd == null)
        {
            return (true, null);
        }

        var slotStart = GetStartTime(slot);
        var slotEnd = GetEndTime(slot);

        if (preferredStart < slotStart || preferredStart > slotEnd)
        {
            return (false, $"Start time phải nằm trong khung giờ đã chọn ({slotStart:HH:mm} - {slotEnd:HH:mm}).");
        }

        if (preferredEnd < preferredStart)
        {
            return (false, "End time phải lớn hơn start time.");
        }

        if (preferredEnd > slotEnd)
        {
            return (false, $"End time không được vượt quá {slotEnd:HH:mm} của khung giờ đã chọn.");
        }

        return (true, null);
    }

    /// <summary>
    /// Helper: build ScheduledStartTime + ScheduledEndTime (DateTime) từ user input.
    /// Nếu preferredStart/End là null → dùng slot start/end mặc định.
    /// </summary>
    public static (DateTime scheduledStart, DateTime scheduledEnd) BuildScheduledStartEndFromPreferred(
        DateOnly playDate,
        TimeSlot slot,
        TimeOnly? preferredStart,
        TimeOnly? preferredEnd)
    {
        var start = playDate.ToDateTime(preferredStart ?? GetStartTime(slot));
        var end = playDate.ToDateTime(preferredEnd ?? GetEndTime(slot));
        return (start, end);
    }

    /// <summary>
    /// Helper: duration phút cho từng slot (dùng cho refund calculation).
    /// </summary>
    public static int GetDurationMinutes(TimeSlot slot)
    {
        var start = GetStartTime(slot);
        var end = GetEndTime(slot);
        return (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
    }
}