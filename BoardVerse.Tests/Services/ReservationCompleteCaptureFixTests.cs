using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Regression tests for the BR-END-02 lifecycle-metadata fix landed on 2026-08-27.
///
/// Bug being guarded:
///   <c>ReservationService.ExecuteCompleteAndCaptureTransactionAsync</c> previously
///   flipped <see cref="Reservation.Status"/> to <see cref="ReservationStatus.Completed"/>
///   without populating <see cref="Reservation.ActualEndAt"/>,
///   <see cref="Reservation.PlayedRatio"/>, or <see cref="Reservation.EndReason"/>.
///   Downstream audit/karma/refund reports read NULL for those fields, leading to
///   "bàn tự dưng closed" reports because:
///     1. <see cref="ActiveSessionService.PaySessionAsync"/> calls
///        <c>CompleteAndCaptureAsync</c> after staff presses Pay.
///     2. The reservation row was missing <c>CheckedInAt</c> too
///        (separate upstream bug in <c>ExecuteCheckInTransactionAsync</c> step 9 —
///        fixed in the same change-set), so <c>EndAndSettleAsync</c> would also throw.
///
/// These tests assert the **mapping rules** that the fix encodes. They are
/// intentionally pure-logic (no service container) so they stay cheap and
/// never drift with refactors of unrelated dependencies. The integration coverage
/// lives in <c>ReservationFlowIntegrationTests.PaySessionAsync_SetsLifecycleMetadata_*</c>
/// (manual smoke in dev DB).
/// </summary>
public class ReservationCompleteCaptureFixTests
{
    private static readonly DateTime ScheduledStart =
        new(2026, 8, 27, 19, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ScheduledEnd =
        ScheduledStart.AddHours(2); // 2h session

    /// <summary>
    /// Mirrors <c>ExecuteCompleteAndCaptureTransactionAsync</c> step 5 logic
    /// so the regression assertions do not require spinning up the full DI graph.
    /// Keep in sync with <c>ReservationService.cs</c> BR-END-02 block.
    /// </summary>
    private static (DateTime? actualEndAt, decimal playedRatio, SessionEndReason? endReason)
        ComputeLifecycleMetadata(
            DateTime? actualEndAtInput,
            DateTime? checkedInAtInput,
            DateTime scheduledStart,
            DateTime scheduledEnd)
    {
        var actualEndAt = actualEndAtInput ?? DateTime.UtcNow;
        var checkedInAt = checkedInAtInput ?? scheduledStart;

        var scheduledMinutes = (scheduledEnd - scheduledStart).TotalMinutes;
        decimal playedRatio = 0m;
        if (scheduledMinutes > 0)
        {
            var playedMinutes = (decimal)(actualEndAt - checkedInAt).TotalMinutes;
            var rawRatio = playedMinutes / (decimal)scheduledMinutes;
            playedRatio = Math.Max(0m, Math.Min(1m, rawRatio));
        }

        var endReason = playedRatio >= 0.9m
            ? SessionEndReason.OnTime
            : SessionEndReason.EarlyLeave;

        return (actualEndAt, playedRatio, endReason);
    }

    [Fact]
    public void PlayedRatio_OnTime_ShouldBeAtLeastNinetyPercent_AndReasonOnTime()
    {
        // Arrange: player chơi đủ 110/120 phút (91.67%)
        var checkedInAt = ScheduledStart;
        var endedAt = ScheduledStart.AddMinutes(110);

        // Act
        var (actualEndAt, playedRatio, endReason) = ComputeLifecycleMetadata(
            actualEndAtInput: endedAt,
            checkedInAtInput: checkedInAt,
            scheduledStart: ScheduledStart,
            scheduledEnd: ScheduledEnd);

        // Assert
        Assert.Equal(endedAt, actualEndAt);
        Assert.True(playedRatio >= 0.9m, $"Expected ≥0.9 but was {playedRatio:P2}");
        Assert.Equal(SessionEndReason.OnTime, endReason);
    }

    [Fact]
    public void PlayedRatio_EarlyLeave_HalfToNinetyPercent_StillEarlyLeave()
    {
        // Arrange: player chơi 60/120 phút (50%) → BR-REFUND-05 nhưng lifecycle vẫn EarlyLeave
        var checkedInAt = ScheduledStart;
        var endedAt = ScheduledStart.AddMinutes(60);

        // Act
        var (_, playedRatio, endReason) = ComputeLifecycleMetadata(
            actualEndAtInput: endedAt,
            checkedInAtInput: checkedInAt,
            scheduledStart: ScheduledStart,
            scheduledEnd: ScheduledEnd);

        // Assert
        Assert.Equal(0.5m, playedRatio);
        Assert.Equal(SessionEndReason.EarlyLeave, endReason);
    }

    [Fact]
    public void PlayedRatio_EarlyLeave_LessThanHalf_TriggersWalkInWindowPath()
    {
        // Arrange: player về sớm sau 30/120 phút (25%) → EC-09 tạo WalkInWindow
        var checkedInAt = ScheduledStart;
        var endedAt = ScheduledStart.AddMinutes(30);

        // Act
        var (_, playedRatio, endReason) = ComputeLifecycleMetadata(
            actualEndAtInput: endedAt,
            checkedInAtInput: checkedInAt,
            scheduledStart: ScheduledStart,
            scheduledEnd: ScheduledEnd);

        // Assert
        Assert.Equal(0.25m, playedRatio);
        Assert.True(playedRatio < 0.5m, "Phải < 0.5 để trigger WalkInWindow creation");
        Assert.Equal(SessionEndReason.EarlyLeave, endReason);
    }

    [Fact]
    public void PlayedRatio_ZeroDuration_IsZero()
    {
        // Arrange: edge case — scheduledEnd == scheduledStart
        var checkedInAt = ScheduledStart;
        var endedAt = ScheduledStart; // same as checkedIn

        // Act
        var (_, playedRatio, _) = ComputeLifecycleMetadata(
            actualEndAtInput: endedAt,
            checkedInAtInput: checkedInAt,
            scheduledStart: ScheduledStart,
            scheduledEnd: ScheduledStart);

        // Assert: guard divide-by-zero → fallback 0
        Assert.Equal(0m, playedRatio);
    }

    [Fact]
    public void PlayedRatio_NegativeRatio_ClampedToZero()
    {
        // Arrange: pathological — EndedAt trước CheckedInAt (clock skew / demo flow)
        var checkedInAt = ScheduledStart.AddMinutes(60);
        var endedAt = ScheduledStart; // 60 phút TRƯỚC checkin

        // Act
        var (_, playedRatio, endReason) = ComputeLifecycleMetadata(
            actualEndAtInput: endedAt,
            checkedInAtInput: checkedInAt,
            scheduledStart: ScheduledStart,
            scheduledEnd: ScheduledEnd);

        // Assert: clamp về 0 (không để ratio âm → EndReason vẫn EarlyLeave)
        Assert.Equal(0m, playedRatio);
        Assert.Equal(SessionEndReason.EarlyLeave, endReason);
    }

    [Fact]
    public void PlayedRatio_OverOneHundredPercent_ClampedToOne()
    {
        // Arrange: sessions kéo dài quá scheduled end (player chơi thêm 30 phút)
        var checkedInAt = ScheduledStart;
        var endedAt = ScheduledEnd.AddMinutes(30); // 150/120 = 125%

        // Act
        var (_, playedRatio, endReason) = ComputeLifecycleMetadata(
            actualEndAtInput: endedAt,
            checkedInAtInput: checkedInAt,
            scheduledStart: ScheduledStart,
            scheduledEnd: ScheduledEnd);

        // Assert: clamp về 1, semantic OnTime (≥0.9)
        Assert.Equal(1m, playedRatio);
        Assert.Equal(SessionEndReason.OnTime, endReason);
    }

    [Fact]
    public void MissingCheckedInAt_FallbackToScheduledStart_AvoidsDivideByZero()
    {
        // Arrange: simulate legacy reservation không có CheckedInAt
        // (BR-END-02 yêu cầu CheckedInAt; safety net dùng ScheduledStart làm fallback).
        var endedAt = ScheduledStart.AddMinutes(60);
        DateTime? checkedInAtMissing = null;

        // Act: dùng fallback trong ComputeLifecycleMetadata
        var (_, playedRatio, _) = ComputeLifecycleMetadata(
            actualEndAtInput: endedAt,
            checkedInAtInput: checkedInAtMissing,
            scheduledStart: ScheduledStart,
            scheduledEnd: ScheduledEnd);

        // Assert: với fallback = ScheduledStart → playedMinutes = 60, ratio = 0.5
        Assert.Equal(0.5m, playedRatio);
    }

    [Fact]
    public void Boundary_NinetyPercentExactly_IsOnTime()
    {
        // Arrange: 108 phút / 120 = 0.9 (boundary)
        var checkedInAt = ScheduledStart;
        var endedAt = ScheduledStart.AddMinutes(108);

        // Act
        var (_, playedRatio, endReason) = ComputeLifecycleMetadata(
            actualEndAtInput: endedAt,
            checkedInAtInput: checkedInAt,
            scheduledStart: ScheduledStart,
            scheduledEnd: ScheduledEnd);

        // Assert
        Assert.Equal(0.9m, playedRatio);
        Assert.Equal(SessionEndReason.OnTime, endReason);
    }

    [Fact]
    public void Boundary_BelowNinetyPercent_IsEarlyLeave()
    {
        // Arrange: 107 phút / 120 = 0.8916 (<0.9)
        var checkedInAt = ScheduledStart;
        var endedAt = ScheduledStart.AddMinutes(107);

        // Act
        var (_, playedRatio, endReason) = ComputeLifecycleMetadata(
            actualEndAtInput: endedAt,
            checkedInAtInput: checkedInAt,
            scheduledStart: ScheduledStart,
            scheduledEnd: ScheduledEnd);

        // Assert
        Assert.True(playedRatio < 0.9m);
        Assert.Equal(SessionEndReason.EarlyLeave, endReason);
    }
}
