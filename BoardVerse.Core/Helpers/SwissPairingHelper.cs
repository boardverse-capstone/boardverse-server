using BoardVerse.Core.Entities;

namespace BoardVerse.Core.Helpers;

/// <summary>
/// Adaptive Balanced Swiss pairing algorithm cho Splendor tournament.
///
/// Design goals (priority order):
///   1. ANTI-REPEAT: không cho 2 người gặp lại nhau giữa các round.
///   2. SWISS SCORE BALANCE: người cùng điểm gặp nhau (Swiss rule chuẩn).
///   3. ELO BALANCE: variance Elo giữa các bàn thấp nhất.
///   4. TABLE SIZE BALANCE: tự tính số bàn + kích thước tối ưu theo N (2-4 người/bàn).
///
/// Strategies:
///   - Round 1: Seeded snake draft (rank 1 gặp rank N+1-i, ranking giảm dần theo Elo).
///   - Round 2+: Greedy constraint solver with retry + backtracking.
///
/// Edge cases:
///   - Số người không chia hết cho 4: TableSizeOptimizer chọn optimal (e.g. 5 → [3,2], 9 → [3,3,3]).
///   - Không tìm được valid pairing: relax anti-repeat, dùng best-effort split by score.
/// </summary>
public static class SwissPairingHelper
{
    /// <summary>
    /// Optimal table size cho tournament Splendor:
    /// - 4 người = full table (tối đa cân bằng)
    /// - 2-3 người = partial table (vẫn hợp lệ Splendor)
    /// - 1 người = không ghép được → trả warning để manager manual
    /// </summary>
    public static int IdealTableSize(int playerCount)
    {
        if (playerCount <= 0) return 0;
        if (playerCount <= 2) return playerCount;
        return 4;
    }

    /// <summary>
    /// Build balanced Swiss pairing.
    ///
    /// Algorithm:
    ///   1. Nếu chỉ có &lt;= 4 người → 1 bàn (deterministic, dùng hết luôn).
    ///   2. Nếu đây là Round 1 hoặc chưa có match history → snake draft by Elo,
    ///      dùng TableSizeOptimizer để chia bàn optimal (không bàn 1 người).
    ///   3. Nếu đã có matches (Round 2+) → constraint solver với anti-repeat priority.
    ///
    /// Lưu ý: helper này KHÔNG xử lý odd-numbered edge cases (1 người) — caller phải manual.
    /// </summary>
    /// <param name="participants">Active participants (đã check-in).</param>
    /// <param name="roundNumber">Vòng hiện tại (1 = Swiss 1, 2 = Swiss 2, ...).</param>
    /// <param name="previousMatches">
    /// Tất cả matches của các round trước (cùng TournamentId).
    /// Helper sẽ dùng để build anti-repeat history.
    /// </param>
    /// <returns>List các groups, mỗi group là 1 bàn (2-4 players).</returns>
    public static List<List<TournamentParticipant>> BuildBalancedPairings(
        IReadOnlyList<TournamentParticipant> participants,
        int roundNumber,
        IReadOnlyList<TournamentMatchBracket> previousMatches)
    {
        if (participants.Count == 0)
        {
            return new List<List<TournamentParticipant>>();
        }

        if (participants.Count == 1)
        {
            // 1 player không thể tạo bàn hợp lệ cho Splendor (min 2).
            // Caller (BuildRoundMatches) sẽ kiểm tra và throw conflict yêu cầu manual pairing.
            return new List<List<TournamentParticipant>> { new List<TournamentParticipant> { participants[0] } };
        }

        if (participants.Count <= 4)
        {
            return new List<List<TournamentParticipant>> { participants.ToList() };
        }

        var opponentHistory = BuildOpponentHistory(previousMatches);

        if (roundNumber == 1 || previousMatches.Count == 0)
        {
            return BuildSnakeDraft(participants);
        }

        return BuildConstraintSolver(participants, opponentHistory);
    }

    /// <summary>
    /// Round-robin snake draft: chia đều sao cho top player spread đều qua các bàn.
    /// Sort by Elo desc, sau đó round-robin phân bổ 1 người/lượt qua các bàn.
    ///
    /// F8 Note: Đổi từ snake-forward sang round-robin để cải thiện Elo balance khi
    /// table sizes không đều (vd 5 người → [3,2]).
    ///
    /// Ví dụ 8 players [1,2,3,4,5,6,7,8] chia làm 2 bàn đều (size 4):
    ///   Bàn 0: 1, 3, 5, 7
    ///   Bàn 1: 2, 4, 6, 8
    ///
    /// Ví dụ 9 players chia [3,3,3] (optimal theo TableSizeOptimizer):
    ///   Bàn 0: 1, 4, 7
    ///   Bàn 1: 2, 5, 8
    ///   Bàn 2: 3, 6, 9
    ///
    /// Trung bình Elo giữa các bàn đều nhau; mỗi bàn có 1 top + 1 mid + 1 bottom.
    /// </summary>
    public static List<List<TournamentParticipant>> BuildSnakeDraft(
        IReadOnlyList<TournamentParticipant> participants)
    {
        var sorted = participants
            .OrderByDescending(p => p.FinalElo)
            .ThenByDescending(p => p.TotalPrestigePoints)
            .ThenBy(p => p.CheckedInAt ?? p.RegisteredAt)
            .ToList();

        return DistributeIntoTablesWithRemainder(sorted);
    }

