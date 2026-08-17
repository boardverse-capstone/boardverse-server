using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.SystemConfig;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace BoardVerse.API.Controllers
{
    /// <summary>
    /// Admin endpoint để xem 1 system config theo key (read-only).
    /// Hữu ích cho Admin/Dev/QA verify demo mode, bypass flag, elo_k_factor, v.v.
    /// KHÔNG expose write operations — đã có <c>AdminConfigurationController</c> cho PUT/DELETE.
    /// KHÔNG expose sensitive config (passwords, API keys, webhooks secret) ở production.
    /// </summary>
    [ApiController]
    [Route("api/v1/system-configs")]
    [Authorize(Roles = "Admin")]
    public class SystemConfigurationPublicController : ControllerBase
    {
        private readonly ISystemConfigurationProvider _provider;
        private readonly ISystemConfigurationRepository _repository;

        public SystemConfigurationPublicController(
            ISystemConfigurationProvider provider,
            ISystemConfigurationRepository repository)
        {
            _provider = provider;
            _repository = repository;
        }

        /// <summary>
        /// Lấy 1 system config theo key. Trả về raw string value + parsed value (bool/int/double/string).
        /// Admin endpoint — yêu cầu role Admin. Dùng để Admin/Dev/QA verify nhanh các flag runtime. [Role: Admin]
        /// </summary>
        /// <param name="key">Config key, ví dụ: <c>demo_loosen_lobby_constraints</c>, <c>elo_k_factor</c>, <c>bypass_time_window_validations</c>.</param>
        /// <response code="200">Trả về raw value + parsed value + inferred type.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Đã đăng nhập nhưng không có role Admin.</response>
        /// <response code="404">Key không tồn tại trong DB.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{key}")]
        public async Task<IActionResult> GetByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return BadRequest(new { error = "Config key must not be empty." });
            }

            var trimmedKey = key.Trim();
            var entry = await _repository.GetByKeyAsync(trimmedKey);
            if (entry == null)
            {
                return NotFound(new { error = $"Config key '{trimmedKey}' không tồn tại.", key = trimmedKey });
            }

            var response = new SystemConfigPublicResponseDto
            {
                ConfigKey = entry.ConfigKey,
                ConfigValue = entry.ConfigValue,
                Description = entry.Description,
                UpdatedAt = entry.UpdatedAt,
                InferredType = InferType(entry.ConfigValue),
                ParsedValue = ParseValue(entry.ConfigValue)
            };

            return Ok(response);
        }

        private static string InferType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "string";
            }

            var trimmed = raw.Trim();
            if (bool.TryParse(trimmed, out _)
                || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return "bool";
            }

            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return "int";
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return "double";
            }

            return "string";
        }

        private static object? ParseValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }

            var trimmed = raw.Trim();
            if (bool.TryParse(trimmed, out var b))
            {
                return b;
            }

            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return l;
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }

            return raw;
        }
    }
}
