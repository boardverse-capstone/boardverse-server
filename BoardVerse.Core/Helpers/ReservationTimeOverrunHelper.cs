namespace BoardVerse.Core.Helpers;

/// <summary>
/// Phase 4 / EC-10 (§7.1 doc <c>time-slot-fixed-end-design.md</c>):
/// Tính cảnh báo "Game có thể không kết thúc trước khi TimeSlot hết" cho POS UI.
///
/// Công thức:
/// <list type="bullet">
///   <item><description><c>TimeSlotRemainingMinutes = max(0, ceil((Reservation.ScheduledEndTime - now).TotalMinutes))</c></description></item>
///   <item><description><c>TimeOverrunWarning = EstimatedRemainingMinutes &gt; TimeSlotRemainingMinutes</c> AND <c>TimeSlotRemainingMinutes &gt; 0</c></description></item>
/// </list>
///
/// Áp dụng cho ActiveSession link với Reservation (ReservationId != null).
/// ActiveSession walk-in hoặc legacy booking không có ScheduledEndTime chính xác → warning = false.
/// </summary>
public static class ReservationTimeOverrunHelper
{
    /// <summary>
    /// Tính toán warning flag từ scheduled end time + estimated remaining minutes.
    /// </summary>
    /// <param name="scheduledEndTimeUtc">
    /// Reservation.ScheduledEndTime (UTC) — null nếu session không thuộc Reservation flow.
    /// </param>
    /// <param name="estimatedRemainingMinutes">
    /// Số phút còn lại ước tính cho game (tính từ GameTemplate.PlayTime - ElapsedMinutes).
    /// </param>
    /// <param name="nowUtc">Thời điểm hiện tại (UTC). Mặc định = DateTime.UtcNow.</param>
    /// <returns>Tuple (warning flag, TimeSlot remaining minutes).</returns>
    public static (bool TimeOverrunWarning, int TimeSlotRemainingMinutes) Compute(
        DateTime? scheduledEndTimeUtc,
        int estimatedRemainingMinutes,
        DateTime? nowUtc = null)
    {
        if (scheduledEndTimeUtc is null)
        {
            // Session không thuộc Reservation flow → không áp dụng warning.
            return (false, 0);
        }

        var now = nowUtc ?? DateTime.UtcNow;
        var remaining = scheduledEndTimeUtc.Value - now;
        var timeSlotRemaining = remaining.TotalMinutes <= 0
            ? 0
            : (int)Math.Ceiling(remaining.TotalMinutes);

        // Warning: game cần > phút TimeSlot còn lại.
        // Bỏ qua khi TimeSlot đã hết (tránh false positive khi session đang grace).
        var warning = timeSlotRemaining > 0 && estimatedRemainingMinutes > timeSlotRemaining;

        return (warning, timeSlotRemaining);
    }
}
