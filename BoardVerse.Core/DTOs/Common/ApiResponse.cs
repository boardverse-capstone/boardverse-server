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
    }
}
