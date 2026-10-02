using BoardVerse.Core.DTOs.Session;

namespace BoardVerse.Services.IServices;

/// <summary>
/// M2 / Case 2 — Member BVC bill payment orchestration service.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.
/// <para>
/// Phối hợp:
/// </para>
/// <list type="bullet">
///   <item><see cref="IWalletService.DirectDebitForBillAsync"/> — trừ BVC từ wallet.</item>
///   <item><see cref="IWalletService.RefundMemberBillAsync"/> — refund bill sai / dispute.</item>
///   <item>Validate session + member state, idempotency, distinct UserId check.</item>
///   <item>Insert <see cref="BoardVerse.Core.Entities.MemberPaymentAuditLog"/> (audit trail).</item>
///   <item>Trigger all-paid session flip (khi member cuối cùng thanh toán xong).</item>
/// </list>
/// </summary>
public interface IWalletSessionPaymentService
{
    /// <summary>
    /// M2 / Task C2.6: Preview hóa đơn cá nhân của 1 member trong session.
    /// Tính totalDue = Subtotal + Penalty (nếu không Guest) - DepositAppliedAmount.
    /// </summary>
    /// <param name="sessionId">ActiveSession.Id.</param>
    /// <param name="memberId">ActiveSessionMember.Id.</param>
    /// <param name="actorUserId">User thực hiện (member.UserId, host, staff, manager, admin).</param>
    /// <param name="actorRole">Role của actor ("Player" | "CafeStaff" | "Admin" | "Manager").</param>
    /// <param name="cancellationToken">Token hủy.</param>
    /// <returns>Preview DTO với breakdown chi tiết.</returns>
    /// <exception cref="NotFoundException">Session hoặc member không tồn tại.</exception>
    /// <exception cref="ForbiddenException">Player không phải member này và không phải host session (Gap-#1).</exception>
    Task<MemberBillPreviewDto> GetMemberBillPreviewAsync(
        Guid sessionId,
        Guid memberId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// M2 / Task C2.7: Member (hoặc staff) thanh toán bill cá nhân bằng BVC.
    /// <para>
    /// Flow chi tiết (theo §C2):
    /// </para>
    /// <list type="number">
    ///   <item>Lookup session + member; validate trạng thái (Unpaid + NotPaid).</item>
    ///   <item>Validate distinct UserId (C2.4 Gap #7) — nếu có 2+ members cùng UserId → 409.</item>
    ///   <item>Tính totalDue; validate overpayment (C2.2 Gap #6).</item>
    ///   <item>Lock member row + wallet row (Serializable).</item>
    ///   <item>Idempotency check qua MemberPaymentAuditLog.IdempotencyKey (C2.5).</item>
    ///   <item>Call <see cref="IWalletService.DirectDebitForBillAsync"/>.</item>
    ///   <item>Update member: PaidBvcAmount, PaidCashRemainder, PaymentStatus, PaidAt, TransactionId.</item>
    ///   <item>Insert MemberPaymentAuditLog row.</item>
    ///   <item>Save + commit.</item>
    ///   <item>C2.10 All-paid trigger: nếu tất cả members đã paid → atomic flip session.Status = Paid.</item>
    /// </list>
    /// </summary>
    /// <param name="sessionId">ActiveSession.Id.</param>
    /// <param name="memberId">ActiveSessionMember.Id.</param>
    /// <param name="request">BvcAmount + IdempotencyKey.</param>
    /// <param name="actorUserId">User thực hiện (member.UserId hoặc staff).</param>
    /// <param name="actorRole">Role của actor ("Player" | "CafeStaff" | "Admin" | "Manager").</param>
    /// <param name="cancellationToken">Token hủy.</param>
    /// <returns>Response DTO với ledger entry + audit log id.</returns>
    /// <exception cref="NotFoundException">Session hoặc member không tồn tại.</exception>
    /// <exception cref="ConflictException">Session status không phải Unpaid, member đã paid, hoặc duplicate UserId.</exception>
    /// <exception cref="BadRequestException">Overpayment (BvcAmount > totalDue) hoặc wallet không đủ BVC.</exception>
    /// <exception cref="ForbiddenException">Actor (Player) không phải member này và không phải host session (Gap-#1).</exception>
    Task<MemberBillPaymentResponseDto> PayMemberBillAsync(
        Guid sessionId,
        Guid memberId,
        MemberBillPaymentRequestDto request,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// M2 / Task C2.8: Refund bill BVC đã thanh toán trước đó (Gap #11 bill sai / dispute).
    /// <para>
    /// Flow:
    /// </para>
    /// <list type="number">
    ///   <item>Lookup audit log; nếu đã refund (RefundedAt != null) → 409.</item>
    ///   <item>Call <see cref="IWalletService.RefundMemberBillAsync"/> để cộng BVC vào wallet.</item>
    ///   <item>Update audit log: RefundedAt, RefundReason, RefundLedgerEntryId.</item>
    ///   <item>Update member: BvcRefundedAt, BvcRefundReason, status = RefundedBvc.</item>
    /// </list>
    /// </summary>
    /// <param name="memberPaymentAuditLogId">ID của MemberPaymentAuditLog cần refund.</param>
    /// <param name="request">Reason + IdempotencyKey.</param>
    /// <param name="actorUserId">User thực hiện (staff/manager/admin).</param>
    /// <param name="cancellationToken">Token hủy.</param>
    /// <returns>Response DTO.</returns>
    /// <exception cref="NotFoundException">Audit log không tồn tại.</exception>
    /// <exception cref="ConflictException">Audit log đã refund rồi.</exception>
    /// <exception cref="ForbiddenException">Actor không đủ quyền (controller-level filter chỉ Admin/Manager/CafeStaff).</exception>
    Task<MemberBillRefundResponseDto> RefundMemberBillAsync(
        Guid memberPaymentAuditLogId,
        MemberBillRefundRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}