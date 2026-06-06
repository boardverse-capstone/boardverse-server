using BoardVerse.Core.DTOs.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardVerse.Services.IServices;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class UserManagementController : BaseApiController
    {
        private readonly IUserManagementService _userManagementService;

        public UserManagementController(IUserManagementService userManagementService)
        {
            _userManagementService = userManagementService;
        }

        /// <summary>
        /// Lấy danh sách người dùng theo điều kiện tìm kiếm, lọc và phân trang.
        /// </summary>
        /// <param name="query">Thông tin truy vấn bao gồm từ khóa, vai trò, trạng thái và phân trang.</param>
        /// <response code="200">Trả về danh sách người dùng phù hợp.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Người dùng không có quyền quản trị.</response>
        [HttpGet]
        [HttpGet("users")]
        public async Task<IActionResult> GetAll([FromQuery] AdminUserQueryDto query)
        {
            var response = await _userManagementService.GetAllAsync(query);

            return this.NewResponse(200, "Users retrieved successfully", response);
        }

        /// <summary>
        /// Lấy thông tin chi tiết của một người dùng theo mã định danh.
        /// </summary>
        /// <param name="id">Mã định danh của người dùng.</param>
        /// <response code="200">Trả về thông tin người dùng.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var user = await _userManagementService.GetAsync(id);
            return this.NewResponse(200, "User retrieved successfully", user);
        }

        /// <summary>
        /// Tạo mới tài khoản người dùng cho quản trị viên.
        /// </summary>
        /// <param name="request">Thông tin tài khoản cần tạo.</param>
        /// <response code="201">Tạo người dùng thành công.</response>
        /// <response code="400">Dữ liệu đầu vào không hợp lệ hoặc email/username đã tồn tại.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Người dùng không có quyền quản trị.</response>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AdminCreateUserDto request)
        {
            var user = await _userManagementService.CreateAsync(request);
            return this.NewResponse(201, "User created", user);
        }

        /// <summary>
        /// Cập nhật thông tin người dùng theo mã định danh.
        /// </summary>
        /// <param name="id">Mã định danh của người dùng.</param>
        /// <param name="request">Thông tin cập nhật người dùng.</param>
        /// <response code="200">Cập nhật người dùng thành công.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Người dùng không có quyền quản trị.</response>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] AdminUpdateUserDto request)
        {
            var user = await _userManagementService.UpdateAsync(id, request);
            return this.NewResponse(200, "User updated successfully", user);
        }

        /// <summary>
        /// Vô hiệu hóa tài khoản người dùng theo mã định danh.
        /// </summary>
        /// <param name="id">Mã định danh của người dùng.</param>
        /// <response code="200">Vô hiệu hóa người dùng thành công.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Người dùng không có quyền quản trị.</response>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Disable(Guid id)
        {
            await _userManagementService.DisableAsync(id);
            return this.NewResponse(200, "User disabled successfully", null);
        }

        /// <summary>
        /// Chặn một người dùng theo mã định danh với lý do cụ thể.
        /// </summary>
        /// <param name="id">Mã định danh của người dùng.</param>
        /// <param name="request">Lý do chặn người dùng.</param>
        /// <response code="200">Chặn người dùng thành công.</response>
        /// <response code="400">Lý do chặn không hợp lệ.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        [HttpPost("users/{id:guid}/block")]
        public async Task<IActionResult> Block(Guid id, [FromBody] AdminBlockUserDto request)
        {
            var user = await _userManagementService.BlockAsync(id, request);
            return this.NewResponse(200, "User blocked successfully", user);
        }

        /// <summary>
        /// Gỡ chặn người dùng theo mã định danh.
        /// </summary>
        /// <param name="id">Mã định danh của người dùng.</param>
        /// <response code="200">Gỡ chặn người dùng thành công.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        [HttpPost("users/{id:guid}/unblock")]
        public async Task<IActionResult> Unblock(Guid id)
        {
            var user = await _userManagementService.UnblockAsync(id);
            return this.NewResponse(200, "User unblocked successfully", user);
        }

        /// <summary>
        /// Cập nhật vai trò của người dùng theo mã định danh.
        /// </summary>
        /// <param name="id">Mã định danh của người dùng.</param>
        /// <param name="request">Vai trò mới của người dùng.</param>
        /// <response code="200">Cập nhật vai trò thành công.</response>
        /// <response code="400">Vai trò không hợp lệ.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        [HttpPut("users/{id:guid}/role")]
        public async Task<IActionResult> UpdateRole(Guid id, [FromBody] AdminUpdateUserRoleDto request)
        {
            var user = await _userManagementService.UpdateRoleAsync(id, request);
            return this.NewResponse(200, "User role updated successfully", user);
        }
    }
}
