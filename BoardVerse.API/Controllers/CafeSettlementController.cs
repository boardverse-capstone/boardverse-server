using BoardVerse.Core.Messages;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/cafes/{cafeId:guid}/settlements")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public class CafeSettlementController : BaseApiController
    {
        private readonly ISettlementService _settlementService;

        public CafeSettlementController(ISettlementService settlementService)
        {
            _settlementService = settlementService;
        }

        /// <summary>
        /// Lấy danh sách giải ngân deposit đang chờ xử lý. [Role: Manager, CafeStaff]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <response code="200">Danh sách bản ghi settlement.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền vận hành quán.</response>
        /// <response code="404">Không tìm thấy quán.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingSettlements(Guid cafeId)
        {
            var result = await _settlementService.GetPendingSettlementsAsync(cafeId);
            return this.NewResponse(200, "Lấy danh sách settlement đang chờ thành công.", result);
        }
    }
}
