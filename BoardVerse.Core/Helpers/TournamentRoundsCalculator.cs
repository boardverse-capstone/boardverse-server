namespace BoardVerse.Core.Helpers;

/// <summary>
/// Tính toán số rounds tối ưu cho Swiss tournament dựa trên số người đăng ký.
///
/// Nguyên lý:
///   - Standard Swiss: 3 rounds cho 8-16 người, 4 rounds cho 16-32 người.
///   - Auto-shorten: giảm rounds nếu số người ít (ít data → ít rounds cần để phân hạng).
///   - Công thức: rounds = max(2, ceil(log2(N/4) + 1)), cap = configuredPreliminaryRounds.
///
/// Ví dụ:
///   - 4 người (1 bàn): 2 rounds (chỉ để xếp Top 4)
///   - 5-7 người (1-2 bàn): 2 rounds
///   - 8-11 người (2-3 bàn): 3 rounds (chuẩn Swiss)
///   - 12-15 người (3-4 bàn): 3 rounds
///   - 16-23 người (4-6 bàn): 4 rounds
///   - 24-31 người (6-8 bàn): 4 rounds
///   - 32 người (8 bàn): 4-5 rounds
/// </summary>
public static class TournamentRoundsCalculator
{
    /// <summary>
    /// Tính số rounds Swiss tối ưu cho tournament.
    ///
    /// </summary>
    /// <param name="participantCount">Số người đăng ký thực tế.</param>
    /// <param name="configuredRounds">Số rounds Manager đã config (default).</param>
    /// <param name="minRounds">Tối thiểu rounds (default 2).</param>
    /// <returns>Số rounds tối ưu, không vượt quá configuredRounds.</returns>
    public static int CalculateOptimalPreliminaryRounds(
        int participantCount,
        int configuredRounds,
        int minRounds = 2)
    {
        if (participantCount <= 0)
        {
            return minRounds;
        }

        // Công thức đơn giản: ceil(log2(N/4)) + 1
        // 4 người → log2(1) + 1 = 1 → clamp to 2
        // 8 người → log2(2) + 1 = 2
        // 16 người → log2(4) + 1 = 3
        // 32 người → log2(8) + 1 = 4
        var tables = Math.Ceiling(participantCount / 4.0);
        var calculated = (int)Math.Ceiling(Math.Log2(Math.Max(1, tables))) + 1;

        // Clamp
        var result = Math.Max(minRounds, calculated);
        result = Math.Min(configuredRounds, result);

        return result;
    }

    /// <summary>
    /// Quyết định có nên auto-shorten không dựa trên shortage severity.
    /// Trả về số rounds đề xuất, hoặc null nếu không nên shorten (giữ nguyên).
    /// </summary>
    /// <param name="participantCount">Số người thực tế.</param>
    /// <param name="configuredRounds">Số rounds Manager config.</param>
    /// <returns>Số rounds đề xuất, hoặc null = giữ nguyên configured.</returns>
    public static int? SuggestShortenedRounds(int participantCount, int configuredRounds)
    {
        var optimal = CalculateOptimalPreliminaryRounds(participantCount, configuredRounds);

        return optimal < configuredRounds ? optimal : null;
    }
}