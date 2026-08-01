using BoardVerse.Core.DTOs.Pos;
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

        public CafePosController(ICafePosService posService)
        {
            _posService = posService;
        }

        /// <summary>
        /// Lấy sơ đồ bàn realtime cho Web POS. [Role: Manager — chủ quán; CafeStaff — đã gắn quán.]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <response code="200">Trả về danh sách bàn active kèm trạng thái (Available, InUse, Reserved, EventInProgress).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải Manager chủ quán hoặc CafeStaff chưa được gắn quán.</response>
        /// <response code="404">Quán không tồn tại hoặc không ở trạng thái ACTIVE.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("tables")]
        public async Task<IActionResult> GetTables(Guid cafeId)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.GetTablesAsync(cafeId, userId, role);
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
        /// Host-led check-in: Quét mã đặt chỗ (BookingCode) để kích hoạt phiên chơi cho cả nhóm.
        /// Nhân viên quét mã QR trên ứng dụng của Host để check-in toàn bộ thành viên trong nhóm.
        /// MDC Happy Path Step 9
        /// </summary>
        /// <param name="cafeId">Mã định danh quán.</param>
        /// <param name="request">Mã đặt chỗ, bàn và game barcode.</param>
        /// <response code="201">Check-in thành công, phiên chơi đã được kích hoạt.</response>
        /// <response code="400">Mã đặt chỗ không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Quán, bàn hoặc game không tồn tại.</response>
        /// <response code="409">Đơn đặt chỗ chưa thanh toán hoặc bàn/game không khả dụng.</response>
        [HttpPost("sessions/from-booking")]
        public async Task<IActionResult> StartSessionFromBooking(Guid cafeId, [FromBody] StartSessionFromBookingRequestDto request)
        {
            var (userId, role) = GetViewerContext();
            var result = await _posService.StartSessionFromBookingAsync(cafeId, userId, role, request);
            return this.NewResponse(201, ApiSuccessMessages.Pos.SessionStarted, result);
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
        /// BR-12: Bắt buộc kiểm kê trước khi in hóa đơn.
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
        /// BR-12: Mở khóa in hóa đơn khi kiểm kê xong.
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

        private (Guid UserId, string Role) GetViewerContext()
        {
            var userId = GetUserIdFromClaims();
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
            return (userId, role);
        }
    }
}
