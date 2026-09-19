using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.DTOs.Session;

namespace BoardVerse.Services.IServices
{
    public interface ISplitBillService
    {
        /// <summary>
        /// Lấy trạng thái thanh toán per-member của session.
        /// </summary>
        Task<SessionPaymentStatusDto> GetSessionPaymentStatusAsync(Guid sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Thanh toán cho các thành viên cụ thể.
        /// - CASH: xác nhận thanh toán ngay
        /// - QR_CODE: tạo QR cho từng thành viên
        /// </summary>
        Task<List<MemberPaymentResponseDto>> PayMembersAsync(
            Guid sessionId,
            PayMemberRequestDto request,
            Guid staffId,
            string actorRole,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Tạo QR thanh toán cho một thành viên cụ thể.
        /// </summary>
        Task<MemberPaymentResponseDto> CreateMemberQrAsync(
            Guid sessionId,
            Guid memberId,
            Guid staffId,
            string actorRole,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Xác nhận thanh toán tiền mặt cho một thành viên.
        /// </summary>
        Task<MemberPaymentResponseDto> ConfirmMemberCashAsync(
            Guid sessionId,
            Guid memberId,
            decimal amount,
            Guid staffId,
            string actorRole,
            string? notes = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Xử lý webhook SePay cho thanh toán QR của member.
        /// </summary>
        Task ProcessMemberQrWebhookAsync(MemberPaymentWebhookDto webhook, CancellationToken cancellationToken = default);

        /// <summary>
        /// Xác nhận thanh toán QR đã được chuyển khoản (manual confirmation).
        /// </summary>
        Task<MemberPaymentResponseDto> ConfirmMemberQrAsync(
            Guid sessionId,
            Guid memberId,
            Guid staffId,
            string actorRole,
            string? notes = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Tạo lại QR thanh toán cho một thành viên (khi QR cũ bị lỗi/hết hạn).
        /// Chỉ áp dụng khi member đã chọn paymentMethod=QR_CODE nhưng chưa trả.
        /// </summary>
        Task<MemberPaymentResponseDto> RegenerateMemberQrAsync(
            Guid sessionId,
            Guid memberId,
            Guid staffId,
            string actorRole,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy thông tin QR code của một thành viên để hiển thị trên mobile.
        /// Staff/Admin: có thể xem QR của bất kỳ thành viên nào.
        /// Player: chỉ xem được QR của chính mình.
        /// Trả về null nếu member chưa được tạo QR (PaymentMethod != QR_CODE).
        /// </summary>
        Task<MemberQrResponseDto?> GetMemberQrAsync(
            Guid memberId,
            Guid requesterId,
            string requesterRole,
            CancellationToken cancellationToken = default);
    }
}
