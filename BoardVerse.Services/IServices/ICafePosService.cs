using BoardVerse.Core.DTOs.Common;
using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Enum;

namespace BoardVerse.Services.IServices
{
    public interface ICafePosService
    {
        Task<IReadOnlyList<CafeTableStatusDto>> GetTablesAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            bool includeOnlyAvailable = true,
            bool includeInactive = false,
            IReadOnlyCollection<CafeTableStatus>? statuses = null);

        /// <summary>
        /// Legacy overload — đồng bộ chỉ tên bàn (giữ nguyên SeatCount cũ, default 4 cho bàn mới).
        /// </summary>
        Task SyncTablesAsync(Guid cafeId, Guid managerId, IReadOnlyList<string> tableNames);

        /// <summary>
        /// Overload mới — đồng bộ cả Name + SeatCount + SortOrder trong một lần PUT.
        /// PUT /api/cafes/{cafeId}/pos/tables shape mới.
        /// </summary>
        Task SyncTablesAsync(Guid cafeId, Guid managerId, IReadOnlyList<CafeTableSyncItem> tables);

        Task<CafeTableStatusDto> UpdateCafeTableAsync(
            Guid cafeId,
            Guid managerId,
            Guid tableId,
            UpdateCafeTableRequestDto request);
        Task<IReadOnlyList<CafeInventoryBoxDto>> GetBoxesAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? gameTemplateId);
        Task<CafeInventoryBoxDto> GetBoxByBarcodeAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            string barcode);
        Task<IReadOnlyList<ActiveSessionDto>> GetActiveSessionsAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? gameTemplateId);

        /// <summary>
        /// Lấy danh sách phiên chơi đang ở trạng thái UNPAID (chờ thanh toán).
        /// POS staff dùng để scan "phiên nào chờ tôi thanh toán?" — đặc biệt sau khi đã end-game.
        /// </summary>
        /// <param name="sessionId">
        /// Optional — nếu truyền, trả về session cụ thể đó (nếu đang UNPAID).
        /// Dùng khi nhân viên muốn lấy lại hóa đơn của một phiên cụ thể.
        /// </param>
        Task<IReadOnlyList<ActiveSessionDto>> GetUnpaidSessionsAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? sessionId = null);

        /// <summary>
        /// Lấy danh sách phiên chơi đã thanh toán (PAID) theo khoảng ngày + phân trang.
        /// POS manager dùng cho end-of-day report / đối soát SePay / cash reconciliation.
        /// </summary>
        Task<PaginatedResult<PaidSessionDto>> GetPaidSessionsPagedAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            GetPaidSessionsQuery query);

        /// <summary>
        /// GAP 1 Fix: Get session by ID for frontend to view session details.
        /// </summary>
        Task<ActiveSessionDto> GetSessionByIdAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId);

        Task<ActiveSessionDto> StartGameSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            StartGameSessionRequestDto request);

        /// <summary>
        /// POS check-in: Staff quét QR (ReservationCode hoặc BookingCode legacy) để kích hoạt phiên chơi.
        /// BR §21A.7 — Host-led check-in.
        /// </summary>
        Task<ActiveSessionDto> CheckInByCodeAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            CheckInRequestDto request);

        /// <summary>
        /// Preview booking info trước khi check-in.
        /// AC 1.1: Hiển thị danh sách thành viên + game info TRƯỚC khi check-in.
        /// </summary>
        Task<BookingPreviewDto> GetBookingPreviewAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            string bookingCode);

        Task<ActiveSessionDto> EndGameSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId);

        // BR-12: Component Checklist
        // GET: trả về mô tả các linh kiện cần kiểm (chưa có số liệu thực tế).
        Task<ComponentChecklistDto> GetComponentChecklistAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionGameId);
        // POST verify: lưu kết quả kiểm kê, tính phí phạt, trả ComponentCheckResultDto.
        Task<ComponentCheckResultDto> SubmitComponentCheckAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            SubmitComponentCheckRequestDto request);

        // GAP-25 Fix: Reset checklist — cho phép staff reset lại checklist nếu đã kiểm tra sai
        Task<ComponentChecklistDto> ResetComponentCheckAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionGameId);

        // Box history #1: trả lịch sử các lần hộp bị ghi nhận MissingComponents
        // qua các phiên trước, kèm linh kiện thiếu + staff + member chịu trách nhiệm.
        // Staff dùng trước khi giao hộp cho khách phiên mới.
        /// <param name="sessionId">
        /// Optional: nếu truyền, chỉ trả incidents thuộc phiên chơi này.
        /// Nếu null/empty Guid, trả tất cả incidents của hộp.
        /// </param>
        Task<BoxComponentHistoryDto> GetBoxComponentHistoryAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid boxId,
            Guid? sessionId = null);

        // Return Game: tính surcharge_fine
        Task<ReturnGameResponseDto> ReturnGameAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            ReturnGameRequestDto request);

        // ====== Billing Operations (delegates to ActiveSessionService) ======
        // AttachGame: Nhóm tự ý lấy thêm game (Exception 6)
        Task<ActiveSessionDto> AttachGameAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            AttachGameRequestDto request);

        // AddGuestSlot: Thêm khách vô danh (Exception 10)
        Task<ActiveSessionDto> AddGuestSlotAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            AddGuestSlotRequestDto request);

        // AddLateMember: Thêm thành viên đến muộn (Exception 8)
        Task<ActiveSessionDto> AddLateMemberAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            AddLateMemberRequestDto request);

        // RecordInventoryLoss: Ghi nhận hao hụt trước phiên (Exception 7)
        Task RecordInventoryLossAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            RecordInventoryLossRequestDto request);

        // P-04: Ghi nhận hao hụt TRƯỚC KHI có phiên chơi (shift handoff)
        Task RecordPreSessionInventoryLossAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            RecordPreSessionInventoryLossRequestDto request);

        // ====== Checkout & Payment Operations ======
        // Checkout: Thanh toán toàn bộ sau kiểm kê (BR-12)
        // GAP-7 Fix: Nhận userId/userRole để EnsurePosAccessAsync đúng cách.
        Task<ActiveSessionResponseDto> CheckoutAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            CheckoutRequestDto request);

        // Pay: Thanh toán hóa đơn tổng (BR-15)
        // GAP-7 Fix: Nhận userId/userRole để EnsurePosAccessAsync đúng cách.
        Task<PaySessionResponseDto> PaySessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            PaySessionRequestDto request);

        // PartialCheckout: Thanh toán một phần cho thành viên về sớm
        // GAP-7 Fix: Nhận userId/userRole để EnsurePosAccessAsync đúng cách.
        Task<ActiveSessionResponseDto> PartialCheckoutAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            PartialCheckoutRequestDto request);

        // ====== POS QR 2-chiều check-in (BR §21A.7) ======
        Task<PosCheckInTokenDto> CreateCheckInTokenAsync(
            Guid cafeId,
            Guid staffUserId,
            string staffRole,
            CreatePosCheckInTokenRequestDto request);

        // Merge: Ghép thành viên vào nhóm mới (Exception 4)
        // GAP-7 Fix: Nhận userId/userRole để EnsurePosAccessAsync đúng cách.
        Task<MergeSessionResponseDto> MergeSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sourceSessionId,
            MergeSessionRequestDto request);

        // Phase 4 / EC-11: ghi audit log khi player khiếu nại giờ chơi.
        // BR §XX evidence: POS logs (StartedAt scan QR + EndedAt POS button) là definitive.
        // Endpoint này chỉ audit — KHÔNG tự ý sửa hóa đơn. Manager review/override sau.
        Task<DisputePlayedTimeResponseDto> DisputePlayedTimeAsync(
            Guid cafeId,
            Guid staffUserId,
            string staffRole,
            DisputePlayedTimeRequestDto request);

        /// <summary>
        /// Phase 5 / EC-11 — Manager override played time (BR-REFUND-07).
        /// Manager review dispute evidence, set <c>NewTotalMinutesPlayed</c> mới.
        /// Service recalc <c>Subtotal</c> + <c>TotalAmount</c> và ghi audit log với
        /// <c>ActionType=PlayedTimeOverridden (=41)</c>.
        ///
        /// Quyền: <b>Manager only</b>.
        /// Điều kiện tiên quyết:
        /// <list type="bullet">
        ///   <item><description>Phải có ít nhất 1 dispute audit (PlayedTimeDisputed) cho session trước đó.</description></item>
        ///   <item><description>Session chưa ở trạng thái Paid.</description></item>
        /// </list>
        /// </summary>
        Task<OverridePlayedTimeResponseDto> OverridePlayedTimeAsync(
            Guid cafeId,
            Guid managerUserId,
            string managerRole,
            OverridePlayedTimeRequestDto request);
    }
}
