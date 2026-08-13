using BoardVerse.Core.Enum;

namespace BoardVerse.Services.Services;

/// <summary>
/// Tính refund theo playedRatio + BR-REFUND-01..07 (docs/time-slot-fixed-end-design (1).md §3.4).
/// </summary>
public class RefundCalculationService
{
    /// <summary>
    /// Tính refund BVC theo playedRatio.
    /// BR-END-04: playedRatio ≥ 90% → OnTime, refund 0%.
    /// BR-REFUND-04: playedRatio ≥ 50% → forfeit 70% (refund 30%).
    /// BR-REFUND-05: playedRatio &lt; 50% → forfeit 100% (refund 0%).
    /// </summary>
    /// <param name="originalDeposit">BVC đã giữ (DepositAmount).</param>
    /// <param name="playedRatio">Decimal 0-1 (BR-END-02).</param>
    /// <returns>(refundAmount, forfeitAmount, reason).</returns>
    public (long RefundAmount, long ForfeitAmount, RefundReason Reason) Calculate(
        long originalDeposit,
        decimal? playedRatio)
    {
        if (originalDeposit <= 0)
        {
            return (0, 0, RefundReason.Other);
        }

        // Default: playedRatio null khi session quá ngắn → coi như không chơi.
        var ratio = playedRatio ?? 0m;

        if (ratio >= 0.90m)
        {
            // OnTime: played ≥ 90% → refund 0%, forfeit 100%.
            return (0, originalDeposit, RefundReason.OnTime);
        }

        if (ratio >= 0.50m)
        {
            // Early leave nhưng ≥ 50% → refund 30%, forfeit 70%.
            var refund = (long)Math.Round(originalDeposit * 0.30m, MidpointRounding.AwayFromZero);
            var forfeit = originalDeposit - refund;
            return (refund, forfeit, RefundReason.EarlyCheckout);
        }

        // Early leave < 50% → forfeit 100%.
        return (0, originalDeposit, RefundReason.EarlyCheckout);
    }

    /// <summary>
    /// BR-REFUND-02 + BR-REFUND-03: refund khi host cancel trước check-in.
    /// </summary>
    /// <param name="originalDeposit">BVC đã giữ.</param>
    /// <param name="hoursBeforeStart">Số giờ trước <c>ScheduledStartTime</c> tại thời điểm cancel.</param>
    /// <param name="isInGracePeriod">True nếu còn trong grace 15 phút + chưa có member.</param>
    /// <returns>(refundAmount, forfeitAmount, reason).</returns>
    public (long RefundAmount, long ForfeitAmount, RefundReason Reason) CalculateHostCancel(
        long originalDeposit,
        double hoursBeforeStart,
        bool isInGracePeriod)
    {
        if (isInGracePeriod)
        {
            return (originalDeposit, 0, RefundReason.CancelGracePeriod);
        }

        if (hoursBeforeStart >= 24.0)
        {
            return (originalDeposit, 0, RefundReason.CancelBefore24h);
        }

        // < 24 hours → forfeit 100% (BR-REFUND-02 đã đơn giản hóa).
        return (0, originalDeposit, RefundReason.CancelAfter24h);
    }

    /// <summary>
    /// BR-CHECKIN-02 + BR-REFUND-03: no-show refund = 0%.
    /// </summary>
    public (long RefundAmount, long ForfeitAmount, RefundReason Reason) CalculateNoShow(long originalDeposit)
        => (0, originalDeposit, RefundReason.NoShow);

    /// <summary>
    /// BR-REFUND-04: cafe hủy → refund 100%.
    /// </summary>
    public (long RefundAmount, long ForfeitAmount, RefundReason Reason) CalculateCafeCancel(long originalDeposit)
        => (originalDeposit, 0, RefundReason.StaffOverride);
}