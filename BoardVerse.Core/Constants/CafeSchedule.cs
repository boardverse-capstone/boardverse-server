namespace BoardVerse.Core.Constants;

/// <summary>
/// Lịch mặc định cho cafe.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot enum - dùng OpenTime/CloseTime trực tiếp.
/// </summary>
public static class CafeSchedule
{
    /// <summary>Giờ mở cửa mặc định: 06:00.</summary>
    public static readonly TimeOnly DefaultOpenTime = new(6, 0);

    /// <summary>Giờ đóng cửa mặc định: 23:00.</summary>
    public static readonly TimeOnly DefaultCloseTime = new(23, 0);

    /// <summary>
    /// Validate preferredStartTime + preferredEndTime hợp lệ.
    /// </summary>
    public static (bool isValid, string? error) ValidatePreferredTimeRange(
        TimeOnly preferredStart,
        TimeOnly preferredEnd)
    {
        if (preferredEnd <= preferredStart)
        {
            return (false, "End time phải lớn hơn start time.");
        }

        if (preferredStart < DefaultOpenTime)
        {
            return (false, $"Start time không được trước giờ mở cửa ({DefaultOpenTime:HH:mm}).");
        }

        if (preferredEnd > DefaultCloseTime)
        {
            return (false, $"End time không được sau giờ đóng cửa ({DefaultCloseTime:HH:mm}).");
        }

        return (true, null);
    }

    /// <summary>
    /// Helper: build ScheduledStartTime + ScheduledEndTime (DateTime) từ user input.
    /// </summary>
    public static (DateTime scheduledStart, DateTime scheduledEnd) BuildScheduledStartEndFromPreferred(
        DateOnly playDate,
        TimeOnly preferredStart,
        TimeOnly preferredEnd)
    {
        return (
            playDate.ToDateTime(preferredStart),
            playDate.ToDateTime(preferredEnd)
        );
    }
}
