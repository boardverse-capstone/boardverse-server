using BoardVerse.Core.DTOs.Pos;
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
            return this.NewResponse(200, "Cafe tables retrieved successfully", result);
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
            return this.NewResponse(200, "Inventory boxes retrieved successfully", result);
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
            return this.NewResponse(200, "Inventory box retrieved successfully", result);
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
            return this.NewResponse(200, "Active sessions retrieved successfully", result);
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
            return this.NewResponse(201, "Game session started successfully", result);
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
            return this.NewResponse(200, "Game session ended successfully", result);
        }

        private (Guid UserId, string Role) GetViewerContext()
        {
            var userId = GetUserIdFromClaims();
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
            return (userId, role);
        }
    }
}
