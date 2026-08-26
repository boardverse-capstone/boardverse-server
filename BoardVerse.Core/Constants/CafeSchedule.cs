using BoardVerse.Core.Messages;

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
    /// Nếu end nhỏ hơn start, end thuộc ngày kế tiếp.
    /// </summary>
    public static (bool isValid, string? error) ValidatePreferredTimeRange(
        TimeOnly preferredStart,
        TimeOnly preferredEnd)
    {
        if (preferredEnd == preferredStart)
        {
            return (false, ApiErrorMessages.Reservation.PreferredTimesMustDiffer);
        }

        if (preferredStart < DefaultOpenTime)
        {
            return (false, ApiErrorMessages.Reservation.PreferredStartBeforeOpen(DefaultOpenTime));
        }

        var isOvernight = preferredEnd < preferredStart;
        if (!isOvernight && preferredEnd > DefaultCloseTime)
        {
            return (false, ApiErrorMessages.Reservation.PreferredEndAfterClose(DefaultCloseTime));
        }

        return (true, null);
    }

    /// <summary>
    /// Helper: build ScheduledStartTime + ScheduledEndTime (DateTime) từ user input.
    /// Nếu end nhỏ hơn start, ScheduledEndTime thuộc ngày kế tiếp.
    /// </summary>
    public static (DateTime scheduledStart, DateTime scheduledEnd) BuildScheduledStartEndFromPreferred(
        DateOnly playDate,
        TimeOnly preferredStart,
        TimeOnly preferredEnd)
    {
        var endDate = preferredEnd < preferredStart
            ? playDate.AddDays(1)
            : playDate;

        return (
            playDate.ToDateTime(preferredStart),
            endDate.ToDateTime(preferredEnd)
        );
    }
}
