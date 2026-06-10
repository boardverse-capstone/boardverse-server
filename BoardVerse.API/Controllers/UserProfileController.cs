using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserProfileController : BaseApiController
    {
        private readonly IUserProfileService _profileService;

        public UserProfileController(IUserProfileService profileService)
        {
            _profileService = profileService;
        }

        /// <summary>
        /// Lấy hồ sơ công khai của người dùng đang đăng nhập. [Role: Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập.]
        /// </summary>
        /// <response code="200">Lấy hồ sơ thành công.</response>
        /// <response code="401">Thiếu token, token không hợp lệ hoặc thiếu claim người dùng.</response>
        /// <response code="403">Tài khoản bị chặn hoặc vô hiệu hóa.</response>
        /// <response code="404">Không tìm thấy người dùng trong token.</response>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Get()
        {
            var userId = GetUserIdFromClaims();
            var profile = await _profileService.GetPublicProfileAsync(userId);
            return this.NewResponse(200, "Profile retrieved successfully", profile);
        }

        /// <summary>
        /// Tạo hồ sơ người dùng mới cho tài khoản đang đăng nhập. [Role: Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập.]
        /// </summary>
        /// <param name="request">Thông tin hồ sơ cần tạo.</param>
        /// <response code="201">Tạo hồ sơ thành công.</response>
        /// <response code="400">Dữ liệu hồ sơ không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc claim người dùng.</response>
        /// <response code="403">Tài khoản bị chặn hoặc vô hiệu hóa.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        /// <response code="409">Người dùng đã có hồ sơ hoạt động.</response>
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] ProfileCreateDto request)
        {
            var userId = GetUserIdFromClaims();
            var profile = await _profileService.CreateProfileAsync(userId, request);
            return this.NewResponse(201, "Profile created", profile);
        }

        /// <summary>
        /// Cập nhật thông tin hồ sơ của người dùng đang đăng nhập. [Role: Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập.]
        /// </summary>
        /// <param name="request">Thông tin hồ sơ cần cập nhật.</param>
        /// <response code="200">Cập nhật hồ sơ thành công.</response>
        /// <response code="400">Dữ liệu hồ sơ không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc claim người dùng.</response>
        /// <response code="403">Tài khoản bị chặn hoặc vô hiệu hóa.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        [HttpPut]
        [Authorize]
        public async Task<IActionResult> Update([FromBody] ProfileUpdateDto request)
        {
            var userId = GetUserIdFromClaims();
            var profile = await _profileService.UpdateProfileAsync(userId, request);
            return this.NewResponse(200, "Profile updated successfully", profile);
        }

        /// <summary>
        /// Cập nhật tiến trình và điểm số của hồ sơ người dùng. [Role: Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập.]
        /// </summary>
        /// <param name="request">Thông tin tiến trình cần cập nhật.</param>
        /// <response code="200">Cập nhật tiến trình thành công.</response>
        /// <response code="400">Dữ liệu tiến trình không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc claim người dùng.</response>
        /// <response code="403">Tài khoản bị chặn hoặc vô hiệu hóa.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        [HttpPost("progress")]
        [Authorize]
        public async Task<IActionResult> UpdateProgress([FromBody] ProfileProgressUpdateDto request)
        {
            var userId = GetUserIdFromClaims();
            var profile = await _profileService.UpdateProgressAsync(userId, request);
            return this.NewResponse(200, "Profile progress updated successfully", profile);
        }

        /// <summary>
        /// Cập nhật ảnh đại diện của người dùng đang đăng nhập. [Role: Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập.]
        /// </summary>
        /// <param name="request">Đường dẫn ảnh đại diện mới.</param>
        /// <response code="200">Cập nhật avatar thành công.</response>
        /// <response code="400">URL avatar không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc claim người dùng.</response>
        /// <response code="403">Tài khoản bị chặn hoặc vô hiệu hóa.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        [HttpPut("me/avatar")]
        [Authorize]
        public async Task<IActionResult> UpdateAvatar([FromBody] UpdateAvatarRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var profile = await _profileService.UpdateAvatarAsync(userId, request);
            return this.NewResponse(200, "Avatar updated successfully", profile);
        }

        /// <summary>
        /// Xem trạng thái điểm karma hiện tại của người dùng đang đăng nhập. [Role: Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập.]
        /// </summary>
        /// <response code="200">Trả về trạng thái karma hiện tại.</response>
        /// <response code="401">Thiếu token hoặc claim người dùng.</response>
        /// <response code="403">Tài khoản bị chặn hoặc vô hiệu hóa.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        [HttpGet("me/karma-history")]
        [Authorize]
        public async Task<IActionResult> GetKarmaHistory()
        {
            var userId = GetUserIdFromClaims();
            var karmaState = await _profileService.GetKarmaStateAsync(userId);
            return this.NewResponse(200, "Karma state retrieved successfully", karmaState);
        }

        /// <summary>
        /// Vô hiệu hóa hồ sơ của người dùng đang đăng nhập. [Role: Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập.]
        /// </summary>
        /// <response code="200">Xóa hoặc vô hiệu hóa hồ sơ thành công (idempotent nếu chưa có hồ sơ).</response>
        /// <response code="401">Thiếu token hoặc claim người dùng.</response>
        /// <response code="403">Tài khoản bị chặn hoặc vô hiệu hóa.</response>
        [HttpDelete]
        [Authorize]
        public async Task<IActionResult> Delete()
        {
            var userId = GetUserIdFromClaims();
            await _profileService.DeleteProfileAsync(userId);
            return this.NewResponse(200, "Profile deleted successfully", null);
        }
    }
}
