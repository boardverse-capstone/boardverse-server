using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/cafes/{cafeId:guid}/sessions")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public class ActiveSessionController : BaseApiController
    {
        private readonly IActiveSessionService _sessionService;

        public ActiveSessionController(IActiveSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        /// <summary>
        /// Kiểm tra phiên chơi hiện tại. [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <response code="200">Chi tiết phiên chơi.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{sessionId:guid}")]
        public async Task<IActionResult> GetSession(Guid cafeId, Guid sessionId)
        {
            var result = await _sessionService.GetSessionAsync(cafeId, sessionId);
            return this.NewResponse(200, ApiSuccessMessages.Session.SessionRetrieved, result);
        }

        /// <summary>
        /// Thanh toán toàn bộ phiên chơi sau kiểm kê linh kiện. [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Kết quả kiểm kê linh kiện.</param>
        /// <response code="200">Phiên chuyển UNPAID/PAID và trả hóa đơn tóm tắt.</response>
        /// <response code="400">Thiếu kiểm kê hoặc dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{sessionId:guid}/checkout")]
        public async Task<IActionResult> Checkout(Guid cafeId, Guid sessionId, [FromBody] CheckoutRequestDto request)
        {
            var result = await _sessionService.CheckoutAsync(cafeId, sessionId, request);
            return this.NewResponse(200, ApiSuccessMessages.Session.SessionCheckedOut, result);
        }

        /// <summary>
        /// Thêm khách vô danh vào phiên chơi. [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Thông tin hiển thị của khách vô danh.</param>
        /// <response code="200">Đã thêm khách vô danh.</response>
        /// <response code="400">Thiếu thông tin hiển thị.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{sessionId:guid}/guest-slots")]
        public async Task<IActionResult> AddGuestSlot(Guid cafeId, Guid sessionId, [FromBody] AddGuestSlotRequestDto request)
        {
            var result = await _sessionService.AddGuestSlotAsync(cafeId, sessionId, request);
            return this.NewResponse(200, ApiSuccessMessages.Session.GuestSlotAdded, result);
        }

        /// <summary>
        /// Trả game toàn bộ - chuyển session sang CHECKING để kiểm kê linh kiện. [Role: Manager, CafeStaff]
        /// BR-12: Bắt buộc phải trả game trước khi checkout.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <response code="200">Đã trả game, chuyển sang kiểm kê.</response>
        /// <response code="409">Phiên không ở trạng thái ACTIVE.</response>
        [HttpPost("{sessionId:guid}/end-game")]
        public async Task<IActionResult> EndGame(Guid cafeId, Guid sessionId)
        {
            var result = await _sessionService.EndGameAsync(cafeId, sessionId);
            return this.NewResponse(200, "Đã trả game. Vui lòng kiểm kê linh kiện.", result);
        }

        /// <summary>
        /// Thanh toán một phần cho nhóm về sớm. [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Danh sách thành viên thanh toán sớm.</param>
        /// <response code="200">Phiên chuyển sang CHECKING; chờ kiểm kê linh kiện.</response>
        /// <response code="400">Thiếu danh sách thành viên.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{sessionId:guid}/partial-checkout")]
        public async Task<IActionResult> PartialCheckout(Guid cafeId, Guid sessionId, [FromBody] PartialCheckoutRequestDto request)
        {
            var result = await _sessionService.PartialCheckoutAsync(cafeId, sessionId, request);
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
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Không tìm thấy phiên chơi hoặc thành viên.</response>
        /// <response code="409">Phiên đích không hoạt động.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{sourceSessionId:guid}/merge")]
        public async Task<IActionResult> MergeSession(Guid cafeId, Guid sourceSessionId, [FromBody] MergeSessionRequestDto request)
        {
            var result = await _sessionService.MergeSessionAsync(cafeId, sourceSessionId, request);
            return this.NewResponse(200, "Đã ghép thành viên vào nhóm mới.", result);
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
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành quán.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="409">Phiên không ở trạng thái UNPAID.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{sessionId:guid}/pay")]
        public async Task<IActionResult> PaySession(Guid cafeId, Guid sessionId, [FromBody] PaySessionRequestDto request)
        {
            var result = await _sessionService.PaySessionAsync(cafeId, sessionId, request);
            return this.NewResponse(200, ApiSuccessMessages.Session.SessionPaid, result);
        }

        /// <summary>
        /// Gán thêm game vào phiên chơi. [Role: Manager, CafeStaff]
        /// Exception 6: Nhóm tự ý lấy thêm game.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Barcode game.</param>
        /// <response code="200">Đã gán game vào phiên.</response>
        /// <response code="400">Game đã được gán.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy game.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{sessionId:guid}/games")]
        public async Task<IActionResult> AttachGame(Guid cafeId, Guid sessionId, [FromBody] AttachGameRequestDto request)
        {
            var result = await _sessionService.AttachGameAsync(cafeId, sessionId, request);
            return this.NewResponse(200, "Đã gán game vào phiên chơi.", result);
        }

        /// <summary>
        /// Thêm thành viên đến muộn vào phiên. [Role: Manager, CafeStaff]
        /// Exception 8: Thêm 2 người bạn đến muộn vào nhóm đang chơi.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Danh sách userId thành viên.</param>
        /// <response code="200">Đã thêm thành viên.</response>
        /// <response code="400">Phiên không hoạt động.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{sessionId:guid}/members/add")]
        public async Task<IActionResult> AddLateMember(Guid cafeId, Guid sessionId, [FromBody] AddLateMemberRequestDto request)
        {
            var result = await _sessionService.AddLateMemberAsync(cafeId, sessionId, request);
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
        [HttpPost("{sessionId:guid}/inventory-loss")]
        public async Task<IActionResult> RecordInventoryLoss(Guid cafeId, Guid sessionId, [FromBody] RecordInventoryLossRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            await _sessionService.RecordInventoryLossAsync(cafeId, userId, sessionId, request);
            return this.NewResponse(200, "Đã ghi nhận hao hụt linh kiện.", new { });
        }

        /// <summary>
        /// Gợi ý quán thay thế khi hết chỗ. [Role: Player]
        /// Exception 1: Phòng đầy nhưng quán hết chỗ.
        /// </summary>
        /// <param name="gameTemplateId">Mã game.</param>
        /// <param name="memberCount">Số người.</param>
        /// <param name="scheduledTime">Giờ hẹn.</param>
        /// <response code="200">Danh sách quán gợi ý.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("alternative-cafes")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAlternativeCafes(
            Guid gameTemplateId,
            int memberCount,
            DateTime scheduledTime)
        {
            var result = await _sessionService.GetAlternativeCafesAsync(Guid.Empty, gameTemplateId, memberCount, scheduledTime);
            return this.NewResponse(200, "Danh sách quán gợi ý.", result);
        }

        /// <summary>
        /// Submit bảng kiểm kê linh kiện cho một game trong phiên chơi (BR-12).
        /// Nhân viên POS quét linh kiện thực tế; thiếu → cộng phí phạt + đánh dấu MissingComponents.
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="request">Kết quả đếm linh kiện từng component.</param>
        /// <response code="200">Đã lưu checklist; trả về phiên cập nhật.</response>
        /// <response code="400">Danh sách components rỗng.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phiên hoặc game.</response>
        /// <response code="409">Phiên không ở trạng thái CHECKING.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{sessionId:guid}/games/check")]
        public async Task<IActionResult> SubmitComponentCheck(Guid cafeId, Guid sessionId, [FromBody] SubmitComponentCheckRequestDto request)
        {
            if (request.Results == null || request.Results.Count == 0)
            {
                return this.NewResponse(400, "Cần ít nhất 1 component để kiểm kê.", new { });
            }

            var result = await _sessionService.SubmitComponentCheckAsync(cafeId, sessionId, request);
            return this.NewResponse(200, "Đã lưu bảng kiểm kê linh kiện.", result);
        }
    }
}
