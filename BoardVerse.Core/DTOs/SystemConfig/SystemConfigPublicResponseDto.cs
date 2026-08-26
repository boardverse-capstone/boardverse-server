using BoardVerse.Core.Data;

namespace BoardVerse.Core.DTOs.SystemConfig
{
    /// <summary>
    /// Public/dev-facing view của một system config. Trả về raw value + parsed value (best-effort).
    /// Dùng cho endpoint GET /api/v1/system-configs/{key} cho phép dev check nhanh không cần token Admin.
    /// </summary>
    public class SystemConfigPublicResponseDto
    {
        public string ConfigKey { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// "bool" | "int" | "double" | "string". Detect bằng thử parse lần lượt.
        /// </summary>
        public string InferredType { get; set; } = "string";

        /// <summary>
        /// Parsed value theo InferredType. Có thể là bool / double / string.
        /// </summary>
        public object? ParsedValue { get; set; }
    }
}
