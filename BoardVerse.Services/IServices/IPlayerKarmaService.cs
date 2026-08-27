using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Services.IServices;

/// <summary>
/// BR-KARMA-01 (§4.3) + §9.5: Quản lý karma violation cho player (short play, no-show).
/// Hook từ <c>ReservationService.EndAndSettleAsync</c> và <c>ReservationService.ProcessNoShowAsync</c>.
/// </summary>
public interface IPlayerKarmaService
{
    /// <summary>
    /// BR-KARMA-01 §4.3: Ghi nhận vi phạm short play khi playedRatio &lt; 0.5.
    /// Idempotent theo (reservationId, userId) — không tạo 2 record trùng.
    /// </summary>
    /// <param name="reservationId">Reservation bị vi phạm.</param>
    /// <param name="userId">User bị ghi nhận.</param>
    /// <param name="playedMinutes">Phút đã chơi.</param>
    /// <param name="scheduledMinutes">Phút dự kiến.</param>
    /// <returns>True nếu record được tạo; false nếu đã có (idempotent).</returns>
    Task<bool> RecordShortPlayAsync(
        Guid reservationId,
        Guid userId,
        int playedMinutes,
        int scheduledMinutes,
        CancellationToken ct = default);

    /// <summary>
    /// BR §21A.9: Ghi nhận no-show (host không check-in sau grace 30 phút).
    /// </summary>
    Task<bool> RecordNoShowAsync(Guid reservationId, Guid hostId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-KARMA-01 §4.3: Ghi nhận early-checkout (playedRatio 50-90%).
    /// </summary>
    Task<bool> RecordEarlyCheckoutAsync(
        Guid reservationId,
        Guid userId,
        int playedMinutes,
        int scheduledMinutes,
        CancellationToken ct = default);

    /// <summary>
    /// ID của record gần nhất (dùng cho query debug).
    /// </summary>
    Task<KarmaShortPlayRecord?> GetLatestByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-REFUND-02 + BR-RISK-05 §16.7: Ghi nhận host dissolve lobby gần giờ chơi.
    /// Phạt Karma cho host khi dissolve lobby trong vòng 24 giờ trước scheduledStart.
    /// </summary>
    /// <param name="reservationId">Reservation liên kết (nullable cho legacy lobby không có reservation).</param>
    /// <param name="hostId">Host bị ghi nhận.</param>
    /// <param name="hoursBeforeScheduledStart">Số giờ trước scheduledStart (âm nếu đã qua).</param>
    /// <param name="policyName">Tên policy dissolve đã áp dụng (dùng cho audit).</param>
    /// <returns>True nếu record được tạo; false nếu đã có (idempotent).</returns>
    Task<bool> RecordHostDissolveAsync(
        Guid? reservationId,
        Guid hostId,
        double hoursBeforeScheduledStart,
        string policyName,
        CancellationToken cancellationToken = default);
}