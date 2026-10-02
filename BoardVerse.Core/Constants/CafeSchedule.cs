using BoardVerse.Core.IRepositories;
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
    /// FIX TZ-RESV-01 (2026-10-02): Server timezone mặc định của container Linux là UTC,
    /// nhưng user input (preferredStart/EndTime) là giờ VN local. ScheduledStartTime/EndTime
    /// được build từ <c>playDate.ToDateTime(preferredStart)</c> có Kind=Unspecified, raw ticks
    /// đại diện giờ VN. Khi so sánh trực tiếp với <c>DateTime.UtcNow</c> ở server UTC,
    /// C# treat <c>Unspecified</c> như <c>Local</c> = UTC → sai lệch 7 giờ.
    ///
    /// Dùng helper <see cref="ToUtcAssumingVietnamLocal"/> trước khi so sánh với
    /// <c>DateTime.UtcNow</c> để chuẩn hóa về UTC. Dùng <see cref="ToVietnamLocal"/>
    /// trước khi format error message cho user.
    /// </summary>
    public static readonly TimeZoneInfo VietnamTz = FindVietnamTimeZone();

    private static TimeZoneInfo FindVietnamTimeZone()
    {
        // Windows: "SE Asia Standard Time" (UTC+7, no DST)
        // Linux/Mac: "Asia/Ho_Chi_Minh" (IANA)
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
    }

    /// <summary>
    /// Convert một <see cref="DateTime"/> được build từ user playDate + TimeOnly (Kind=Unspecified,
    /// ngầm hiểu là VN local) sang UTC để so sánh với <c>DateTime.UtcNow</c>.
    ///
    /// Nếu input đã là UTC thì trả nguyên giá trị (convert về UTC).
    /// Nếu input là Local thì convert qua Local timezone của server (cẩn thận nếu server không ở VN).
    /// </summary>
    public static DateTime ToUtcAssumingVietnamLocal(DateTime local)
    {
        return local.Kind switch
        {
            DateTimeKind.Utc => local,
            DateTimeKind.Local => local.ToUniversalTime(),
            // Unspecified: ngầm hiểu là VN local time (theo convention của project).
            _ => TimeZoneInfo.ConvertTimeToUtc(local, VietnamTz)
        };
    }

    /// <summary>
    /// Convert UTC <see cref="DateTime"/> sang giờ VN local để hiển thị cho user (error message,
    /// notification payload). Trả về Kind=Unspecified để giữ format hiển thị theo VN.
    /// </summary>
    public static DateTime ToVietnamLocal(DateTime utc)
    {
        var converted = TimeZoneInfo.ConvertTimeFromUtc(
            utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc,
            VietnamTz);
        return DateTime.SpecifyKind(converted, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Validate preferredStartTime + preferredEndTime hợp lệ.
    /// Nếu end nhỏ hơn start, end thuộc ngày kế tiếp.
    ///
    /// <para>
    /// <b>Lưu ý quan trọng (2026-10-02):</b> Method này dùng <see cref="DefaultOpenTime"/>
    /// / <see cref="DefaultCloseTime"/> cứng — KHÔNG tôn trọng <c>CafeScheduleOverride</c>.
    /// Chỉ phù hợp cho smoke test hoặc kiểm tra boundary mặc định 06:00–23:00.
    /// </para>
    ///
    /// <para>
    /// Production code (CreateQuoteAsync, ConfirmAsync, …) phải dùng
    /// <see cref="ValidatePreferredTimeRangeAsync"/> thay thế — method đó resolve giờ thực tế
    /// của cafe (qua IScheduleResolver) để hỗ trợ 24/7, override ngày lễ, giờ riêng từng quán.
    /// </para>
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
    /// Validate preferredStartTime + preferredEndTime với giờ mở/đóng thực tế của cafe
    /// (resolve qua <see cref="IScheduleResolver"/>). Hỗ trợ 24/7: nếu cafe override
    /// <c>openTime = 00:00</c>, <c>closeTime = 23:59</c> thì mọi preferredStart/End đều pass.
    ///
    /// <para>
    /// FIX 24/7 (2026-10-02): thay thế cho <see cref="ValidatePreferredTimeRange"/> trong
    /// production code. Sync method vẫn giữ để backwards-compatible với unit test cũ, nhưng
    /// KHÔNG dùng cho flow reservation/confirm nữa.
    /// </para>
    ///
    /// <para>
    /// Xử lý overnight: nếu <paramref name="preferredEnd"/> &lt; <paramref name="preferredStart"/>,
    /// validate <paramref name="preferredEnd"/> với schedule ngày <c>playDate + 1</c>.
    /// </para>
    /// </summary>
    /// <param name="scheduleResolver">Resolver để lấy giờ thực tế của cafe theo ngày.</param>
    /// <param name="cafeId">Mã cafe.</param>
    /// <param name="playDate">Ngày chơi.</param>
    /// <param name="preferredStart">Giờ bắt đầu.</param>
    /// <param name="preferredEnd">Giờ kết thúc.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>(isValid, errorMessage?).</returns>
    public static async Task<(bool isValid, string? error)> ValidatePreferredTimeRangeAsync(
        IScheduleResolver scheduleResolver,
        Guid cafeId,
        DateOnly playDate,
        TimeOnly preferredStart,
        TimeOnly preferredEnd,
        CancellationToken cancellationToken = default)
    {
        // 1. Zero duration check (giữ nguyên logic method sync).
        if (preferredEnd == preferredStart)
        {
            return (false, ApiErrorMessages.Reservation.PreferredTimesMustDiffer);
        }

        var isOvernight = preferredEnd < preferredStart;

        // 2. Resolve schedule ngày bắt đầu.
        var startDaySchedule = await scheduleResolver
            .ResolveAsync(cafeId, playDate, cancellationToken)
            .ConfigureAwait(false);

        if (startDaySchedule.IsClosed)
        {
            return (false, ApiErrorMessages.Reservation.CafeScheduleClosedForPlayDate);
        }

        // 3. preferredStart phải nằm trong [OpenTime, CloseTime] của ngày bắt đầu.
        if (preferredStart < startDaySchedule.OpenTime)
        {
            return (false, ApiErrorMessages.Reservation.PreferredStartBeforeOpen(startDaySchedule.OpenTime));
        }

        if (isOvernight)
        {
            // Validate preferredEnd với schedule ngày kế tiếp.
            var nextDay = playDate.AddDays(1);
            var nextDaySchedule = await scheduleResolver
                .ResolveAsync(cafeId, nextDay, cancellationToken)
                .ConfigureAwait(false);

            if (nextDaySchedule.IsClosed)
            {
                return (false,
                    $"Quán đóng cửa vào ngày {nextDay:dd/MM/yyyy} (ngày kết thúc của phiên qua đêm). Vui lòng chọn ngày khác.");
            }

            if (preferredEnd > nextDaySchedule.CloseTime)
            {
                return (false, ApiErrorMessages.Reservation.PreferredEndAfterClose(nextDaySchedule.CloseTime));
            }
        }
        else
        {
            // Same-day: preferredEnd <= CloseTime ngày bắt đầu.
            if (preferredEnd > startDaySchedule.CloseTime)
            {
                return (false, ApiErrorMessages.Reservation.PreferredEndAfterClose(startDaySchedule.CloseTime));
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Helper: build ScheduledStartTime + ScheduledEndTime (DateTime, <b>Kind=Utc</b>) từ
    /// user input playDate + preferredStart/End.
    ///
    /// <para>
    /// FIX TZ-DT-UTC-01 (2026-10-02): trước đây trả về <c>Kind=Unspecified</c> với raw ticks
    /// = VN local time. Điều này khiến JSON serialize ra dạng "2026-10-02T20:45:00" (không có
    /// timezone) — FE không biết convert như thế nào, dẫn đến hiển thị sai giờ.
    /// </para>
    ///
    /// <para>
    /// Sau fix: convert sang UTC ngay tại đây (Kind=Utc, raw ticks = UTC). JSON serialize
    /// ra dạng "2026-10-02T13:45:00Z" — FE chỉ cần <c>new Date(...).toLocaleString('vi-VN')</c>
    /// là ra đúng 20:45 giờ VN.
    /// </para>
    ///
    /// <para>
    /// Lưu ý cho consumer: nếu cần hiển thị giờ VN (format <c>:HH:mm</c> cho log/error message,
    /// hay truyền vào <c>TimeOnly.FromDateTime</c> cho inventory lookup), phải convert ngược
    /// qua <see cref="ToVietnamLocal"/> trước.
    /// </para>
    ///
    /// Nếu end nhỏ hơn start, ScheduledEndTime thuộc ngày kế tiếp (overnight session).
    /// </summary>
    public static (DateTime scheduledStart, DateTime scheduledEnd) BuildScheduledStartEndFromPreferred(
        DateOnly playDate,
        TimeOnly preferredStart,
        TimeOnly preferredEnd)
    {
        var endDate = preferredEnd < preferredStart
            ? playDate.AddDays(1)
            : playDate;

        // Bước 1: build với raw DateTime (Kind=Unspecified), raw ticks = VN wall clock.
        var startVnLocal = playDate.ToDateTime(preferredStart);
        var endVnLocal = endDate.ToDateTime(preferredEnd);

        // Bước 2: convert sang Kind=Utc để tất cả downstream (DB, JSON, comparison) thấy
        // cùng 1 kind = Utc. EF Core với column cột `timestamp with time zone` sẽ store raw
        // UTC ticks; System.Text.Json sẽ serialize thành "...Z".
        return (
            TimeZoneInfo.ConvertTimeToUtc(startVnLocal, VietnamTz),
            TimeZoneInfo.ConvertTimeToUtc(endVnLocal, VietnamTz)
        );
    }
}
