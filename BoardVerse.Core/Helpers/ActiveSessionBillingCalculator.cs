using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers;

/// <summary>
/// Phase 5 / EC-11 — Manager override played time (BR-REFUND-07 §time-slot-fixed-end v3.0).
/// Pure helper tính subtotal cho phiên chơi dựa trên cafe config + minutes chơi.
///
/// Trước đây logic này nằm private trong <c>ActiveSessionService.CalculateRealtimeBilling</c>
/// — không thể gọi từ <c>CafePosService.OverridePlayedTimeAsync</c> (singleton khác service).
/// Tách ra đây để (1) unit test pure logic, (2) share giữa ActiveSessionService + CafePosService.
/// </summary>
/// <remarks>
/// Quy tắc (BR-15 + BR-16 + anti-abuse mở bàn rồi nghỉ):
/// <list type="bullet">
///   <item><description>Anti-abuse: elapsedMinutes = 0 vẫn trả BasePrice (không trả 0) — chống player mở bàn rồi nghỉ.</description></item>
///   <item><description>FlatEntry: subtotal = BasePrice (giá vé vào cổng duy nhất).</description></item>
///   <item><description>TimeBased + elapsed ≤ 60: subtotal = BasePrice (giờ đầu).</description></item>
///   <item><description>TimeBased + elapsed &gt; 60: subtotal = BasePrice + ⌈(elapsed - 60) / TieredBlockMinutes⌉ × TieredBlockRate.</description></item>
/// </list>
/// </remarks>
public static class ActiveSessionBillingCalculator
{
    /// <summary>
    /// Tính subtotal cho phiên chơi.
    /// </summary>
    /// <param name="cafe">Cafe config (BasePrice, BillingModel, TieredBlockMinutes/Rate).</param>
    /// <param name="elapsedMinutes">Số phút chơi. Phải ≥ 0.</param>
    /// <returns>Subtotal VND. Tối thiểu = BasePrice (kể cả khi elapsedMinutes = 0, để chống player mở bàn rồi nghỉ).</returns>
    public static decimal CalculateRealtimeBilling(Cafe cafe, int elapsedMinutes)
    {
        if (elapsedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedMinutes), elapsedMinutes, "Số phút chơi phải >= 0.");
        }

        // FlatEntry: giá vé vào cổng duy nhất, không cộng block.
        if (cafe.BillingModel == CafePartnerBillingModel.FlatEntry)
        {
            return cafe.BasePrice;
        }

        // TimeBased ≤ 60 phút: giờ đầu (gồm cả elapsed = 0 — chống mở bàn rồi nghỉ).
        if (elapsedMinutes <= 60)
        {
            return cafe.BasePrice;
        }

        // TimeBased > 60 phút: giờ đầu + block lũy tiến.
        // Defensive: nếu TieredBlockRate null/invalid → fallback về BasePrice (giờ đầu).
        if (!cafe.TieredBlockRate.HasValue || cafe.TieredBlockRate <= 0)
        {
            return cafe.BasePrice;
        }

        var remainingMinutes = elapsedMinutes - 60;
        var blockMinutes = cafe.TieredBlockMinutes > 0 ? cafe.TieredBlockMinutes : 30;
        var blockPrice = cafe.TieredBlockRate.Value;

        var additionalBlocks = (int)Math.Ceiling((double)remainingMinutes / blockMinutes);
        return cafe.BasePrice + (additionalBlocks * blockPrice);
    }
}
