namespace BoardVerse.Core.DTOs.Discovery
{
    /// <summary>
    /// Thống kê lịch sử chơi game của user trong khoảng thời gian gần đây.
    /// Dùng để tính penalty cho games đã chơi nhiều lần.
    /// </summary>
    public class UserPlayHistoryDto
    {
        /// <summary>
        /// ID của game template.
        /// </summary>
        public Guid GameTemplateId { get; set; }

        /// <summary>
        /// Tên game.
        /// </summary>
        public string GameName { get; set; } = string.Empty;

        /// <summary>
        /// Số lần chơi game này trong khoảng thời gian query.
        /// </summary>
        public int PlayCount { get; set; }

        /// <summary>
        /// Lần chơi gần nhất (EndedAt của session).
        /// </summary>
        public DateTime? LastPlayedAt { get; set; }

        /// <summary>
        /// Tổng số phút đã chơi game này.
        /// </summary>
        public int TotalMinutesPlayed { get; set; }
    }
}
