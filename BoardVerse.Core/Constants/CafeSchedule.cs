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
/// <item><description><c>Morning</c>    06:00 – 12:00</description></item>
/// <item><description><c>Afternoon</c>  12:00 – 17:00</description></item>
/// <item><description><c>Evening</c>   17:00 – 23:00</description></item>
/// <item><description><c>LateNight</c>  23:00 – 06:00 (next day) — cover 24/7</description></item>
/// </list>
/// </remarks>
public static class CafeSchedule
{
    /// <summary>
    /// BR-RES-09 + BR-NEW-15: timeSlot Morning bắt đầu lúc 06:00.
    /// LateNight bắt đầu 23:00 ngày hôm trước, kết thúc 06:00 ngày hôm sau.
    /// </summary>
    public static TimeOnly GetStartTime(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(6, 0),
        TimeSlot.Afternoon => new TimeOnly(12, 0),
        TimeSlot.Evening => new TimeOnly(17, 0),
        TimeSlot.LateNight => new TimeOnly(23, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    /// <summary>
    /// BR-RES-07/08/09: endTime auto-resolve từ timeSlot.
    /// LateNight 23:00-06:00: endTime là 06:00 ngày hôm sau (khác playDate).
    /// Các slot khác: cùng ngày với startTime.
    /// </summary>
    public static TimeOnly GetEndTime(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(12, 0),
        TimeSlot.Afternoon => new TimeOnly(17, 0),
        TimeSlot.Evening => new TimeOnly(23, 0),
        TimeSlot.LateNight => new TimeOnly(6, 0), // 06:00 ngày hôm sau
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    /// <summary>
    /// BR-RES-07/08/09 helper: Build (startTime, endTime) DateTime từ playDate + timeSlot.
    /// LateNight: endTime = playDate + 1 day + 06:00 (overnight session).
    /// Các slot khác: endTime cùng ngày với playDate.
    /// </summary>
    /// <param name="playDate">DateOnly (BR-NEW-04 — chỉ ngày, không giờ).</param>
    /// <param name="slot">TimeSlot enum.</param>
    /// <returns>(startTime, endTime). LateNight: endTime là next day.</returns>
    public static (DateTime startTime, DateTime endTime) BuildScheduledStartEnd(DateOnly playDate, TimeSlot slot)
    {
        var start = playDate.ToDateTime(GetStartTime(slot));
        var end = slot == TimeSlot.LateNight
            ? playDate.AddDays(1).ToDateTime(GetEndTime(slot))
            : playDate.ToDateTime(GetEndTime(slot));
        return (start, end);
    }

    /// <summary>
    /// BR-NEW-15b: preferredStartTime phải nằm trong [startTime, endTime].
    /// LateNight là overnight (23:00-06:00), start > end → validate khác.
    /// </summary>
    public static bool IsPreferredStartTimeValid(TimeSlot slot, TimeOnly? preferred)
    {
        if (preferred is null)
        {
            return true;
        }

        var slotStart = GetStartTime(slot);
        var slotEnd = GetEndTime(slot);

        // LateNight: overnight slot (23:00-06:00), start > end
        if (slot == TimeSlot.LateNight)
        {
            return preferred >= slotStart || preferred <= slotEnd;
        }

        return preferred >= slotStart && preferred <= slotEnd;
    }

    /// <summary>
    /// BR-RESV-02: Validate preferred start + end time nằm trong slot range.
    /// Nếu preferredStart/End là null → skip validation (API validate bắt buộc riêng).
    /// LateNight là overnight: preferredStart >= 23:00 OR <= 06:00, preferredEnd > preferredStart (wrap-around allowed).
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

        // LateNight: overnight slot (23:00-06:00 next day)
        if (slot == TimeSlot.LateNight)
        {
            // preferredStart phải >= 23:00 OR <= 06:00
            if (preferredStart < slotStart && preferredStart > slotEnd)
            {
                return (false, $"Start time phải nằm trong khung giờ đã chọn (23:00 - 06:00).");
            }

            // preferredEnd phải >= 23:00 OR <= 06:00
            if (preferredEnd < slotStart && preferredEnd > slotEnd)
            {
                return (false, $"End time phải nằm trong khung giờ đã chọn (23:00 - 06:00).");
            }

            // Validate: nếu cả start và end đều nằm trong khoảng 00:00-06:00 (early morning),
            // thì end phải > start. Nếu start >= 23:00 và end <= 06:00 → OK (end là next day).
            var startHour = preferredStart.Value.Hour;
            var endHour = preferredEnd.Value.Hour;
            var startIsEarlyMorning = startHour >= 0 && startHour <= 6;
            var endIsEarlyMorning = endHour >= 0 && endHour <= 6;

            // Edge case: cả start và end đều >= 23:00 (cùng buổi tối) → end phải > start.
            // Nếu không check, BuildScheduledStartEndFromPreferred sẽ trả về end < start (same day).
            var startIsLateNight = preferredStart.Value >= slotStart; // >= 23:00
            var endIsLateNight = preferredEnd.Value >= slotStart;     // >= 23:00
            if (startIsLateNight && endIsLateNight && preferredEnd <= preferredStart)
            {
                return (false, "End time phải lớn hơn start time khi cả 2 đều trong khung 23:00-23:59.");
            }

            if (endIsEarlyMorning && startIsEarlyMorning && preferredEnd <= preferredStart)
            {
                return (false, "End time phải lớn hơn start time khi cả 2 đều trong 00:00-06:00.");
            }

            return (true, null);
        }

        // Các slot same-day thông thường
        if (preferredStart < slotStart || preferredStart > slotEnd)
        {
            return (false, $"Start time phải nằm trong khung giờ đã chọn ({slotStart:HH:mm} - {slotEnd:HH:mm}).");
        }

        // BR-RESV-02: end phải > start (tránh zero-duration session)
        if (preferredEnd <= preferredStart)
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
    /// LateNight (23:00-06:00 next day): preferredEnd được hiểu là wrap-around:
    ///   - Nếu preferredEnd &lt; preferredStart → end = playDate + 1 day + preferredEnd.
    ///   - Nếu preferredEnd &gt;= preferredStart → end = playDate + preferredEnd (same day).
    /// </summary>
    public static (DateTime scheduledStart, DateTime scheduledEnd) BuildScheduledStartEndFromPreferred(
        DateOnly playDate,
        TimeSlot slot,
        TimeOnly? preferredStart,
        TimeOnly? preferredEnd)
    {
        var start = playDate.ToDateTime(preferredStart ?? GetStartTime(slot));

        DateTime end;
        if (preferredEnd.HasValue)
        {
            // Overnight semantics cho LateNight:
            // preferredStart=23:30, preferredEnd=02:00 → end là ngày hôm sau 02:00.
            if (slot == TimeSlot.LateNight && preferredEnd.Value < (preferredStart ?? GetStartTime(slot)))
            {
                end = playDate.AddDays(1).ToDateTime(preferredEnd.Value);
            }
            else
            {
                end = playDate.ToDateTime(preferredEnd.Value);
            }
        }
        else
        {
            // Dùng default endTime → LateNight là overnight
            end = slot == TimeSlot.LateNight
                ? playDate.AddDays(1).ToDateTime(GetEndTime(slot))
                : playDate.ToDateTime(GetEndTime(slot));
        }

        return (start, end);
    }

    /// <summary>
    /// Helper: duration phút cho từng slot (dùng cho refund calculation).
    /// LateNight: 23:00 → 06:00 next day = 7 tiếng = 420 phút.
    /// </summary>
    public static int GetDurationMinutes(TimeSlot slot)
    {
        var start = GetStartTime(slot);
        var end = GetEndTime(slot);

        // LateNight: overnight → duration = (24*60 - startMinutes) + endMinutes
        if (slot == TimeSlot.LateNight)
        {
            var startMinutes = start.Hour * 60 + start.Minute;
            var endMinutes = end.Hour * 60 + end.Minute;
            return (24 * 60 - startMinutes) + endMinutes; // (1440 - 1380) + 60 = 120... wait
            // 23:00 = 1380 min, 06:00 = 360 min
            // (1440 - 1380) + 360 = 60 + 360 = 420 phút ✓
        }

        return (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
    }
}