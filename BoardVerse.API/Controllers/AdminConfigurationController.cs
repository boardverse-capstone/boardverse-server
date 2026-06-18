using BoardVerse.Core.DTOs.Admin;
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
            return NewResponse(200, "System configurations retrieved successfully", map);
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
            return NewResponse(200, "System configurations updated successfully", map);
        }
    }
}
