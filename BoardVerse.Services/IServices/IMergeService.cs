using BoardVerse.Core.DTOs.Session;

namespace BoardVerse.Services.IServices;

/// <summary>
/// M1 / Option A — Service xử lý refund PER-MEMBER deposit khi member merge sang lobby khác (Exception 4).
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §B4.
/// <para>
/// <b>SCOPE QUAN TRỌNG — chỉ PER-MEMBER deposit (BR-22):</b>
/// <list type="bullet">
///   <item>Service này chỉ chạm vào <c>ActiveSessionMember.DepositId</c> (per-member deposit của member).</item>
///   <item>KHÔNG đụng vào <c>Reservation.DepositAmount</c> (host deposit) — xem giải thích bên dưới.</item>
/// </list>
/// </para>
/// <para>
/// <b>HOST deposit xử lý ở LobbyMergeService.ApproveMergeAsync</b> (docs §B3.1) theo cơ chế
/// "Host Deposit Follows" (CẬP NHẬT 2026-10-01):
/// <list type="bullet">
///   <item>Khi source lobby dissolve (stillActive == 0), host deposit <b>FOLLOWS</b> merged members
///         sang target reservation (cộng vào <c>Reservation.CarriedOverDepositBvc</c>).</item>
///   <item>KHÔNG refund về host wallet — tránh "ghost deposit" trong heldBalance vô thời hạn.</item>
///   <item>Khi target session PAY, effective deposit = <c>DepositAmount + CarriedOverDepositBvc</c>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Phân biệt với <c>ActiveSessionService.MergeSessionAsync</c></c> (chỉ move member entity):
/// <list type="bullet">
///   <item>HandleMemberMergeAsync xử lý TOÀN BỘ side-effect của merge cho per-member deposit:
///     <list type="bullet">
///       <item>Refund per-member deposit về wallet (Option A — M1 Phase 2b).</item>
///       <item>Update member: DepositId=null, DepositAppliedAmount=0, LeftAt=now, MergedFromLobbyId=from.</item>
///       <item>Insert MemberDepositAuditLog row (Refunded_OnMerge).</item>
///     </list>
///   </item>
///   <item>KHÔNG move ActiveSessionId (đó là ActiveSessionService.MergeSessionAsync).</item>
///   <item>Có thể gọi từ ActiveSessionService.MergeSessionAsync như decorator, hoặc gọi độc lập từ POS khi cần refund riêng.</item>
/// </list>
/// </para>
/// </summary>
public interface IMergeService
{
    /// <summary>
    /// Refund per-member deposit về wallet khi member merge sang lobby khác (Option A).
    /// Idempotent: cùng memberId đã refund → return existing audit log row.
    /// </summary>
    /// <param name="memberId">ActiveSessionMember.Id cần refund deposit.</param>
    /// <param name="fromLobbyId">Lobby gốc (merge source) — audit trail.</param>
    /// <param name="toLobbyId">Lobby đích (merge target) — audit trail. Nullable nếu chưa link target.</param>
    /// <param name="staffUserId">UserId staff/host thực hiện thao tác merge.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Kết quả refund (ledger entry + audit log).</returns>
    /// <exception cref="NotFoundException">Member không tồn tại.</exception>
    /// <exception cref="InvalidOperationException">Member là Guest_Slot hoặc không có UserId.</exception>
    Task<MergeMemberRefundResponseDto> HandleMemberMergeAsync(
        Guid memberId,
        Guid fromLobbyId,
        Guid? toLobbyId,
        Guid staffUserId,
        CancellationToken ct = default);
}
