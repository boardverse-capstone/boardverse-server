namespace BoardVerse.Core.Settings
{
    /// <summary>
    /// Cấu hình cho Geocoding service (Nominatim OpenStreetMap).
    /// Dùng để reverse-geocode tọa độ GPS thành tên Quận/Thành phố cho PlayerLocationDto.
    /// </summary>
    public class NominatimSettings
    {
        public const string SectionName = "Geocoding";

        /// <summary>Base URL của Nominatim API. Mặc định public server (giới hạn rate 1 req/s, cần User-Agent).</summary>
        public string ApiBaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

        /// <summary>Endpoint reverse geocoding path.</summary>
        public string ReversePath { get; set; } = "/reverse";

        /// <summary>
        /// HTTP User-Agent bắt buộc theo Nominatim usage policy
        /// (vd: "BoardVerse/1.0 (contact@boardverse.app)").
        /// </summary>
        public string UserAgent { get; set; } = "BoardVerse/1.0 (contact@boardverse.app)";

        /// <summary>Accept-Language cho response (vi = Tiếng Việt, en = English).</summary>
        public string AcceptLanguage { get; set; } = "vi";

        /// <summary>Timeout mỗi request (giây).</summary>
        public int RequestTimeoutSeconds { get; set; } = 5;

        /// <summary>Số lần retry khi gặp lỗi transient (5xx, timeout).</summary>
        public int MaxRetryAttempts { get; set; } = 2;

        /// <summary>Delay giữa các lần retry (ms).</summary>
        public int RetryDelayMilliseconds { get; set; } = 500;

        /// <summary>Bật cache in-memory cho kết quả reverse-geocode.</summary>
        public bool EnableCache { get; set; } = true;

        /// <summary>Thời gian sống của cache entry (giờ).</summary>
        public int CacheTtlHours { get; set; } = 24 * 30; // 30 ngày

        /// <summary>Độ chính xác làm tròn tọa độ (5 chữ số thập phân ≈ 1.1m) trước khi cache để tránh spam.</summary>
        public double CoordinateQuantization { get; set; } = 0.00001;
    }
}