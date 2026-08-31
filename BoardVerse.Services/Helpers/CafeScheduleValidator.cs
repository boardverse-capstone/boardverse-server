using BoardVerse.Core.Constants;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;

namespace BoardVerse.Services.Helpers;

/// <summary>
/// Shared validation helper cho preferredStartTime/preferredEndTime với CafeScheduleOverride.
/// BR-NEW-15: Validate giờ mở/đóng thực tế của cafe (theo CafeScheduleOverride).
/// </summary>
public static class CafeScheduleValidator
{
    /// <summary>
    /// Validate preferredStartTime/preferredEndTime nằm trong giờ mở/đóng thực tế của cafe.
    /// Xử lý overnight: nếu preferredEnd < preferredStart, validate preferredEnd với schedule ngày kế tiếp.
    /// </summary>
    /// <param name="scheduleResolver">IScheduleResolver để lấy giờ resolved.</param>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="playDate">Ngày bắt đầu.</param>
    /// <param name="preferredStart">Giờ bắt đầu ưu tiên.</param>
    /// <param name="preferredEnd">Giờ kết thúc ưu tiên.</param>
    /// <param name="cancellationToken">CancellationToken.</param>
    /// <exception cref="BadRequestException">Nếu giờ không hợp lệ theo schedule thực tế.</exception>
    public static async Task ValidatePreferredTimesWithCafeScheduleAsync(
        IScheduleResolver scheduleResolver,
        Guid cafeId,
        DateOnly playDate,
        TimeOnly preferredStart,
        TimeOnly preferredEnd,
        CancellationToken cancellationToken = default)
    {
        // 1. Detect overnight session
        var isOvernight = preferredEnd < preferredStart;

        // 2. Resolve giờ mở/đóng thực tế cho ngày bắt đầu (playDate)
        var startDaySchedule = await scheduleResolver.ResolveAsync(cafeId, playDate, cancellationToken);

        // 3. Nếu cafe đóng cửa ngày bắt đầu → reject ngay
        if (startDaySchedule.IsClosed)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.CafeScheduleClosedForPlayDate);
        }

        // 4. Validate preferredStart >= OpenTime (ngày bắt đầu)
        if (preferredStart < startDaySchedule.OpenTime)
        {
            throw new BadRequestException(
                ApiErrorMessages.Reservation.PreferredStartBeforeOpen(startDaySchedule.OpenTime));
        }

        // 5. Validate preferredEnd
        if (isOvernight)
        {
            // Overnight: preferredEnd thuộc ngày kế tiếp → validate với schedule ngày playDate+1
            var nextDay = playDate.AddDays(1);
            var nextDaySchedule = await scheduleResolver.ResolveAsync(cafeId, nextDay, cancellationToken);

            // Nếu cafe đóng cửa ngày kế tiếp → reject
            if (nextDaySchedule.IsClosed)
            {
                throw new BadRequestException(
                    $"Quán đóng cửa vào ngày {nextDay:dd/MM/yyyy} (ngày kết thúc của phiên qua đêm). Vui lòng chọn ngày khác.");
            }

            // preferredEnd phải <= CloseTime của ngày kế tiếp
            if (preferredEnd > nextDaySchedule.CloseTime)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Reservation.PreferredEndAfterClose(nextDaySchedule.CloseTime));
            }
        }
        else
        {
            // Same-day: preferredEnd phải <= CloseTime của ngày bắt đầu
            if (preferredEnd > startDaySchedule.CloseTime)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Reservation.PreferredEndAfterClose(startDaySchedule.CloseTime));
            }
        }
    }
}
