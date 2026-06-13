using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Core.DTOs.Auth.Responses;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : BaseApiController
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        /// <summary>
        /// Đăng ký tài khoản người dùng mới vào hệ thống BoardVerse. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Thông tin đăng ký của người dùng.</param>
        /// <response code="200">Đăng ký thành công.</response>
        /// <response code="400">Dữ liệu đầu vào không hợp lệ (email, username, password).</response>
        /// <response code="409">Email hoặc username đã tồn tại.</response>
        /// <response code="500">Lỗi hệ thống khi xử lý đăng ký.</response>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            var response = await _authService.RegisterAsync(request);
            return this.NewResponse(200, "Registration successful", response);
        }

        /// <summary>
        /// Xác thực tài khoản và tạo token đăng nhập cho người dùng. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Thông tin đăng nhập của người dùng.</param>
        /// <response code="200">Đăng nhập thành công, trả về access token và refresh token.</response>
        /// <response code="401">Thông tin đăng nhập không hợp lệ.</response>
        /// <response code="403">Tài khoản bị chặn.</response>
        /// <response code="429">Vượt quá số lần đăng nhập cho phép.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            var response = await _authService.LoginAsync(request);
            return this.NewResponse(200, "Login successful", response);
        }

        /// <summary>
        /// Đăng nhập bằng tài khoản Google và phát hành token truy cập. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Thông tin Google ID token.</param>
        /// <response code="200">Đăng nhập Google thành công.</response>
        /// <response code="401">Google token không hợp lệ, thiếu email hoặc xác thực thất bại.</response>
        /// <response code="403">Tài khoản bị chặn.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleAuthRequestDto request)
        {
            var response = await _authService.GoogleLoginAsync(request);
            return this.NewResponse(200, "Google login successful", response);
        }

        /// <summary>
        /// Gia hạn access token bằng refresh token hợp lệ. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Thông tin refresh token cần gia hạn.</param>
        /// <response code="200">Gia hạn token thành công.</response>
        /// <response code="401">Refresh token không hợp lệ hoặc đã hết hạn.</response>
        /// <response code="403">Tài khoản bị chặn.</response>
        /// <response code="404">Không tìm thấy người dùng tương ứng với refresh token.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequestDto request)
        {
            var response = await _authService.ExchangeRefreshTokenAsync(request);
            return this.NewResponse(200, "Token refreshed successfully", response);
        }

        /// <summary>
        /// Thu hồi refresh token của người dùng để kết thúc phiên đăng nhập. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Thông tin refresh token cần thu hồi.</param>
        /// <response code="200">Đăng xuất thành công.</response>
        /// <response code="400">Dữ liệu yêu cầu không hợp lệ.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestDto request)
        {
            await _authService.RevokeRefreshTokenAsync(request.RefreshToken);
            return this.NewResponse(200, "Logged out", null);
        }

        /// <summary>
        /// Gửi mã xác minh email đến địa chỉ đã đăng ký của người dùng. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Địa chỉ email cần nhận mã xác minh.</param>
        /// <response code="200">Gửi email xác minh thành công.</response>
        /// <response code="400">Email không hợp lệ.</response>
        /// <response code="403">Tài khoản bị chặn.</response>
        /// <response code="404">Không tìm thấy người dùng theo email.</response>
        /// <response code="500">Không gửi được email (lỗi SMTP).</response>
        [HttpPost("send-email-verification")]
        public async Task<IActionResult> SendEmailVerification([FromBody] SendEmailVerificationRequestDto request)
        {
            var message = await _authService.SendEmailVerificationAsync(request);
            return this.NewResponse(200, message, null);
        }

        /// <summary>
        /// Xác thực email của người dùng bằng mã xác minh đã nhận. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Mã xác minh email.</param>
        /// <response code="200">Xác minh email thành công.</response>
        /// <response code="401">Mã xác minh không hợp lệ hoặc đã hết hạn.</response>
        /// <response code="403">Tài khoản bị chặn.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequestDto request)
        {
            await _authService.VerifyEmailAsync(request);
            return this.NewResponse(200, "Email verified", null);
        }

        /// <summary>
        /// Tạo yêu cầu đặt lại mật khẩu và gửi mã reset đến email người dùng. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Địa chỉ email cần yêu cầu đặt lại mật khẩu.</param>
        /// <response code="200">Gửi yêu cầu đặt lại mật khẩu thành công.</response>
        /// <response code="400">Email không hợp lệ.</response>
        /// <response code="403">Email chưa được xác minh hoặc tài khoản bị chặn.</response>
        /// <response code="404">Không tìm thấy người dùng theo email.</response>
        /// <response code="500">Không gửi được email (lỗi SMTP).</response>
        [HttpPost("request-password-reset")]
        public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetDto request)
        {
            var message = await _authService.RequestPasswordResetAsync(request);
            return this.NewResponse(200, message, null);
        }

        /// <summary>
        /// Đặt lại mật khẩu bằng mã xác minh đặt lại mật khẩu. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Thông tin mã reset và mật khẩu mới.</param>
        /// <response code="200">Đặt lại mật khẩu thành công.</response>
        /// <response code="400">Mật khẩu mới không hợp lệ.</response>
        /// <response code="401">Mã reset không hợp lệ hoặc đã hết hạn.</response>
        /// <response code="403">Tài khoản bị chặn.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request)
        {
            await _authService.ResetPasswordAsync(request);
            return this.NewResponse(200, "Password has been reset", null);
        }

        /// <summary>
        /// Thay đổi mật khẩu của tài khoản đang đăng nhập. [Role: Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập.]
        /// </summary>
        /// <param name="request">Mật khẩu hiện tại và mật khẩu mới.</param>
        /// <response code="200">Đổi mật khẩu thành công.</response>
        /// <response code="400">Mật khẩu mới trùng mật khẩu cũ, hoặc tài khoản không có mật khẩu nội bộ (Google-only).</response>
        /// <response code="401">Token không hợp lệ hoặc mật khẩu hiện tại không đúng.</response>
        /// <response code="403">Tài khoản bị chặn.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request)
        {
            var userIdValue = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(userIdValue, out var userId))
            {
                throw new UnauthorizedException(ApiErrorMessages.Controller.ChangePasswordInvalidUserId);
            }

            await _authService.ChangePasswordAsync(userId, request);
            return this.NewResponse(200, "Password has been changed", null);
        }

        /// <summary>
        /// Liên kết tài khoản Google với tài khoản BoardVerse hiện có. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="request">Google ID token dùng để liên kết tài khoản.</param>
        /// <response code="200">Liên kết tài khoản Google thành công.</response>
        /// <response code="401">Google token không hợp lệ hoặc thiếu email.</response>
        /// <response code="403">Tài khoản bị chặn.</response>
        /// <response code="404">Không tìm thấy tài khoản nội bộ để liên kết.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("link-google")]
        public async Task<IActionResult> LinkGoogle([FromBody] LinkGoogleRequestDto request)
        {
            var response = await _authService.LinkGoogleAccountAsync(request);
            return this.NewResponse(200, "Google account linked successfully", response);
        }
    }
}
