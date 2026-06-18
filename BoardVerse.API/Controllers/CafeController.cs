using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/cafes")]
    public class CafeController : BaseApiController
    {
        private readonly ICafeService _cafeService;

        public CafeController(ICafeService cafeService)
        {
            _cafeService = cafeService;
        }

        /// <summary>
        /// Tìm quán đối tác ACTIVE gần vị trí player, có thể lọc theo tựa game (PostGIS). [Role: Public]
        /// </summary>
        /// <param name="latitude">Vĩ độ player (WGS84, -90 đến 90).</param>
        /// <param name="longitude">Kinh độ player (WGS84, -180 đến 180).</param>
        /// <param name="radiusKm">Bán kính tìm kiếm km (mặc định 15; cho phép 0.1–50).</param>
        /// <param name="gameTemplateId">Bắt buộc — chỉ quán có hộp game Available hoặc InUse của tựa này (AC 2.1).</param>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Kích thước trang (mặc định 20).</param>
        /// <response code="200">Danh sách quán phân trang; khi rỗng kèm emptyResultMessage và alternativeSuggestions (AC 5.1, 5.2).</response>
        /// <response code="400">Tọa độ, bán kính hoặc gameTemplateId không hợp lệ.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("nearby")]
        [AllowAnonymous]
        public async Task<IActionResult> GetNearbyCafes(
            [FromQuery] double latitude,
            [FromQuery] double longitude,
            [FromQuery] Guid gameTemplateId,
            [FromQuery] double radiusKm = GeoLocationHelper.DefaultNearbyRadiusKm,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await _cafeService.GetNearbyCafesAsync(
                latitude,
                longitude,
                radiusKm,
                gameTemplateId,
                pagination);
            return this.NewResponse(200, "Nearby cafes retrieved successfully", result);
        }

        /// <summary>
        /// Tìm quán gần vị trí đã lưu trên profile (LastKnown GPS/map pin). [Role: Player, Manager, CafeStaff, Admin]
        /// </summary>
        /// <param name="gameTemplateId">Bắt buộc — tựa game player đã chọn (AC 2.1).</param>
        /// <param name="radiusKm">Bán kính tìm kiếm km (mặc định 15; cho phép 0.1–50).</param>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Kích thước trang (mặc định 20).</param>
        /// <response code="200">Cùng shape như GET /nearby; dùng tọa độ từ profile thay vì query lat/lng.</response>
        /// <response code="400">Chưa lưu vị trí (gọi PUT /api/userprofile/me/location trước), hoặc gameTemplateId/bán kính không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("nearby/me")]
        [Authorize]
        public async Task<IActionResult> GetNearbyCafesForCurrentUser(
            [FromQuery] Guid gameTemplateId,
            [FromQuery] double radiusKm = GeoLocationHelper.DefaultNearbyRadiusKm,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = GetUserIdFromClaims();
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await _cafeService.GetNearbyCafesForCurrentUserAsync(
                userId,
                radiusKm,
                gameTemplateId,
                pagination);
            return this.NewResponse(200, "Nearby cafes retrieved successfully", result);
        }

        /// <summary>
        /// Xem thông tin quán cafe (public). [Role: Public]
        /// </summary>
        /// <param name="id">Mã định danh quán cafe.</param>
        /// <response code="200">Trả về thông tin quán (tên, địa chỉ, mô tả, ...).</response>
        /// <response code="404">Không tìm thấy quán hoặc quán đã bị vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCafe(Guid id)
        {
            var cafe = await _cafeService.GetCafeAsync(id);
            return this.NewResponse(200, "Cafe retrieved successfully", cafe);
        }

        /// <summary>
        /// Cập nhật thông tin quán. [Role: Manager — phải là chủ quán (ManagerId của cafe).]
        /// </summary>
        /// <param name="id">Mã định danh quán cafe.</param>
        /// <param name="dto">Thông tin cần cập nhật (chỉ gửi field muốn đổi).</param>
        /// <response code="200">Cập nhật quán thành công, trả về thông tin quán sau khi sửa.</response>
        /// <response code="400">Dữ liệu không hợp lệ (độ dài name/address/phone/description vượt giới hạn).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán (ManagerId khác).</response>
        /// <response code="404">Không tìm thấy quán.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> UpdateCafe(Guid id, [FromBody] UpdateCafeRequestDto dto)
        {
            var managerId = GetUserIdFromClaims();
            var cafe = await _cafeService.UpdateCafeAsync(id, managerId, dto);
            return this.NewResponse(200, "Cafe updated successfully", cafe);
        }

        /// <summary>
        /// Thêm nhân viên CafeStaff vào quán (tạo tài khoản mới hoặc gắn staff đã có). [Role: Manager — phải là chủ quán (ManagerId của cafe).]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="dto">Email bắt buộc; username/password bắt buộc khi tạo tài khoản mới.</param>
        /// <response code="200">Thêm hoặc gắn nhân viên thành công.</response>
        /// <response code="400">Email/username/password không hợp lệ; thiếu username khi tạo mới; user có role Player (chưa promote) — gọi POST .../staff/promote trước; không thể thêm Admin/Manager.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán.</response>
        /// <response code="409">User đã là nhân viên quán này; username đã được sử dụng.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{cafeId:guid}/staff")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> AddStaff(Guid cafeId, [FromBody] AddStaffRequestDto dto)
        {
            var managerId = GetUserIdFromClaims();
            await _cafeService.AddStaffAsync(cafeId, managerId, dto);
            return this.NewResponse(200, "Staff member added successfully", null);
        }

        /// <summary>
        /// Nâng player (role Player) thành CafeStaff và gắn vào quán. [Role: Manager — phải là chủ quán (ManagerId của cafe).]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="dto">Email user cần promote; username/password tùy chọn.</param>
        /// <response code="200">Promote và gắn nhân viên vào quán thành công.</response>
        /// <response code="400">Email không hợp lệ; user đã là CafeStaff (gọi POST .../staff để gắn quán); không thể promote Admin/Manager; username quá ngắn.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán hoặc user theo email.</response>
        /// <response code="409">User đã là nhân viên quán này; username đã được sử dụng.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{cafeId:guid}/staff/promote")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> PromoteUserToStaff(Guid cafeId, [FromBody] PromoteStaffRequestDto dto)
        {
            var managerId = GetUserIdFromClaims();
            await _cafeService.PromoteUserToStaffAsync(cafeId, managerId, dto);
            return this.NewResponse(200, "User promoted to cafe staff successfully", null);
        }

        /// <summary>
        /// Lấy danh sách nhân viên của quán (phân trang). [Role: Manager — phải là chủ quán (ManagerId của cafe).]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Số bản ghi mỗi trang (mặc định 10).</param>
        /// <response code="200">Trả về danh sách nhân viên có phân trang.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{cafeId:guid}/staff")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> GetStaffList(
            Guid cafeId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var managerId = GetUserIdFromClaims();
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await _cafeService.GetStaffListAsync(cafeId, managerId, pagination);
            return this.NewResponse(200, "Staff list retrieved successfully", result);
        }

        /// <summary>
        /// Xóa nhân viên khỏi quán cafe (hạ role Player nếu không còn quán nào). [Role: Manager — phải là chủ quán (ManagerId của cafe).]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="staffId">Mã user của nhân viên cần xóa khỏi quán.</param>
        /// <response code="200">Xóa nhân viên khỏi quán thành công; tự động hạ role Player nếu không còn quán nào.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán hoặc nhân viên không thuộc quán này.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpDelete("{cafeId:guid}/staff/{staffId:guid}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> RemoveStaff(Guid cafeId, Guid staffId)
        {
            var managerId = GetUserIdFromClaims();
            await _cafeService.RemoveStaffAsync(cafeId, managerId, staffId);
            return this.NewResponse(200, "Staff member removed successfully", null);
        }
    }
}
