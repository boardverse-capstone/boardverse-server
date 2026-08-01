using BoardVerse.Core.DTOs.Notification;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Controller cho FCM device tokens (mobile gap #9, #13).
/// Mobile gọi POST sau khi Firebase SDK trả token, DELETE khi logout/gỡ app.
/// </summary>
[ApiController]
[Route("api/notifications/device-tokens")]
[Authorize]
[Produces("application/json")]
[Tags("Notifications")]
public class DeviceTokenController : BaseApiController
{
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly IPushNotificationService _pushNotificationService;

    public DeviceTokenController(
        IDeviceTokenService deviceTokenService,
        IPushNotificationService pushNotificationService)
    {
        _deviceTokenService = deviceTokenService;
        _pushNotificationService = pushNotificationService;
    }

    /// <summary>
    /// Đăng ký hoặc cập nhật FCM device token cho user hiện tại.
    /// Idempotent: gọi nhiều lần với cùng token → update timestamp.
    /// </summary>
    /// <param name="request">FCM token + platform metadata.</param>
    /// <response code="200">Token đã đăng ký thành công.</response>
    /// <response code="400">Platform không hợp lệ hoặc token rỗng.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost]
    [ProducesResponseType(typeof(DeviceTokenResponseDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceTokenRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _deviceTokenService.RegisterAsync(userId, request);
        return NewResponse(200, "Đăng ký device token thành công.", result);
    }

    /// <summary>
    /// Xóa FCM device token (vd: khi user logout hoặc gỡ app).
    /// </summary>
    /// <param name="id">Device token id (GUID).</param>
    /// <response code="200">Xóa thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="404">Không tìm thấy token hoặc không thuộc user hiện tại.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = GetUserIdFromClaims();
        await _deviceTokenService.DeleteAsync(userId, id);
        return NewResponse(200, "Đã xóa device token.", new { id });
    }
}