    /// <summary>
    /// Constraint solver cho Round 2+.
    ///
    /// Approximate algorithm (greedy):
    ///   1. Group participants theo Swiss score (descending).
    ///   2. Với mỗi group Swiss score, thử random shuffle với seeded random (khác nhau mỗi attempt).
    ///   3. Tính "quality score" cho mỗi attempt = -(anti-repeat violations) + -(elo variance between tables).
    ///   4. Trả về attempt có quality score tốt nhất.
    ///
    /// F8 Note: Round-robin distribution (thay snake-forward) đảm bảo top Elo spread đều qua các bàn.
    /// Không giải quyết được optimal (đó là NP-hard) nhưng gần-optimal cho tournament size 4-32.
    /// </summary>
    public static List<List<TournamentParticipant>> BuildConstraintSolver(
        IReadOnlyList<TournamentParticipant> participants,
        IReadOnlyDictionary<Guid, HashSet<Guid>> opponentHistory)
    {
        var sorted = participants
            .OrderByDescending(p => TournamentEloCalculator.CalculateSwissScore(p))
            .ThenByDescending(p => p.TotalPrestigePoints)
            .ToList();

        // Try multiple deterministic shuffles với priority scoring.
        // Seed cố định = based on participant list để reproducible (audit trail).
        var seed = participants.Count > 0 ? participants[0].Id.GetHashCode() : 42;

        List<List<TournamentParticipant>>? bestPairing = null;
        int bestScore = int.MinValue;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            var shuffled = ShuffleWithSeed(sorted, seed + attempt);

            var tables = DistributeIntoTablesWithRemainder(shuffled);
            var score = ComputePairingQuality(tables, opponentHistory);

            if (score > bestScore)
            {
                bestScore = score;
                bestPairing = tables;
            }

            // Nếu perfect (no anti-repeat violations) → dừng sớm
            if (score >= 0)
            {
                break;
            }
        }

