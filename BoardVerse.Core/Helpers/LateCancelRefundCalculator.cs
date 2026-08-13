namespace BoardVerse.Core.Helpers;

/// <summary>
/// BR-REFUND-08 (walk-in-override-design §2.3):
/// Pure helper tính refund/forfeit cho late cancel after check-in.
///
/// Công thức:
/// <list type="bullet">
///   <item><description><c>playedMinutes = max(0, (now - session.StartedAt).TotalMinutes)</c></description></item>
///   <item><description><c>scheduledDurationMinutes = max(1, (ScheduledEndTime - ScheduledStartTime).TotalMinutes)</c></description></item>
///   <item><description><c>playedRatio = playedMinutes / scheduledDurationMinutes</c> (clamp 0..1)</description></item>
///   <item><description><c>playedRatio &gt;= 0.5</c> → refund 30%, forfeit 70%.</description></item>
///   <item><description><c>playedRatio &lt; 0.5</c> → refund 0, forfeit 100%.</description></item>
/// </list>
///
/// Tách riêng để (1) unit test pure logic, (2) service chỉ lo transaction.
/// </summary>
public static class LateCancelRefundCalculator
{
    /// <summary>
    /// Tính refund/forfeit breakdown cho late cancel.
    /// </summary>
    /// <param name="depositAmount">Tổng deposit ban đầu (BVC, long).</param>
    /// <param name="playedMinutes">Số phút player đã chơi (≥ 0).</param>
    /// <param name="scheduledDurationMinutes">Tổng số phút của slot (≥ 1).</param>
    /// <returns>(PlayedRatio, RefundBvc, ForfeitBvc, PolicyName).</returns>
    public static (decimal PlayedRatio, long RefundBvc, long ForfeitBvc, string PolicyName) Compute(
        long depositAmount,
        int playedMinutes,
        int scheduledDurationMinutes)
    {
        // Edge case: scheduled duration ≤ 0 → coi như played = 0.
        var safeScheduledMinutes = Math.Max(1, scheduledDurationMinutes);
        var safePlayedMinutes = Math.Max(0, playedMinutes);

        var rawRatio = (decimal)safePlayedMinutes / safeScheduledMinutes;
        var playedRatio = Math.Clamp(rawRatio, 0m, 1m);

        // BR-REFUND-08: playedRatio ≥ 0.5 → refund 30%, forfeit 70%.
        // Dùng epsilon 0.01 (playerRatio đã round 2 chữ số) — tránh false-positive khi raw ratio = 0.4999
        // → round 0.50 sau khi Math.Round(value, 2). Threshold check dùng rawRatio (chưa round) để chính xác.
        if (rawRatio >= 0.5m)
        {
            var refund = (long)Math.Round(depositAmount * 0.30m, MidpointRounding.AwayFromZero);
            var forfeit = depositAmount - refund;
            return (
                Math.Round(playedRatio, 2),
                refund,
                forfeit,
                "BR-REFUND-08 ≥ 0.5");
        }

        return (
            Math.Round(playedRatio, 2),
            0L,
            depositAmount,
            "BR-REFUND-08 < 0.5");
    }
}
