using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/cafes/{cafeId:guid}/pos")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public class CafePosController : BaseApiController
    {
        private readonly ICafePosService _posService;
        private readonly IActiveSessionService _sessionService;

        public CafePosController(ICafePosService posService, IActiveSessionService sessionService)
        {
            _posService = posService;
            _sessionService = sessionService;
        }

        /// <summary>
        /// Lấy sơ đồ bàn realtime cho Web POS. [Role: Manager — chủ quán; CafeStaff — đã gắn quán.]
        /// GAP-21 Fix: Thêm query param includeOnlyAvailable.
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="includeOnlyAvailable">Mặc định true — chỉ trả bàn Available. false = trả tất cả bàn (kể cả InUse/Reserved) để POS monitor.</param>
        /// <response code="200">Trả về danh sách bàn active kèm trạng thái (Available, InUse, Reserved, EventInProgress).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải Manager chủ quán hoặc CafeStaff chưa được gắn quán.</response>
        /// <response code="404">Quán không tồn tại hoặc không ở trạng thái ACTIVE.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("tables")]
        public async Task<IActionResult> GetTables(Guid cafeId, [FromQuery] bool includeOnlyAvailable = true)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.GetTablesAsync(cafeId, userId, role, includeOnlyAvailable);
            return this.NewResponse(200, ApiSuccessMessages.Pos.TablesRetrieved, result);
        }

        /// <summary>
        /// Đồng bộ sơ đồ bàn — tạo mới, cập nhật hoặc xóa bàn. Hỗ trợ 2 shape:
        /// 1. Legacy: { "tableNames": ["Bàn 1", "Bàn 2"] } — chỉ tên, SeatCount giữ nguyên / default 4.
        /// 2. Mới: { "tables": [{ "name": "Bàn 1", "seatCount": 8, "sortOrder": 0 }] } — đầy đủ Name + SeatCount + SortOrder.
        /// Không gửi cả 2 cùng lúc. [Role: Manager — chủ quán]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="request">Danh sách bàn muốn đồng bộ (1 trong 2 shape).</param>
        /// <response code="200">Đồng bộ thành công, trả danh sách bàn hiện tại.</response>
        /// <response code="400">Payload rỗng, gửi cả 2 shape, hoặc seatCount/sortOrder ngoài range.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Manager chủ quán.</response>
        /// <response code="404">Quán không tồn tại.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPut("tables")]
        public async Task<IActionResult> SyncTables(Guid cafeId, [FromBody] SyncCafeTablesRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return this.NewResponse(400, "Dữ liệu không hợp lệ.", null);
            }

            var hasLegacy = request.TableNames != null && request.TableNames.Count > 0;
            var hasNew = request.Tables != null && request.Tables.Count > 0;

            if (hasLegacy && hasNew)
            {
                return this.NewResponse(400, "Chỉ được gửi một trong hai: 'tableNames' hoặc 'tables'.", null);
            }

            if (!hasLegacy && !hasNew)
            {
                return this.NewResponse(400, "Phải gửi 'tableNames' hoặc 'tables' với ít nhất 1 phần tử.", null);
            }

            var managerId = GetUserIdFromClaims();

            if (hasNew)
            {
                await _posService.SyncTablesAsync(cafeId, managerId, request.Tables!);
            }
            else
            {
                await _posService.SyncTablesAsync(cafeId, managerId, request.TableNames!);
            }

            var tables = await _posService.GetTablesAsync(cafeId, managerId, "Manager");
            return this.NewResponse(200, ApiSuccessMessages.Pos.TablesRetrieved, tables);
        }

        /// <summary>
        /// Cập nhật một phần thông tin bàn (Name, SeatCount, SortOrder). Dùng để đổi số ghế (SeatCount) cho booking capacity. [Role: Manager — chủ quán]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="tableId">Mã định danh bàn cần cập nhật.</param>
        /// <param name="request">Các trường muốn đổi (Name, SeatCount, SortOrder). Tất cả optional; ít nhất một phải có giá trị.</param>
        /// <response code="200">Cập nhật bàn thành công, trả về thông tin bàn sau khi sửa.</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc không có trường nào để cập nhật.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải Manager chủ quán.</response>
        /// <response code="404">Không tìm thấy bàn trong quán.</response>
        /// <response code="409">Bàn đang có phiên chơi hoạt động, hoặc tên bàn đã trùng với bàn khác.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPatch("tables/{tableId:guid}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> UpdateTable(
            Guid cafeId,
            Guid tableId,
            [FromBody] UpdateCafeTableRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return this.NewResponse(400, "Dữ liệu không hợp lệ.", null);
            }

            var managerId = GetUserIdFromClaims();
            var result = await _posService.UpdateCafeTableAsync(cafeId, managerId, tableId, request);
            return this.NewResponse(200, ApiSuccessMessages.Pos.TableUpdated, result);
        }

        /// <summary>
        /// Liệt kê hộp game vật lý (barcode + trạng thái) trong kho quán. [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="gameTemplateId">Tuỳ chọn — chỉ hộp thuộc tựa game này.</param>
        /// <response code="200">Danh sách hộp game (CafeInventoryBoxDto).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Quán không tồn tại hoặc không ACTIVE.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("boxes")]
        public async Task<IActionResult> GetBoxes(Guid cafeId, [FromQuery] Guid? gameTemplateId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.GetBoxesAsync(cafeId, userId, role, gameTemplateId);
            return this.NewResponse(200, ApiSuccessMessages.Pos.BoxesRetrieved, result);
        }

        /// <summary>
        /// Tra cứu một hộp game theo barcode sau khi quét POS. [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="barcode">Mã barcode in trên hộp game (vd. BV-bbbbbbbb-xxxxxxxx-001).</param>
        /// <response code="200">Thông tin hộp game và trạng thái hiện tại.</response>
        /// <response code="400">Barcode rỗng hoặc không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Quán hoặc hộp game không tồn tại.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("boxes/by-barcode/{barcode}")]
        public async Task<IActionResult> GetBoxByBarcode(Guid cafeId, string barcode)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.GetBoxByBarcodeAsync(cafeId, userId, role, barcode);
            return this.NewResponse(200, ApiSuccessMessages.Pos.BoxRetrieved, result);
        }

        /// <summary>
        /// Liệt kê phiên chơi đang active (phục vụ billing và tính thời gian chờ discovery). [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="gameTemplateId">Tuỳ chọn — lọc theo tựa game.</param>
        /// <response code="200">Danh sách ActiveSessionDto kèm elapsedMinutes và estimatedRemainingMinutes.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Quán không tồn tại hoặc không ACTIVE.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("sessions/active")]
        public async Task<IActionResult> GetActiveSessions(Guid cafeId, [FromQuery] Guid? gameTemplateId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.GetActiveSessionsAsync(cafeId, userId, role, gameTemplateId);
            return this.NewResponse(200, ApiSuccessMessages.Pos.SessionsRetrieved, result);
        }

        /// <summary>
        /// Lấy chi tiết một phiên chơi theo mã định danh. [Role: Manager, CafeStaff]
        /// GAP 1 Fix: API mới để frontend lấy chi tiết 1 session cụ thể.
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="sessionId">Mã phiên chơi cần lấy chi tiết.</param>
        /// <response code="200">Chi tiết phiên chơi.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Quán hoặc phiên chơi không tồn tại.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("sessions/{sessionId:guid}")]
        public async Task<IActionResult> GetSessionById(Guid cafeId, Guid sessionId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.GetSessionByIdAsync(cafeId, userId, role, sessionId);
            return this.NewResponse(200, "Lấy chi tiết phiên chơi thành công.", result);
        }

        /// <summary>
        /// Preview thông tin booking trước khi check-in.
        /// AC 1.1: Hiển thị danh sách thành viên + game info TRƯỚC khi check-in.
        /// Nhân viên quét mã đặt chỗ để xem thông tin chi tiết trước khi bấm xác nhận check-in.
        /// </summary>
        /// <param name="cafeId">Mã định danh quán.</param>
        /// <param name="bookingCode">Mã đặt chỗ (BookingCode/OrderId).</param>
        /// <response code="200">Thông tin booking preview.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy booking.</response>
        [HttpGet("bookings/{bookingCode}")]
        public async Task<IActionResult> GetBookingPreview(Guid cafeId, string bookingCode)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.GetBookingPreviewAsync(cafeId, userId, role, bookingCode);
            return this.NewResponse(200, "Lấy thông tin booking thành công.", result);
        }

        /// <summary>
        /// Giao hộp game cho bàn — bắt đầu phiên chơi (POS scan barcode). [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="request">cafeTableId và barcode hộp game cần giao.</param>
        /// <response code="201">Phiên chơi đã bắt đầu; hộp chuyển InUse, bàn chuyển InUse nếu đang trống.</response>
        /// <response code="400">Dữ liệu request không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Quán, bàn hoặc hộp game không tồn tại.</response>
        /// <response code="409">Hộp không Available, đã có session, hoặc bàn Reserved/EventInProgress.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("sessions")]
        public async Task<IActionResult> StartSession(Guid cafeId, [FromBody] StartGameSessionRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.StartGameSessionAsync(cafeId, userId, role, request);
            return this.NewResponse(201, ApiSuccessMessages.Pos.SessionStarted, result);
        }

        /// <summary>
        /// POS check-in: Staff quét QR (ReservationCode hoặc BookingCode legacy) để kích hoạt phiên chơi cho cả nhóm.
        ///
        /// BR §21A.7 — Host-led check-in:
        ///   > "Host đến quán, mở BookingSuccessPage hiển thị QR → Staff quét QR trên POS.
        ///   > POS validate: Booking tồn tại, status = confirmed, Nonce chưa được sử dụng,
        ///   > Thời gian nằm trong khung giờ cho phép.
        ///   > Booking.status = checkedIn, Game copy held → inUse, bắt đầu billing session."
        ///
        /// Code detection (BR §21A.7 + ReservationCodeDetector):
        ///   - ReservationCode (8-char alphanumeric, exclude 0/1/I/O) → BVC Reservation flow.
        ///   - BookingCode "BV{N}" → VND BookingDeposit flow (backward compat).
        ///
        /// Idempotent: scan cùng QR trả về cùng response (cùng ActiveSessionId).
        /// </summary>
        /// <param name="cafeId">Mã định danh quán staff đang vận hành.</param>
        /// <param name="request">Mã check-in (ReservationCode | BookingCode), bàn và barcode hộp game.</param>
        /// <response code="201">Check-in thành công, phiên chơi đã được kích hoạt (ActiveSession created).</response>
        /// <response code="400">Dữ liệu request không hợp lệ hoặc ngoài time window check-in.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Reservation/Booking, quán, bàn hoặc game không tồn tại.</response>
        /// <response code="409">Reservation không thuộc cafe, sai cafe, chưa Confirmed, hoặc bàn/game không khả dụng.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("check-in")]
        public async Task<IActionResult> CheckIn(Guid cafeId, [FromBody] CheckInRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.CheckInByCodeAsync(cafeId, userId, role, request);
            return this.NewResponse(201, ApiSuccessMessages.Pos.SessionStarted, result);
        }

        /// <summary>
        /// POS tạo mã QR cho player scan check-in (BR §21A.7 — 2 chiều check-in).
        /// Staff bấm "Tạo QR mời khách scan" → server sinh token 16-char alphanumeric → lưu DB → trả QR payload.
        /// Player mở app → scan QR POS → server lookup token → check-in vào cùng reservation.
        ///
        /// Token có TTL 30 phút mặc định (tối đa 240 phút qua <c>ttlMinutes</c>). Mỗi token chỉ dùng 1 lần.
        /// Có thể gắn với 1 reservation cụ thể qua <c>reservationId</c>; nếu trống → token dùng cho walk-in/general.
        /// </summary>
        /// <param name="cafeId">Mã định danh quán staff đang vận hành.</param>
        /// <param name="request">ReservationId (optional) + TTL tùy chỉnh (optional).</param>
        /// <response code="201">Tạo QR token thành công, trả token + QrPayload sẵn sàng hiển thị.</response>
        /// <response code="400">Request không hợp lệ (TTL âm/vượt giới hạn, reservation không tồn tại).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Quán hoặc reservation không tồn tại.</response>
        /// <response code="409">Reservation không thuộc cafe hiện tại.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("check-in-tokens")]
        public async Task<IActionResult> CreateCheckInToken(
            Guid cafeId,
            [FromBody] CreatePosCheckInTokenRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.CreateCheckInTokenAsync(cafeId, userId, role, request);
            return this.NewResponse(201, ApiSuccessMessages.Pos.CheckInTokenCreated, result);
        }

        /// <summary>
        /// Kết thúc phiên chơi — trả hộp game và giải phóng bàn nếu không còn session khác. [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="sessionId">Mã phiên chơi active cần kết thúc.</param>
        /// <response code="200">Phiên đã đóng; hộp về Available; bàn về Available khi không còn session trên bàn đó.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Quán hoặc phiên chơi active không tồn tại.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("sessions/{sessionId:guid}/end")]
        public async Task<IActionResult> EndSession(Guid cafeId, Guid sessionId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.EndGameSessionAsync(cafeId, userId, role, sessionId);
            return this.NewResponse(200, ApiSuccessMessages.Pos.SessionEnded, result);
        }

        /// <summary>
        /// Lấy bảng kiểm kê linh kiện số hóa của một game trong phiên. [Role: Manager, CafeStaff]
        /// BR-12: Bắt buộc kiểm kê trước khi in hóa đơn. Response chỉ chứa mô tả linh kiện
        /// (ExpectedQuantity), chưa có số liệu thực tế — gọi POST để verify và lấy ActualQuantity + PenaltyFee.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionGameId">Mã session game (ActiveSessionGame).</param>
        /// <response code="200">Danh sách linh kiện cần kiểm.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy session game.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("sessions/{sessionGameId:guid}/component-checklist")]
        public async Task<IActionResult> GetComponentChecklist(Guid cafeId, Guid sessionGameId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.GetComponentChecklistAsync(cafeId, userId, role, sessionGameId);
            return this.NewResponse(200, "Lấy bảng kiểm kê linh kiện thành công.", result);
        }

        /// <summary>
        /// Xác nhận kiểm kê linh kiện và tính phí phạt nếu thiếu. [Role: Manager, CafeStaff]
        /// BR-12: Mở khóa in hóa đơn khi kiểm kê xong. Response là ComponentCheckResultDto
        /// (khác với GET) — chứa ActualQuantity + PenaltyFee từng linh kiện, tổng TotalPenaltyAmount
        /// và trạng thái CheckStatus (Verified | MissingComponents).
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="request">Kết quả kiểm kê từng linh kiện.</param>
        /// <response code="200">Kiểm kê hoàn tất, trả kết quả kèm phí phạt.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy session game.</response>
        /// <response code="409">Đã kiểm kê rồi.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/component-check")]
        public async Task<IActionResult> SubmitComponentCheck(Guid cafeId, [FromBody] SubmitComponentCheckRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.SubmitComponentCheckAsync(cafeId, userId, role, request);
            return this.NewResponse(200, "Xác nhận kiểm kê linh kiện thành công.", result);
        }

        /// <summary>
        /// Reset lại checklist linh kiện để kiểm tra lại. [Role: Manager, CafeStaff]
        /// GAP-25 Fix: Cho phép staff reset checklist nếu đã kiểm tra sai.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionGameId">Mã session game cần reset checklist.</param>
        /// <response code="200">Reset thành công, trả lại checklist.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy session game.</response>
        /// <response code="409">Phiên không ở trạng thái CHECKING.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/component-check/reset")]
        public async Task<IActionResult> ResetComponentCheck(Guid cafeId, [FromQuery] Guid sessionGameId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.ResetComponentCheckAsync(cafeId, userId, role, sessionGameId);
            return this.NewResponse(200, "Đã reset checklist để kiểm tra lại.", result);
        }

        /// <summary>
        /// Khôi phục phiên từ CHECKING về ACTIVE. [Role: Manager, CafeStaff]
        /// GAP-1 Fix: Cho phép staff hủy bỏ thao tác "Trả game" nếu bấm nhầm.
        /// Chỉ hoạt động khi chưa có thành viên nào được thanh toán (chưa có member FINISHED).
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi cần khôi phục.</param>
        /// <response code="200">Khôi phục thành công, phiên quay về ACTIVE.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="409">Không thể khôi phục (đã có member được thanh toán hoặc phiên không ở CHECKING).</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/resume")]
        public async Task<IActionResult> ResumeSession(Guid cafeId, Guid sessionId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _sessionService.ResumeSessionAsync(cafeId, sessionId);
            return this.NewResponse(200, "Đã khôi phục phiên về trạng thái ACTIVE.", result);
        }

        /// <summary>
        /// L-05: Tạm dừng phiên chơi — timer ngừng đếm. [Role: Manager, CafeStaff]
        /// Chỉ hoạt động khi phiên đang ACTIVE và chưa bị tạm dừng.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi cần tạm dừng.</param>
        /// <response code="200">Tạm dừng thành công.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="409">Phiên không ở trạng thái ACTIVE hoặc đã bị tạm dừng.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/pause")]
        public async Task<IActionResult> PauseSession(Guid cafeId, Guid sessionId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _sessionService.PauseSessionAsync(cafeId, sessionId);
            return this.NewResponse(200, "Đã tạm dừng phiên chơi.", result);
        }

        /// <summary>
        /// L-05: Tiếp tục lại phiên đang bị tạm dừng — timer tiếp tục đếm. [Role: Manager, CafeStaff]
        /// Chỉ hoạt động khi phiên đang ACTIVE và IsPaused = true.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi cần tiếp tục.</param>
        /// <response code="200">Tiếp tục thành công.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="409">Phiên không bị tạm dừng.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/resume-pause")]
        public async Task<IActionResult> ResumePauseSession(Guid cafeId, Guid sessionId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _sessionService.ResumeFromPauseAsync(cafeId, sessionId);
            return this.NewResponse(200, "Đã tiếp tục phiên chơi.", result);
        }

        /// <summary>
        /// Xử lý trả game: tính surcharge_fine từ linh kiện lỗi, cập nhật box status nếu hỏng.
        /// POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/return-game
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Mảng linh kiện lỗi (ID, số lượng mất, số lượng hỏng).</param>
        /// <response code="200">Xử lý thành công, trả surcharge_fine.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên chơi hoặc hộp game.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/return-game")]
        public async Task<IActionResult> ReturnGame(Guid cafeId, Guid sessionId, [FromBody] ReturnGameRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.ReturnGameAsync(cafeId, userId, role, sessionId, request);
            return this.NewResponse(200, "Xử lý trả game thành công.", result);
        }

        // ====== Billing Operations ======

        /// <summary>
        /// Gán thêm game vào phiên chơi. [Role: Manager, CafeStaff]
        /// Exception 6: Nhóm tự ý lấy thêm game mà không báo nhân viên.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Barcode game cần gán.</param>
        /// <response code="200">Đã gán game vào phiên.</response>
        /// <response code="400">Game đã được gán.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy game.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/games")]
        public async Task<IActionResult> AttachGame(Guid cafeId, Guid sessionId, [FromBody] AttachGameRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.AttachGameAsync(cafeId, userId, role, sessionId, request);
            return this.NewResponse(200, "Đã gán game vào phiên chơi.", result);
        }

        /// <summary>
        /// Thêm khách vô danh vào phiên chơi. [Role: Manager, CafeStaff]
        /// Exception 10: Khách không có ứng dụng hoặc điện thoại hết pin.
        /// BR-13: Guest slot không chịu trách nhiệm tài sản độc lập.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Thông tin hiển thị của khách vô danh.</param>
        /// <response code="200">Đã thêm khách vô danh.</response>
        /// <response code="400">Phiên đã kết thúc.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/guest-slots")]
        public async Task<IActionResult> AddGuestSlot(Guid cafeId, Guid sessionId, [FromBody] AddGuestSlotRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.AddGuestSlotAsync(cafeId, userId, role, sessionId, request);
            return this.NewResponse(200, "Đã thêm khách vô danh.", result);
        }

        /// <summary>
        /// Thêm thành viên đến muộn vào phiên. [Role: Manager, CafeStaff]
        /// Exception 8: Thêm 2 người bạn đến muộn vào nhóm đang chơi.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Danh sách userId thành viên đến muộn.</param>
        /// <response code="200">Đã thêm thành viên.</response>
        /// <response code="400">Phiên không hoạt động.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/members/add")]
        public async Task<IActionResult> AddLateMember(Guid cafeId, Guid sessionId, [FromBody] AddLateMemberRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.AddLateMemberAsync(cafeId, userId, role, sessionId, request);
            return this.NewResponse(200, "Đã thêm thành viên đến muộn.", result);
        }

        /// <summary>
        /// Ghi nhận hao hụt linh kiện trước phiên. [Role: Manager, CafeStaff]
        /// Exception 7: Nhân viên ca chiều phát hiện game thiếu từ ca sáng.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Thông tin hao hụt.</param>
        /// <response code="200">Đã ghi nhận hao hụt.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/inventory-loss")]
        public async Task<IActionResult> RecordInventoryLoss(Guid cafeId, Guid sessionId, [FromBody] RecordInventoryLossRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            await _posService.RecordInventoryLossAsync(cafeId, userId, role, sessionId, request);
            return this.NewResponse(200, "Đã ghi nhận hao hụt linh kiện.", new { });
        }

        /// <summary>
        /// P-04: Ghi nhận hao hụt linh kiện TRƯỚC KHI có phiên chơi — dùng cho shift handoff.
        /// Endpoint này không cần sessionId, chỉ cần cafeId + gameInventoryBoxId.
        /// [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="request">Thông tin game box và danh sách linh kiện thiếu/hỏng.</param>
        /// <response code="200">Đã ghi nhận hao hụt.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Game box không tồn tại.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("inventory-loss/pre-session")]
        public async Task<IActionResult> RecordPreSessionInventoryLoss(Guid cafeId, [FromBody] RecordPreSessionInventoryLossRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            await _posService.RecordPreSessionInventoryLossAsync(cafeId, userId, role, request);
            return this.NewResponse(200, "Đã ghi nhận hao hụt linh kiện trước ca làm việc.", new { });
        }

        // ====== Checkout & Payment Operations ======

        /// <summary>
        /// Thanh toán toàn bộ phiên chơi sau kiểm kê linh kiện. [Role: Manager, CafeStaff]
        /// BR-12: Chỉ gọi được khi session ở trạng thái CHECKING và đã kiểm kê đủ.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Kết quả kiểm kê linh kiện.</param>
        /// <response code="200">Phiên chuyển UNPAID/PAID và trả hóa đơn tóm tắt.</response>
        /// <response code="400">Thiếu kiểm kê hoặc dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên.</response>
        /// <response code="409">Phiên không ở trạng thái CHECKING.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/checkout")]
        public async Task<IActionResult> Checkout(Guid cafeId, Guid sessionId, [FromBody] CheckoutRequestDto request)
        {
            // GAP-7 Fix: Pass actual userId/role to EnsurePosAccessAsync
            var (userId, role) = GetViewerContext();
            var result = await _posService.CheckoutAsync(cafeId, userId, role, sessionId, request);
            return this.NewResponse(200, ApiSuccessMessages.Session.SessionCheckedOut, result);
        }

        /// <summary>
        /// Thanh toán hóa đơn tổng của phiên chơi. [Role: Manager, CafeStaff]
        /// BR-15: TotalAmount = Subtotal + PenaltyAmount - DepositAppliedAmount
        /// BR-09: Deposit chỉ cấn trừ DUY NHẤT 1 LẦN vào hóa đơn tổng
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Thông tin thanh toán: phí phạt linh kiện.</param>
        /// <response code="200">Thanh toán thành công; phiên chuyển PAID.</response>
        /// <response code="400">Phiên không ở trạng thái UNPAID hoặc có lỗi dữ liệu.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên.</response>
        /// <response code="409">Phiên không ở trạng thái UNPAID.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/pay")]
        public async Task<IActionResult> PaySession(Guid cafeId, Guid sessionId, [FromBody] PaySessionRequestDto request)
        {
            // GAP-7 Fix: Pass actual userId/role to EnsurePosAccessAsync
            var (userId, role) = GetViewerContext();
            var result = await _posService.PaySessionAsync(cafeId, userId, role, sessionId, request);
            return this.NewResponse(200, ApiSuccessMessages.Session.SessionPaid, result);
        }

        /// <summary>
        /// Thanh toán một phần cho nhóm về sớm. [Role: Manager, CafeStaff]
        /// BR-12: Khóa in hóa đơn đến khi kiểm kê xong.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Danh sách thành viên thanh toán sớm.</param>
        /// <response code="200">Phiên chuyển sang CHECKING; chờ kiểm kê linh kiện.</response>
        /// <response code="400">Thiếu danh sách thành viên.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sessionId:guid}/partial-checkout")]
        public async Task<IActionResult> PartialCheckout(Guid cafeId, Guid sessionId, [FromBody] PartialCheckoutRequestDto request)
        {
            // GAP-7 Fix: Pass actual userId/role to EnsurePosAccessAsync
            var (userId, role) = GetViewerContext();
            var result = await _posService.PartialCheckoutAsync(cafeId, userId, role, sessionId, request);
            return this.NewResponse(200, ApiSuccessMessages.Session.PartialCheckoutRequested, result);
        }

        /// <summary>
        /// Ghép thành viên vào phiên chơi của nhóm mới. [Role: Manager, CafeStaff]
        /// Exception 4: A3 nhảy từ nhóm A sang nhóm B.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sourceSessionId">Mã phiên chơi nguồn (nhóm cũ).</param>
        /// <param name="request">Mã thành viên và mã phiên đích.</param>
        /// <response code="200">Đã ghép thành viên vào nhóm mới.</response>
        /// <response code="400">Thành viên không ở trạng thái SUSPENDED_MUTATION.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên hoặc thành viên.</response>
        /// <response code="409">Phiên đích không hoạt động.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("sessions/{sourceSessionId:guid}/merge")]
        public async Task<IActionResult> MergeSession(Guid cafeId, Guid sourceSessionId, [FromBody] MergeSessionRequestDto request)
        {
            // GAP-7 Fix: Pass actual userId/role to EnsurePosAccessAsync
            var (userId, role) = GetViewerContext();
            var result = await _posService.MergeSessionAsync(cafeId, userId, role, sourceSessionId, request);
            return this.NewResponse(200, "Đã ghép thành viên vào nhóm mới.", result);
        }

        private (Guid UserId, string Role) GetViewerContext()
        {
            var userId = GetUserIdFromClaims();
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
            return (userId, role);
        }
    }
}
