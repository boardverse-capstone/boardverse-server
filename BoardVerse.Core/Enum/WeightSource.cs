namespace BoardVerse.Core.Enum
{
    /// <summary>
    /// Nguồn gốc của <c>GameTemplate.Weight</c> (BGG Complexity Weight 1.0 → 5.0).
    /// Dùng cho audit: biết được Weight từ BGG import, do admin set thủ công, hay do default rule.
    /// </summary>
    public enum WeightSource
    {
        /// <summary>
        /// Import từ BoardGameGeek (BGG). Đây là default cho game tạo qua BGG import flow.
        /// </summary>
        BGG = 0,

        /// <summary>
        /// Admin set thủ công qua <c>PUT /api/v1/admin/master-games/{id}</c>.
        /// </summary>
        Manual = 1,

        /// <summary>
        /// Set tự động bởi default rule (category-based heuristic). Hiện KHÔNG sử dụng (Strategy 4 đã bị bỏ),
        /// reserved cho phase sau nếu muốn bật lại.
        /// </summary>
        Default = 2
    }
}