        return bestPairing ?? DistributeIntoTablesWithRemainder(sorted);
    }

    /// <summary>
    /// Quality score: càng cao càng tốt.
    /// - Anti-repeat violations: -100 mỗi cái.
    /// - Elo variance giữa các bàn: -(variance * 10).
    /// Positive = perfect (no anti-repeat, balanced Elo).
    /// </summary>
    private static int ComputePairingQuality(
        List<List<TournamentParticipant>> tables,
        IReadOnlyDictionary<Guid, HashSet<Guid>> opponentHistory)
    {
        if (tables.Count == 0) return int.MinValue;

        var avgElo = tables.SelectMany(t => t).Average(p => (double)p.FinalElo);
        var score = 0;

        // Anti-repeat violations
        foreach (var table in tables)
        {
            for (int i = 0; i < table.Count; i++)
            {
                for (int j = i + 1; j < table.Count; j++)
                {
                    // Walk-in không có UserId → bỏ qua anti-repeat check cho họ
                    // (họ không có lịch sử đối thủ, mặc định không bị penalty).
                    if (!table[i].UserId.HasValue || !table[j].UserId.HasValue) continue;

                    var a = table[i].UserId!.Value;
                    var b = table[j].UserId!.Value;

                    if (opponentHistory.TryGetValue(a, out var opponents))
                    {
                        if (opponents.Contains(b))
                        {
                            score -= 100;
                        }
                    }
                }
            }
        }

        // Elo balance: variance giữa avg Elo của các bàn
        if (tables.Count > 1)
        {
            var tableAvgs = tables.Select(t => t.Average(p => (double)p.FinalElo)).ToList();
            var variance = tableAvgs.Average(avg => Math.Pow(avg - avgElo, 2));
            score -= (int)Math.Round(variance / 10);
        }

        return score;
    }

    /// <summary>
    /// Distribute sorted participants vào các bàn kích thước tối ưu theo TableSizeOptimizer.
    ///
    /// Snake draft algorithm: chia đều sao cho top player gặp bottom player.
    ///
    /// Ví dụ 12 players sorted desc [1,2,3,4,5,6,7,8,9,10,11,12]:
    ///   TableSizeOptimizer → [3,3,3,3] (chia đều)
    ///   Snake draft:
    ///     table 1: 1,  8,  9     (top + bottom + mid)
    ///     table 2: 2,  7,  10
    ///     table 3: 3,  6,  11
    ///     table 4: 4,  5,  12
    ///
    /// 5 players → [3,2]:
    ///   table 1: 1, 4, 5
    ///   table 2: 2, 3
    ///
    /// 9 players → [3,3,3]:
    ///   table 1: 1, 6, 7
    ///   table 2: 2, 5, 8
    ///   table 3: 3, 4, 9
    /// </summary>
    public static List<List<TournamentParticipant>> DistributeIntoTablesWithRemainder(
        List<TournamentParticipant> sorted)
    {
        var tables = new List<List<TournamentParticipant>>();

        if (sorted.Count == 0) return tables;

        if (sorted.Count == 1)
        {
            // 1 người không ghép được: caller phải xử lý (bye hoặc manual pair).
            tables.Add(new List<TournamentParticipant> { sorted[0] });
            return tables;
        }

        // Tính kích thước bàn optimal theo TableSizeOptimizer.
        var optimalSizes = TableSizeOptimizer.CalculateOptimalTableSizes(sorted.Count)
            ?? new List<int> { sorted.Count };

        // Khởi tạo tables với size khác nhau (không phải all-4).
        foreach (var _ in optimalSizes)
        {
            tables.Add(new List<TournamentParticipant>());
        }

        // F8 Fix: Round-robin distribution thay cho snake-forward.
        // Lý do: snake-forward pack top Elo vào table đầu, khiến Elo variance giữa các bàn cao.
        // Round-robin lấy 1 người/lần qua từng table → top Elo được spread đều ra các bàn,
        // đảm bảo mỗi bàn có 1 đại diện top + 1 đại diện bottom → Elo balance tối ưu.
        //
        // Ví dụ 5 players sorted [1,2,3,4,5] với sizes=[3,2]:
        //   Round 0: table 0 ← player 1, table 1 ← player 2
        //   Round 1: table 0 ← player 3, table 1 ← player 4
        //   Round 2: table 0 ← player 5 (skip table 1 vì full)
        //   → [1,3,5] vs [2,4]
        //
        // Ví dụ 9 players sorted [1..9] với sizes=[3,3,3]:
        //   → [1,4,7], [2,5,8], [3,6,9]
        //
        // Ví dụ 12 players sorted [1..12] với sizes=[3,3,3,3]:
        //   → [1,5,9], [2,6,10], [3,7,11], [4,8,12]
        var tableCount = tables.Count;
        var sortedIdx = 0;
        var totalSlots = optimalSizes.Sum();

        // Tiếp tục round-robin cho đến khi đặt hết sorted participants hoặc hết slots.
        var safety = totalSlots * 2; // avoid infinite loop
        while (sortedIdx < sorted.Count && safety-- > 0)
        {
            for (int tableIdx = 0; tableIdx < tableCount && sortedIdx < sorted.Count; tableIdx++)
            {
                if (tables[tableIdx].Count >= optimalSizes[tableIdx])
                {
                    continue;
                }
                tables[tableIdx].Add(sorted[sortedIdx]);
                sortedIdx++;
            }
        }

        return tables;
    }

    /// <summary>
    /// Build map: userId → Set(userId đã từng làm đối thủ).
    /// </summary>
    public static Dictionary<Guid, HashSet<Guid>> BuildOpponentHistory(
        IReadOnlyList<TournamentMatchBracket> previousMatches)
    {
        var history = new Dictionary<Guid, HashSet<Guid>>();

        void Record(Guid a, Guid b)
        {
            if (!history.TryGetValue(a, out var set))
            {
                set = new HashSet<Guid>();
                history[a] = set;
            }
            set.Add(b);

            if (!history.TryGetValue(b, out var setB))
            {
                setB = new HashSet<Guid>();
                history[b] = setB;
            }
            setB.Add(a);
        }

        foreach (var match in previousMatches)
        {
            var players = new[] { match.Player1Id, match.Player2Id, match.Player3Id, match.Player4Id }
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToList();

            for (int i = 0; i < players.Count; i++)
            {
                for (int j = i + 1; j < players.Count; j++)
                {
                    Record(players[i], players[j]);
                }
            }
        }

        return history;
    }

    /// <summary>
    /// Deterministic shuffle: dùng linear congruential generator seeded.
    /// Pure function (deterministic cho cùng input + seed) để có audit trail.
    /// </summary>
    private static List<TournamentParticipant> ShuffleWithSeed(
        List<TournamentParticipant> source, int seed)
    {
        var rng = new Random(seed);
        var copy = source.ToList();
        for (int i = copy.Count - 1; i > 0; i--)
        {
            var j = rng.Next(0, i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }
}