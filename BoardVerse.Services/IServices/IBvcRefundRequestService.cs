using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Enum;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service xử lý yêu cầu hoàn BVC do player gửi, admin duyệt/từ chối.
/// BR-RISK-05: mọi admin action ghi audit log + PlayerActionHistory.
/// BR § III.3: ledger append-only — admin approve tạo entry AdminCredit mới.
/// BR § III.6: refund request lifecycle Pending → Approved/Rejected/Cancelled.
/// </summary>
public interface IBvcRefundRequestService
{
    /// <summary>
    /// Player tạo yêu cầu hoàn BVC.
    /// BR § XVII.1: IdempotencyKey bắt buộc (caller cung cấp).
    /// </summary>
    Task<RefundRequestResponseDto> CreateAsync(
        Guid userId,
        CreateRefundRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Player xem lịch sử refund request của chính mình (phân trang).</summary>
    Task<RefundRequestPageDto> GetMyRequestsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Player hủy refund request của mình khi còn Pending.</summary>
    Task CancelAsync(
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken = default);

    // ===== Admin endpoints =====

    /// <summary>Admin xem danh sách refund request (phân trang + filter).</summary>
    Task<RefundRequestPageDto> GetPagedAsync(
        RefundRequestStatus? statusFilter,
        Guid? userIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Admin xem chi tiết 1 refund request (kèm ledger entry context).</summary>
    Task<RefundRequestResponseDto?> GetByIdAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin resolve (approve/reject) refund request.
    /// Approve → tạo ledger AdminCredit + cộng ví + ghi PlayerActionHistory.
    /// Reject → chỉ update status + ghi PlayerActionHistory.
    /// </summary>
    Task<RefundRequestResponseDto> ResolveAsync(
        Guid requestId,
        ResolveRefundRequestDto request,
        Guid adminUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}