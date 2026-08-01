using BoardVerse.Core.DTOs.Booking;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service cho các API voting/rating liên quan đến booking (mobile gaps #4, #5).
/// </summary>
public interface IBookingRatingService
{
    /// <summary>
    /// Gửi/cập nhật phiếu vote vắng mặt cho booking. Mobile gap #4.
    /// - Voter phải là member của lobby (Active).
    /// - Booking phải ở CheckedIn.
    /// - Không vote chính mình.
    /// - Idempotent: voter vote lần 2 sẽ UPDATE (không insert).
    /// </summary>
    Task<NoShowVoteResponseDto> SubmitNoShowVoteAsync(
        Guid bookingId, Guid voterUserId, SubmitNoShowVoteRequestDto request);

    /// <summary>
    /// Gửi lượt chấm điểm chéo cho booking. Mobile gap #5.
    /// - Voter phải là member lobby đã check-in.
    /// - Booking phải CheckedIn (có thể đã check-out vẫn rate trong 24h).
    /// - Không rate chính mình.
    /// - Idempotent: voter submit lần 2 = update.
    /// </summary>
    Task<BookingRatingResponseDto> SubmitRatingsAsync(
        Guid bookingId, Guid voterUserId, SubmitBookingRatingsRequestDto request);

    /// <summary>
    /// Lấy trạng thái rating của voter trong booking (đã rate ai, còn ai chưa, deadline).
    /// </summary>
    Task<BookingRatingStatusDto> GetRatingStatusAsync(
        Guid bookingId, Guid voterUserId);

    /// <summary>
    /// Tổng hợp Karma + audit log cho booking sau khi session kết thúc (staff check-out).
    /// Mobile gap #5 + Exception 2 (no-show):
    /// <list type="number">
    /// <item>Đọc các <see cref="BookingRating"/> chưa aggregate (IsAggregated = false).</item>
    /// <item>Đọc các <see cref="BookingNoShowVote"/> của booking.</item>
    /// <item>Cross-rating: cho mỗi user được rate, tính avgScore trên thang 1-5
    /// (attitude/sportsmanship/punctuality) → delta = (avg - 3.0) * 10. Ghi <see cref="KarmaLog"/>
    /// source = PlayerCrossRating, category = CrossRating, cộng/delta vào UserProfile.KarmaPoints.</item>
    /// <item>No-show: member nào nhận absentVotes > totalMembers/2 → trừ KarmaPoints (mặc định -10),
    /// ghi <see cref="KarmaLog"/> source = SystemAutomatic, category = NoShow.</item>
    /// <item>Forfeit deposit: nếu <see cref="BookingDeposit"/> của no-show member có
    /// <see cref="DepositRefundPolicy.None"/> → mark deposit forfeited,
    /// ghi <see cref="KarmaLog"/> source = SystemAutomatic, category = NoShow.</item>
    /// <item>Idempotent: chỉ aggregate booking <c>IsAggregated</c> rows → set <c>IsAggregated = true</c>
    /// để lần check-out sau (nếu staff bấm lại) không tính lại.</item>
    /// </list>
    /// Trả về summary gồm số user được cộng/trừ Karma, lý do, totalDelta.
    /// </summary>
    Task<BookingRatingAggregationResultDto> AggregateBookingOutcomesAsync(
        Guid bookingId);
}