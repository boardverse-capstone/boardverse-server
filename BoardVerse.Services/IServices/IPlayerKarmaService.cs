using BoardVerse.Core.Entities;

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
    Task<bool> RecordNoShowAsync(Guid reservationId, Guid hostId, CancellationToken ct = default);

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
    Task<KarmaShortPlayRecord?> GetLatestByUserAsync(Guid userId, CancellationToken ct = default);
}