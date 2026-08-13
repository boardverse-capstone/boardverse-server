using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="ReservationTimeOverrunHelper"/> — Phase 4 / EC-10
/// (time-slot-fixed-end-design.md §7.1).
/// </summary>
public class ReservationTimeOverrunHelperTests
{
    [Fact]
    public void Compute_NullScheduledEnd_ReturnsNoWarning()
    {
        // Walk-in / direct booking không thuộc Reservation flow → không có warning.
        var (warning, remaining) = ReservationTimeOverrunHelper.Compute(
            scheduledEndTimeUtc: null,
            estimatedRemainingMinutes: 30,
            nowUtc: DateTime.UtcNow);

        Assert.False(warning);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void Compute_GameLongerThanTimeRemaining_ReturnsWarning()
    {
        // TimeSlot còn 15 phút, game cần 30 phút → warning = true.
        var now = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        var scheduledEnd = now.AddMinutes(15);

        var (warning, remaining) = ReservationTimeOverrunHelper.Compute(
            scheduledEnd,
            estimatedRemainingMinutes: 30,
            now);

        Assert.True(warning);
        Assert.Equal(15, remaining);
    }

    [Fact]
    public void Compute_GameShorterThanTimeRemaining_NoWarning()
    {
        // TimeSlot còn 60 phút, game cần 30 phút → warning = false.
        var now = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        var scheduledEnd = now.AddMinutes(60);

        var (warning, remaining) = ReservationTimeOverrunHelper.Compute(
            scheduledEnd,
            estimatedRemainingMinutes: 30,
            now);

        Assert.False(warning);
        Assert.Equal(60, remaining);
    }

    [Fact]
    public void Compute_TimeSlotAlreadyEnded_NoWarning()
    {
        // TimeSlot đã hết (grace 30 phút) → không warning để tránh false positive.
        var now = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        var scheduledEnd = now.AddMinutes(-5);

        var (warning, remaining) = ReservationTimeOverrunHelper.Compute(
            scheduledEnd,
            estimatedRemainingMinutes: 30,
            now);

        Assert.False(warning);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void Compute_GameNeedsExactlyTimeRemaining_NoWarning()
    {
        // Edge case: estimatedRemaining = timeSlotRemaining → không overrun.
        var now = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        var scheduledEnd = now.AddMinutes(30);

        var (warning, _) = ReservationTimeOverrunHelper.Compute(
            scheduledEnd,
            estimatedRemainingMinutes: 30,
            now);

        Assert.False(warning);
    }

    [Fact]
    public void Compute_NowDefaultsToUtcNow()
    {
        // Scheduled end 10 phút trước → timeSlotRemaining = 0 → warning = false.
        var scheduledEnd = DateTime.UtcNow.AddMinutes(-10);

        var (warning, remaining) = ReservationTimeOverrunHelper.Compute(
            scheduledEnd,
            estimatedRemainingMinutes: 5);

        Assert.False(warning);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void Compute_RoundsUpFractionalMinutes()
    {
        // 1.4 phút → ceil = 2 phút.
        var now = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        var scheduledEnd = now.AddSeconds(84); // 1.4 minutes

        var (_, remaining) = ReservationTimeOverrunHelper.Compute(
            scheduledEnd,
            estimatedRemainingMinutes: 0,
            now);

        Assert.Equal(2, remaining);
    }
}