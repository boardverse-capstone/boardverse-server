namespace BoardVerse.Core.Helpers;

/// <summary>
/// Tính cách chia tối ưu N người vào các bàn Splendor (2-4 người/bàn).
///
/// Design goals (priority order):
///   1. KHÔNG có bàn 1 người (vì không ghép được, lãng phí thời gian).
///   2. CHIA ĐỀU nhất có thể (ít variance giữa các bàn).
///   3. ƯU TIÊN nhiều bàn đầy (4 người) hơn ít bàn full hơn.
///
/// Splendor rules:
///   - Tối đa 4 người/bàn.
///   - Tối thiểu 2 người/bàn (1 người không đấu được).
///   - Nên là bội số của 2 hoặc 4 để pair dễ.
///
/// Ví dụ:
///   5 người → [3, 2] (1 bàn 3 + 1 bàn 2, không bàn 1)
///   6 người → [3, 3] (2 bàn đều)
///   7 người → [4, 3] (1 bàn full + 1 bàn partial)
///   9 người → [3, 3, 3] (3 bàn đều)
///  10 người → [4, 3, 3] hoặc [4, 4, 2] (chọn best)
///  13 người → [4, 3, 3, 3]
/// </summary>
public static class TableSizeOptimizer
{
    /// <summary>
    /// Kích thước bàn Splendor hợp lệ.
    /// </summary>
    public const int MinTableSize = 2;
    public const int MaxTableSize = 4;

    /// <summary>
    /// Tính cách chia tối ưu N người thành các bàn.
    /// </summary>
    /// <param name="playerCount">Số người cần chia.</param>
    /// <returns>List các size, ví dụ [3, 3] cho 6 người. Null nếu không chia được.</param>
    public static List<int>? CalculateOptimalTableSizes(int playerCount)
    {
        if (playerCount < 2)
        {
            // 0 hoặc 1 người: caller xử lý riêng (bye hoặc empty round).
            return null;
        }

        if (playerCount <= MaxTableSize)
        {
            return new List<int> { playerCount };
        }

        // Try multiple strategies, chọn cái có quality cao nhất.
        var candidates = new List<List<int>>
        {
            // Strategy 1: All equal (chia đều nhất)
            BuildEqualTables(playerCount),

            // Strategy 2: Tailed (bàn cuối nhỏ hơn)
            BuildTailedTables(playerCount),

            // Strategy 3: Front-heavy (bàn đầu full, bàn sau partial)
            BuildFrontHeavyTables(playerCount),
        };

        // Pick candidate với quality score cao nhất.
        return candidates
            .Select(c => new { Tables = c, Score = EvaluateQuality(c, playerCount) })
            .OrderByDescending(x => x.Score)
            .First()
            .Tables;
    }

    /// <summary>
    /// Strategy 1: All equal. Tất cả bàn cùng size.
    /// Ví dụ: 9 → [3,3,3]; 12 → [3,3,3,3]; 11 → [3,3,3,2] (gần đều nhất).
    /// </summary>
    private static List<int> BuildEqualTables(int playerCount)
    {
        var tables = new List<int>();
        var tableCount = (int)Math.Ceiling((double)playerCount / MaxTableSize);
        var baseSize = playerCount / tableCount;
        var remainder = playerCount % tableCount;

        for (int i = 0; i < tableCount; i++)
        {
            tables.Add(baseSize + (i < remainder ? 1 : 0));
        }

        return tables;
    }

    /// <summary>
    /// Strategy 2: Tailed. Bàn cuối nhỏ hơn các bàn đầu.
    /// Ví dụ: 9 → [4,4,1] (sai vì có bàn 1) → [4,3,2]; 10 → [4,4,2].
    /// </summary>
    private static List<int> BuildTailedTables(int playerCount)
    {
        var tables = new List<int>();
        var remaining = playerCount;

        while (remaining > 0)
        {
            var size = Math.Min(MaxTableSize, remaining);
            tables.Add(size);
            remaining -= size;
        }

        // Fix bàn cuối nếu = 1: borrow từ bàn trước.
        FixLastTableIfSolo(tables);

        return tables;
    }

    /// <summary>
    /// Strategy 3: Front-heavy. Bàn đầu full (4), bàn sau partial.
    /// Ví dụ: 9 → [4,4,1] (sai) → fix → [4,3,2].
    /// </summary>
    private static List<int> BuildFrontHeavyTables(int playerCount)
    {
        var tables = new List<int>();
        var remaining = playerCount;

        // Fill full bàn đầu
        while (remaining >= MaxTableSize)
        {
            tables.Add(MaxTableSize);
            remaining -= MaxTableSize;
        }

        // Bàn cuối: nếu = 1, borrow từ bàn trước
        if (remaining == 1 && tables.Count > 0)
        {
            tables[tables.Count - 1] -= 1; // 4 → 3
            tables.Add(2);
        }
        else if (remaining > 0)
        {
            tables.Add(remaining);
        }

        return tables;
    }

    /// <summary>
    /// Fix edge case: bàn cuối = 1 người (không hợp lệ).
    /// Borrow 1 người từ bàn trước, đảm bảo bàn cuối ≥ 2.
    /// </summary>
    private static void FixLastTableIfSolo(List<int> tables)
    {
        if (tables.Count < 2) return;

        var lastIdx = tables.Count - 1;
        if (tables[lastIdx] == 1 && tables[lastIdx - 1] > 2)
        {
            tables[lastIdx - 1] -= 1;
            tables[lastIdx] = 2;
        }
    }

    /// <summary>
    /// Quality score: càng cao càng tốt.
    ///
    ///   + Equal bonus:    +10 * (1 - variance/maxSize)        (càng đều càng tốt, weighted cao)
    ///   + Full bonus:     +2 mỗi bàn 4 người                  (bonus nhẹ, không override equal)
    ///   + Penalty:        -100 nếu có bàn 1 người             (không hợp lệ)
    ///   + Penalty:        -3 mỗi bàn 2 người                  (partial penalty)
    ///   + Penalty:        -1 mỗi bàn                           (ưu tiên ít bàn = ít overhead)
    /// </summary>
    private static int EvaluateQuality(List<int> tableSizes, int totalPlayers)
    {
        var score = 0;

        // Variance penalty: càng lệch càng phạt (weighted cao nhất)
        var avg = tableSizes.Average();
        var variance = tableSizes.Sum(s => Math.Pow(s - avg, 2)) / tableSizes.Count;
        score += (int)Math.Round(15 * (1 - variance / MaxTableSize));

        // Full table bonus (nhẹ)
        score += tableSizes.Count(s => s == MaxTableSize) * 2;

        // Penalty cho bàn nhỏ
        if (tableSizes.Any(s => s == 1))
        {
            score -= 100;
        }
        score -= tableSizes.Count(s => s == 2) * 3;

        // Prefer ít bàn hơn
        score -= tableSizes.Count * 2;

        return score;
    }
}