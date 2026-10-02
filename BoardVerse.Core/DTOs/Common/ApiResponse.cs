using System;

namespace BoardVerse.Core.DTOs.Common
{
    public class ApiResponse
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
        public DateTime Timestamp { get; set; }
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Machine-readable error code (PascalCase identifier vd <c>InsufficientSeatsForMerge</c>,
        /// <c>RATE_LIMIT_EXCEEDED</c>). Optional — chỉ có khi request fail. Client dùng để switch
        /// trên code thay vì parse message.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? ErrorCode { get; set; }
    }
}
