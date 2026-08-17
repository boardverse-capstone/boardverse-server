using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/admin/configs")]
    [Authorize(Roles = "Admin")]
    public class AdminConfigurationController : BaseApiController
    {
        private readonly IAdminSystemConfigurationService _configurationService;

        public AdminConfigurationController(IAdminSystemConfigurationService configurationService)
        {
            _configurationService = configurationService;
        }

        /// <summary>
        /// Lấy toàn bộ cấu hình hệ thống dạng key-value JSON. [Role: Admin]
        /// </summary>
        /// <response code="200">Object chứa các cặp config_key → config_value.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet]
        public async Task<IActionResult> GetConfigs()
        {
            var entries = await _configurationService.GetAllConfigsAsync();
            var map = entries.ToDictionary(e => e.ConfigKey, e => e.ConfigValue);
            return NewResponse(200, ApiSuccessMessages.AdminConfig.Retrieved, map);
        }

        /// <summary>
        /// Cập nhật đồng loạt cấu hình hệ thống và invalidate cache. [Role: Admin]
        /// </summary>
        /// <param name="request">Mảng configs (configKey, configValue).</param>
        /// <response code="200">Cấu hình đã cập nhật; trả về danh sách mới.</response>
        /// <response code="400">Dữ liệu request không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPut]
        public async Task<IActionResult> BulkUpdateConfigs([FromBody] SystemConfigBulkUpdateRequestDto request)
        {
            var updated = await _configurationService.BulkUpdateConfigsAsync(request);
            var map = updated.ToDictionary(e => e.ConfigKey, e => e.ConfigValue);
            return NewResponse(200, ApiSuccessMessages.AdminConfig.Updated, map);
        }

        /// <summary>
        /// Bật bypass time-window toàn cục. Áp dụng trong vòng 10 giây cho mọi instance.
        /// Dev/QA dùng để test các flow check-in / lobby / cancel / no-show mà không bị chặn bởi ràng buộc thời gian. [Role: Admin]
        /// </summary>
        /// <response code="200">Bypass đã bật.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("bypass-time-window")]
        public async Task<IActionResult> EnableBypassTimeWindow()
        {
            var entry = await _configurationService.SetConfigValueAsync(
                SystemConfigKeys.BypassTimeWindowValidations, "true");
            return NewResponse(200, "Bypass time-window đã bật. Áp dụng trong vòng 10 giây.",
                new { bypassEnabled = true, configKey = entry.ConfigKey, appliedWithinSeconds = 10 });
        }

        /// <summary>
        /// Tắt bypass time-window toàn cục. Áp dụng trong vòng 10 giây cho mọi instance. [Role: Admin]
        /// </summary>
        /// <response code="200">Bypass đã tắt.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpDelete("bypass-time-window")]
        public async Task<IActionResult> DisableBypassTimeWindow()
        {
            var entry = await _configurationService.SetConfigValueAsync(
                SystemConfigKeys.BypassTimeWindowValidations, "false");
            return NewResponse(200, "Bypass time-window đã tắt. Áp dụng trong vòng 10 giây.",
                new { bypassEnabled = false, configKey = entry.ConfigKey, appliedWithinSeconds = 10 });
        }

        /// <summary>
        /// Xem trạng thái bypass time-window hiện tại. [Role: Admin]
        /// </summary>
        /// <response code="200">Trạng thái bypass hiện tại.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("bypass-time-window")]
        public async Task<IActionResult> GetBypassTimeWindowStatus()
        {
            var enabled = await _configurationService.IsBypassTimeWindowEnabledAsync();
            return NewResponse(200, "OK",
                new { bypassEnabled = enabled, configKey = SystemConfigKeys.BypassTimeWindowValidations });
        }

        /// <summary>
        /// Bật demo mode toàn cục: nới lỏng BR-USER-LIMIT-01/04/05, BR-LOBBY-01a/b (buffer 60/120 phút), BR-NEW-05 (max 5 tạo/hủy / playDate), BR-CHECKIN-01 (early grace 15 phút).
        /// Cache invalidate ngay, áp dụng cho mọi instance trong vòng 10 giây.
        /// CHỈ bật trên Neon testing branch (`br-sparkling-salad-aota3n5d`), KHÔNG bật production (`br-hidden-shadow-aoqtn6su`). [Role: Admin]
        /// </summary>
        /// <response code="200">Demo mode đã bật.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("demo-loosen-lobby-constraints")]
        public async Task<IActionResult> EnableDemoLoosenLobbyConstraints()
        {
            var entry = await _configurationService.SetConfigValueAsync(
                SystemConfigKeys.DemoLoosenLobbyConstraints, "true");
            return NewResponse(200, "Demo mode đã bật. BR-USER-LIMIT-01/04/05, BR-LOBBY-01a/b, BR-NEW-05, BR-CHECKIN-01 sẽ bị bypass. Áp dụng trong vòng 10 giây.",
                new
                {
                    demoEnabled = true,
                    configKey = entry.ConfigKey,
                    appliedWithinSeconds = 10,
                    affectedRules = new[]
                    {
                        "BR-USER-LIMIT-01 (max 2 lobby active)",
                        "BR-USER-LIMIT-04 (member cannot host)",
                        "BR-USER-LIMIT-05 (host cannot join)",
                        "BR-LOBBY-01a (buffer >= 60 phút)",
                        "BR-LOBBY-01b (buffer < 60 phút reject)",
                        "BR-LOBBY-01c (buffer 60-120 phút warning)",
                        "BR-NEW-05 (max 5 tạo/hủy / playDate)",
                        "BR-CHECKIN-01 (early grace 15 phút)"
                    }
                });
        }

        /// <summary>
        /// Tắt demo mode toàn cục. Trở về hành vi production (áp dụng đầy đủ BR-USER-LIMIT / BR-LOBBY-01 / BR-NEW-05 / BR-CHECKIN-01). [Role: Admin]
        /// </summary>
        /// <response code="200">Demo mode đã tắt.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpDelete("demo-loosen-lobby-constraints")]
        public async Task<IActionResult> DisableDemoLoosenLobbyConstraints()
        {
            var entry = await _configurationService.SetConfigValueAsync(
                SystemConfigKeys.DemoLoosenLobbyConstraints, "false");
            return NewResponse(200, "Demo mode đã tắt. BR-USER-LIMIT / BR-LOBBY-01 / BR-NEW-05 / BR-CHECKIN-01 được áp dụng lại. Áp dụng trong vòng 10 giây.",
                new { demoEnabled = false, configKey = entry.ConfigKey, appliedWithinSeconds = 10 });
        }

        /// <summary>
        /// Xem trạng thái demo mode hiện tại. [Role: Admin]
        /// </summary>
        /// <response code="200">Trạng thái demo mode hiện tại.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("demo-loosen-lobby-constraints")]
        public async Task<IActionResult> GetDemoLoosenLobbyConstraintsStatus()
        {
            var enabled = await _configurationService.IsDemoLoosenLobbyConstraintsEnabledAsync();
            return NewResponse(200, "OK",
                new
                {
                    demoEnabled = enabled,
                    configKey = SystemConfigKeys.DemoLoosenLobbyConstraints
                });
        }

        /// <summary>
        /// Invalidate cache cấu hình hệ thống ngay lập tức — áp dụng thay đổi cho mọi instance không cần đợi TTL 10s. [Role: Admin]
        /// </summary>
        /// <response code="200">Cache đã được invalidate.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("invalidate-cache")]
        public async Task<IActionResult> InvalidateCache()
        {
            await _configurationService.InvalidateCacheAsync();
            return NewResponse(200, "Cache cấu hình hệ thống đã được invalidate.", new { });
        }
    }
}
