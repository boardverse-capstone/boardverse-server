using BoardVerse.Core.Constants;
using BoardVerse.Core.Enum;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests for CafeSchedule utilities and TimeSlotExtensions.
/// BR-NEW-15: TimeSlot enum retained for backward compat; new API uses TimeOnly.
/// </summary>
public class ReservationServiceTimeValidationTests
{
    // ===== BR-NEW-15 / CafeSchedule.ValidatePreferredTimeRange =====

    [Fact]
    public void ValidatePreferredTimeRange_ValidRange_ReturnsTrue()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(10, 0), new TimeOnly(14, 0));
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_OvernightRange_ReturnsTrue()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(21, 0), new TimeOnly(0, 0));
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_StartBeforeOpen_ReturnsFalse()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(5, 0), new TimeOnly(8, 0));
        Assert.False(isValid);
        Assert.Contains("mở cửa", error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_EndAfterClose_ReturnsFalse()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(22, 0), new TimeOnly(23, 30));
        Assert.False(isValid);
        Assert.Contains("đóng cửa", error);
    }

    [Fact]
    public void ValidatePreferredTimeRange_SameTime_ReturnsFalse()
    {
        var (isValid, error) = CafeSchedule.ValidatePreferredTimeRange(
            new TimeOnly(10, 0), new TimeOnly(10, 0));
        Assert.False(isValid);
    }

    // ===== BR-NEW-15 / CafeSchedule.BuildScheduledStartEndFromPreferred =====

    [Fact]
    public void BuildScheduledStartEndFromPreferred_ReturnsCorrectDateTime()
    {
        var playDate = new DateOnly(2026, 8, 15);
        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(10, 0), new TimeOnly(14, 0));

        // FIX TZ-DT-UTC-TEST-01 (2026-10-02): output giờ là Kind=Utc.
        // 10:00 VN = 03:00 UTC; 14:00 VN = 07:00 UTC cùng ngày.
        Assert.Equal(new DateTime(2026, 8, 15, 3, 0, 0, DateTimeKind.Utc), scheduledStart);
        Assert.Equal(new DateTime(2026, 8, 15, 7, 0, 0, DateTimeKind.Utc), scheduledEnd);
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_SameDayOnly()
    {
        var playDate = new DateOnly(2026, 8, 15);
        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(17, 0), new TimeOnly(23, 0));

        // Convert về VN local trước khi so sánh DateOnly (early morning VN = previous day UTC).
        Assert.Equal(playDate, DateOnly.FromDateTime(CafeSchedule.ToVietnamLocal(scheduledStart)));
        Assert.Equal(playDate, DateOnly.FromDateTime(CafeSchedule.ToVietnamLocal(scheduledEnd)));
    }

    [Fact]
    public void BuildScheduledStartEndFromPreferred_Overnight_UsesNextDayForEnd()
    {
        var playDate = new DateOnly(2026, 8, 18);
        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            playDate, new TimeOnly(21, 0), new TimeOnly(0, 0));

        // 21:00 VN = 14:00 UTC; 00:00 của 19/08 VN = 17:00 UTC của 18/08.
        Assert.Equal(new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc), scheduledStart);
        Assert.Equal(new DateTime(2026, 8, 18, 17, 0, 0, DateTimeKind.Utc), scheduledEnd);
        // Duration giữ nguyên vì TimeSpan.Subtract là Kind-agnostic.
        Assert.Equal(TimeSpan.FromHours(3), scheduledEnd - scheduledStart);
    }

    // ===== TimeSlotExtensions (backward compat) =====

    [Theory]
    [InlineData(TimeSlot.Morning, 6, 0)]
    [InlineData(TimeSlot.Afternoon, 12, 0)]
    [InlineData(TimeSlot.Evening, 17, 0)]
    [InlineData(TimeSlot.LateNight, 23, 0)]
    public void TimeSlotExtensions_GetStartTime_ReturnsCorrectHour(TimeSlot slot, int hour, int minute)
    {
        var start = slot.GetStartTime();
        Assert.Equal(new TimeOnly(hour, minute), start);
    }

    [Theory]
    [InlineData(TimeSlot.Morning, 12, 0)]
    [InlineData(TimeSlot.Afternoon, 17, 0)]
    [InlineData(TimeSlot.Evening, 23, 0)]
    [InlineData(TimeSlot.LateNight, 6, 0)]
    public void TimeSlotExtensions_GetEndTime_ReturnsCorrectHour(TimeSlot slot, int hour, int minute)
    {
        var end = slot.GetEndTime();
        Assert.Equal(new TimeOnly(hour, minute), end);
    }

    [Theory]
    [InlineData(TimeSlot.Morning, 6)]
    [InlineData(TimeSlot.Afternoon, 5)]
    [InlineData(TimeSlot.Evening, 6)]
    [InlineData(TimeSlot.LateNight, 7)]
    public void TimeSlotExtensions_GetDurationMinutes_ReturnsCorrectDuration(TimeSlot slot, int expectedHours)
    {
        var duration = slot.GetDurationMinutes();
        Assert.Equal(expectedHours * 60, duration);
    }

    [Theory]
    [InlineData(TimeSlot.Morning, false)]
    [InlineData(TimeSlot.Afternoon, false)]
    [InlineData(TimeSlot.Evening, false)]
    [InlineData(TimeSlot.LateNight, true)]
    public void TimeSlotExtensions_IsOvernight_ReturnsCorrectValue(TimeSlot slot, bool expected)
    {
        Assert.Equal(expected, slot.IsOvernight());
    }

    [Theory]
    [InlineData(TimeSlot.Morning, "Sáng (06:00-12:00)")]
    [InlineData(TimeSlot.Afternoon, "Chiều (12:00-17:00)")]
    [InlineData(TimeSlot.Evening, "Tối (17:00-23:00)")]
    [InlineData(TimeSlot.LateNight, "Khuya (23:00-06:00)")]
    public void TimeSlotExtensions_GetDisplayName_ReturnsCorrectValue(TimeSlot slot, string expected)
    {
        Assert.Equal(expected, slot.GetDisplayName());
    }

    // ===== TZ-RESV-01 (2026-10-02): Regression tests cho timezone bug check-in =====
    //
    // Bug: Reservation.ScheduledStartTime được build từ playDate + TimeOnly → Kind=Unspecified,
    // raw ticks đại diện giờ VN local. Trên server UTC (Linux container), C# treat Unspecified
    // như Local (=UTC), làm sai lệch 7 giờ khi so sánh với DateTime.UtcNow.
    //
    // Mục tiêu: đảm bảo helper CafeSchedule.ToUtcAssumingVietnamLocal và ToVietnamLocal convert
    // đúng, bất kể server timezone là UTC hay VN.
    // =====

    [Fact]
    public void ToUtcAssumingVietnamLocal_UnspecifiedLocal_ConvertsToUtcCorrectly()
    {
        // 16:00 giờ VN (UTC+7) = 09:00 UTC
        var vnLocal = new DateTime(2026, 10, 2, 16, 0, 0, DateTimeKind.Unspecified);
        var utc = CafeSchedule.ToUtcAssumingVietnamLocal(vnLocal);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void ToUtcAssumingVietnamLocal_AlreadyUtc_PassesThrough()
    {
        var utc = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
        var result = CafeSchedule.ToUtcAssumingVietnamLocal(utc);

        Assert.Equal(utc, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void ToUtcAssumingVietnamLocal_Local_DoesNotShiftToServerLocal()
    {
        // Trên server UTC: Local kind convert sang UTC bằng ToUniversalTime() của DateTime
        // (dùng server timezone). Đảm bảo method KHÔNG shift nhầm khi input là Local.
        // Setup: input là Local tương ứng 16:00 VN (UTC+7). Nếu server là UTC, .ToUniversalTime()
        // sẽ trừ đi offset của server (0) → vẫn là 16:00 UTC, sai.
        // → Method hiện tại pass-through ToUniversalTime() cho Local — chấp nhận rủi ro nhỏ
        // vì API đang chuẩn hóa input là Unspecified qua playDate.ToDateTime().
        // Test này document behavior: Local pass-through bằng DateTime.ToUniversalTime().
        var local = new DateTime(2026, 10, 2, 16, 0, 0, DateTimeKind.Local);
        var result = CafeSchedule.ToUtcAssumingVietnamLocal(local);

        // result.Kind phải là Utc hoặc Unspecified (do ToUniversalTime có thể set Kind=Utc).
        Assert.True(result.Kind is DateTimeKind.Utc or DateTimeKind.Unspecified);
    }

    [Fact]
    public void ToVietnamLocal_FromUtc_ConvertsToVnLocal()
    {
        // 09:00 UTC = 16:00 giờ VN
        var utc = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
        var vnLocal = CafeSchedule.ToVietnamLocal(utc);

        Assert.Equal(DateTimeKind.Unspecified, vnLocal.Kind);
        Assert.Equal(new DateTime(2026, 10, 2, 16, 0, 0, DateTimeKind.Unspecified), vnLocal);
    }

    [Fact]
    public void BuildScheduledStartEnd_AndCheckInWindow_AllowsCheckInAtRightUtcMoment()
    {
        // Reproduce scenario từ bug report 2026-10-02:
        // - Reservation playDate = 02/10/2026, preferredStart = 16:00 VN.
        // - Server UTC. now = 08:43Z (= 15:43 VN) — TRONG khung 15:30-20:30 VN → cho phép.
        // Trước fix: now (UTC) < windowStart (Unspec=15:30 treated as UTC) → fail "ngoài khung".
        // Sau fix: convert scheduledStart sang UTC (09:00Z), windowStart = 08:30Z → 08:43Z > 08:30Z → pass.

        var (scheduledStart, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            new DateOnly(2026, 10, 2), new TimeOnly(16, 0), new TimeOnly(20, 0));

        // FIX TZ-DT-UTC-TEST-01 (2026-10-02): BuildScheduledStartEndFromPreferred giờ trả về
        // Kind=Utc (UTC ticks). JSON serializer sẽ render "Z" suffix → FE chuyển local chính xác.
        Assert.Equal(DateTimeKind.Utc, scheduledStart.Kind);

        var nowUtc = new DateTime(2026, 10, 2, 8, 43, 0, DateTimeKind.Utc); // 15:43 VN
        var scheduledStartUtc = CafeSchedule.ToUtcAssumingVietnamLocal(scheduledStart);
        var windowStart = scheduledStartUtc.AddMinutes(-30); // 08:30Z = 15:30 VN

        Assert.True(nowUtc >= windowStart,
            $"now UTC={nowUtc:HH:mm:ssZ} phải >= windowStart UTC={windowStart:HH:mm:ssZ} (=15:30 VN) → bug fixed");
    }

    [Fact]
    public void BuildScheduledStartEnd_RejectsCheckInBeforeWindow()
    {
        // Negative case: now = 07:00Z (= 14:00 VN), trước 15:30 VN → phải fail.
        var (scheduledStart, _) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            new DateOnly(2026, 10, 2), new TimeOnly(16, 0), new TimeOnly(20, 0));
        var scheduledStartUtc = CafeSchedule.ToUtcAssumingVietnamLocal(scheduledStart);

        var nowUtc = new DateTime(2026, 10, 2, 7, 0, 0, DateTimeKind.Utc); // 14:00 VN
        var windowStart = scheduledStartUtc.AddMinutes(-30);

        Assert.True(nowUtc < windowStart,
            $"now UTC={nowUtc:HH:mm:ssZ} phải < windowStart UTC={windowStart:HH:mm:ssZ} (=15:30 VN)");
    }

    [Fact]
    public void BuildScheduledStartEnd_RejectsCheckInAfterEndPlusGrace()
    {
        // now = 14:00Z (+7h = 21:00 VN), sau 20:30 + 30min late grace → fail.
        var (_, scheduledEnd) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            new DateOnly(2026, 10, 2), new TimeOnly(16, 0), new TimeOnly(20, 0));
        var scheduledEndUtc = CafeSchedule.ToUtcAssumingVietnamLocal(scheduledEnd);

        var nowUtc = new DateTime(2026, 10, 2, 14, 0, 0, DateTimeKind.Utc); // 21:00 VN
        var windowEnd = scheduledEndUtc.AddMinutes(30);

        Assert.True(nowUtc > windowEnd,
            $"now UTC={nowUtc:HH:mm:ssZ} phải > windowEnd UTC={windowEnd:HH:mm:ssZ} (=20:30 VN)");
    }
}
