using BoardVerse.Core.DTOs.Session;

namespace BoardVerse.Services.IServices;

/// <summary>
/// M2/C2.16 — Force-close service (Gap #33).
/// Manager force-close session khi còn unpaid members,
/// chọn cách xử lý: MarkNoShow | MarkAsDebt | CompensationByHost.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.16.
/// (2026-10-01)
/// </summary>
public interface IForceCloseService
{
    /// <summary>
    /// Force-close session với unpaid members handling.
    /// <para>
    /// Flow:
    /// </para>
    /// <list type="number">
    ///   <item>Validate session status (phải Unpaid).</item>
    ///   <item>Tìm unpaid members (PaymentStatus = NotPaid).</item>
    ///   <item>Validate không rỗng.</item>
    ///   <item>Validate handling-specific: CompensationByHost → host phải đã paid.</item>
    ///   <item>Apply handling cho từng unpaid member:
    ///     <list type="bullet">
    ///       <item>MarkNoShow: status = NoShow, set NoShowAt + NoShowReason.</item>
    ///       <item>MarkAsDebt: status = Finished, insert MemberDebt row.</item>
    ///       <item>CompensationByHost: status = PaidByHost, set PaidByHostAt + PaidByHostUserId.</item>
    ///     </list>
    ///   </item>
    ///   <item>Nếu còn unpaid sau handle + AllowLatePayment=false → 409.</item>
    ///   <item>Nếu hết unpaid → atomic flip session.Status = Paid.</item>
    ///   <item>Nếu còn unpaid → session.Status = UnpaidForced.</item>
    ///   <item>Insert ForceCloseAuditLog row.</item>
    /// </list>
    /// </summary>
    /// <param name="sessionId">ActiveSession.Id.</param>
    /// <param name="request">ForceCloseRequestDto.</param>
    /// <param name="actorUserId">UserId của staff/manager trigger force-close.</param>
    /// <param name="cancellationToken">Token hủy.</param>
    /// <returns>ForceCloseResponseDto với chi tiết các unpaid members đã xử lý.</returns>
    /// <exception cref="NotFoundException">Session không tồn tại.</exception>
    /// <exception cref="ConflictException">Session không ở Unpaid, không có unpaid members, host chưa paid, hoặc vẫn còn unpaid khi AllowLatePayment=false.</exception>
    Task<ForceCloseResponseDto> ForceCloseWithUnpaidAsync(
        Guid sessionId,
        ForceCloseRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}