namespace BoardVerse.Core.Settings
{
    public class BggSettings
    {
        public const string SectionName = "Bgg";

        public string ApiBaseUrl { get; set; } = "https://boardgamegeek.com/xmlapi2";
        public string ApiToken { get; set; } = string.Empty;
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int MaxRetryAttempts { get; set; } = 5;
        public int RetryDelayMilliseconds { get; set; } = 2000;

        /// <summary>
        /// Delay (ms) dùng khi BGG trả 200 OK nhưng XML thiếu <c>&lt;statistics&gt;</c>
        /// (do BGG xử lý async, chưa gắn stats vào XML).
        /// Lớn hơn <see cref="RetryDelayMilliseconds"/> vì BGG cần thời gian tính toán averageweight.
        /// Mặc định 30 000ms = 30s.
        /// </summary>
        public int StatsRetryDelayMilliseconds { get; set; } = 30000;

        /// <summary>
        /// Số vòng tối đa background job sẽ retry fetch BGG cho game đang missing Weight.
        /// Sau khi vượt quá, game sẽ bị loại khỏi retry queue và chỉ admin xử lý thủ công.
        /// Mặc định 5 vòng × 6 giờ = 30 giờ (1.25 ngày).
        /// </summary>
        public int MissingWeightMaxRetryRounds { get; set; } = 5;

        /// <summary>
        /// Khoảng cách giữa các lần chạy của background job retry Weight (giờ).
        /// Mặc định 6 giờ. Job chỉ retry game có <c>BggRetryCount &lt; MissingWeightMaxRetryRounds</c>.
        /// </summary>
        public int MissingWeightRetryIntervalHours { get; set; } = 6;

        /// <summary>
        /// Batch size của background job retry Weight. Mỗi tick xử lý tối đa N game
        /// để tránh spam BGG API.
        /// </summary>
        public int MissingWeightRetryBatchSize { get; set; } = 20;
    }
}
