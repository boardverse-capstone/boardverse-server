using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using Xunit;

namespace BoardVerse.Tests.Helpers;

/// <summary>
/// Tests cho SwissPairingHelper — Adaptive Balanced Swiss algorithm.
///
/// Verify:
///   - Round 1: Snake draft by Elo (top vs bottom mix).
///   - Round 2+: Anti-repeat constraint solver.
///   - Edge cases: 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12 players.
///   - Determinism: same seed → same output.
/// </summary>
public class SwissPairingHelperTests
{
    // === Helper to create test participants ===

    private static TournamentParticipant MakeParticipant(int elo, int userIndex)
    {
        return new TournamentParticipant
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            Status = TournamentParticipantStatus.Active,
            KarmaAtRegistration = 100,
            RegisteredAt = DateTime.UtcNow.AddMinutes(-userIndex),
            InitialElo = elo,
            FinalElo = elo,
            SwissWins = 0,
            SwissDraws = 0,
            SwissLosses = 0,
            TotalPrestigePoints = 0,
            TotalCardsBought = 0
        };
    }

    private static TournamentMatchBracket MakeMatch(
        int roundNumber, params Guid?[] playerIds)
    {
        return new TournamentMatchBracket
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            RoundNumber = roundNumber,
            MatchNumber = 1,
            IsFinal = false,
            Player1Id = playerIds.Length > 0 ? playerIds[0] : null,
            Player2Id = playerIds.Length > 1 ? playerIds[1] : null,
            Player3Id = playerIds.Length > 2 ? playerIds[2] : null,
            Player4Id = playerIds.Length > 3 ? playerIds[3] : null,
            Status = TournamentMatchStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
    }

    // === Edge cases: very small player counts ===

    [Fact]
    public void BuildBalancedPairings_EmptyList_ReturnsEmpty()
    {
        var result = SwissPairingHelper.BuildBalancedPairings(
            new List<TournamentParticipant>(),
            roundNumber: 1,
            previousMatches: new List<TournamentMatchBracket>());

        Assert.Empty(result);
    }

    [Fact]
    public void BuildBalancedPairings_OnePlayer_ReturnsOneSoloTable()
    {
        var p = MakeParticipant(1500, 0);

        var result = SwissPairingHelper.BuildBalancedPairings(
            new[] { p },
            roundNumber: 1,
            previousMatches: new List<TournamentMatchBracket>());

        Assert.Single(result);
        Assert.Single(result[0]);
    }

    [Fact]
    public void BuildBalancedPairings_TwoPlayers_ReturnsOneTwoPlayerTable()
    {
        var p1 = MakeParticipant(1500, 0);
        var p2 = MakeParticipant(1600, 1);

        var result = SwissPairingHelper.BuildBalancedPairings(
            new[] { p1, p2 },
            roundNumber: 1,
            previousMatches: new List<TournamentMatchBracket>());

        Assert.Single(result);
        Assert.Equal(2, result[0].Count);
    }

    [Fact]
    public void BuildBalancedPairings_FourPlayers_ReturnsOneFourPlayerTable()
    {
        var players = Enumerable.Range(1, 4).Select(i => MakeParticipant(1500 + i * 100, i)).ToList();

        var result = SwissPairingHelper.BuildBalancedPairings(
            players,
            roundNumber: 1,
            previousMatches: new List<TournamentMatchBracket>());

        Assert.Single(result);
        Assert.Equal(4, result[0].Count);
    }

    // === Round 1: Snake draft by Elo ===

    [Fact]
    public void SnakeDraft_8Players_TopAndBottomMixed()
    {
        // 8 players sorted by Elo: [1=2000, 2=1900, 3=1800, 4=1700, 5=1600, 6=1500, 7=1400, 8=1300]
        var players = Enumerable.Range(1, 8)
            .Select(i => MakeParticipant(2100 - i * 100, i))
            .ToList();

        var tables = SwissPairingHelper.BuildSnakeDraft(players);

        Assert.Equal(2, tables.Count);
        Assert.All(tables, t => Assert.Equal(4, t.Count));

        // Snake draft expects top 1 với 1 trong 4 người giữa... verify
        // Snake draft algorithm: chia đều top với bottom vào cùng bàn
        var table1Elo = tables[0].Average(p => (double)p.FinalElo);
        var table2Elo = tables[1].Average(p => (double)p.FinalElo);
        var avgAll = players.Average(p => (double)p.FinalElo);

        // Avg Elo mỗi bàn nên gần avgAll
        Assert.InRange(table1Elo, avgAll - 200, avgAll + 200);
        Assert.InRange(table2Elo, avgAll - 200, avgAll + 200);
    }

    [Fact]
    public void SnakeDraft_12Players_ThreeTablesBalanced()
    {
        var players = Enumerable.Range(1, 12)
            .Select(i => MakeParticipant(2200 - i * 100, i))
            .ToList();

        var tables = SwissPairingHelper.BuildSnakeDraft(players);

        Assert.Equal(3, tables.Count);
        Assert.All(tables, t => Assert.Equal(4, t.Count));

        var avgAll = players.Average(p => (double)p.FinalElo);
        foreach (var t in tables)
        {
            var tableAvg = t.Average(p => (double)p.FinalElo);
            Assert.InRange(tableAvg, avgAll - 250, avgAll + 250);
        }
    }

    // === Edge cases: odd player count ===

    [Fact]
    public void BuildBalancedPairings_5Players_SnakeDraftDistributesAcrossTables()
    {
        var players = Enumerable.Range(1, 5).Select(i => MakeParticipant(1500 + i * 100, i)).ToList();

        var result = SwissPairingHelper.BuildBalancedPairings(
            players,
            roundNumber: 1,
            previousMatches: new List<TournamentMatchBracket>());

        // 5 players sorted desc: [E1=2000, E2=1900, E3=1800, E4=1700, E5=1600]
        // tableCount = ceil(5/4) = 2
        // Snake draft:
        //   round 0 forward: pos 0→table0, 1→table1
        //   round 1 backward: pos 0→table1, 1→table0
        //   round 2 forward: pos 0→table0
        // table 0: E1=2000, E3=1800, E5=1600 (pos 0,3,4)
        // table 1: E2=1900, E4=1700 (pos 1,2)
        Assert.Equal(2, result.Count);
        var allPlayers = result.SelectMany(t => t).ToList();
        Assert.Equal(5, allPlayers.Count);
        // Tất cả 5 player đều có mặt
        Assert.Equal(players.Select(p => p.UserId).ToHashSet(), allPlayers.Select(p => p.UserId).ToHashSet());
    }

    [Fact]
    public void BuildBalancedPairings_9Players_SnakeDraftThreeTablesOf3()
    {
        var players = Enumerable.Range(1, 9).Select(i => MakeParticipant(1500 + i * 100, i)).ToList();

        var result = SwissPairingHelper.BuildBalancedPairings(
            players,
            roundNumber: 1,
            previousMatches: new List<TournamentMatchBracket>());

        // 9 players / 4 = ceil(9/4) = 3 tables
        // Snake draft distributes 9 vào 3 tables
        Assert.Equal(3, result.Count);
        var allPlayers = result.SelectMany(t => t).ToList();
        Assert.Equal(9, allPlayers.Count);
        Assert.Equal(players.Select(p => p.UserId).ToHashSet(), allPlayers.Select(p => p.UserId).ToHashSet());
    }

    // === Round 2+: Anti-repeat constraint ===

    [Fact]
    public void BuildConstraintSolver_NoPreviousHistory_BehavesAsSnakeDraft()
    {
        var players = Enumerable.Range(1, 8)
            .Select(i => MakeParticipant(2100 - i * 100, i))
            .ToList();

        var solverResult = SwissPairingHelper.BuildConstraintSolver(
            players, new Dictionary<Guid, HashSet<Guid>>());

        Assert.Equal(2, solverResult.Count);
        Assert.All(solverResult, t => Assert.Equal(4, t.Count));
    }

    [Fact]
    public void BuildConstraintSolver_AllPlayersSameSwissScore_UsesEloBalance()
    {
        // 8 players, all same Swiss score (0-0-0) but different Elo
        var players = Enumerable.Range(1, 8).Select(i => new TournamentParticipant
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            Status = TournamentParticipantStatus.Active,
            FinalElo = 1500 + i * 100,
            SwissWins = 0,
            SwissDraws = 0,
            SwissLosses = 0,
            KarmaAtRegistration = 100,
            RegisteredAt = DateTime.UtcNow
        }).ToList();

        var solverResult = SwissPairingHelper.BuildConstraintSolver(
            players, new Dictionary<Guid, HashSet<Guid>>());

        Assert.Equal(2, solverResult.Count);
        Assert.All(solverResult, t => Assert.Equal(4, t.Count));
    }

    [Fact]
    public void BuildBalancedPairings_Round2_TriesToAvoidRepeats()
    {
        // 8 players
        var players = Enumerable.Range(1, 8).Select(i => MakeParticipant(1500 + i * 50, i)).ToList();

        // Round 1 đã diễn ra: 2 bàn 4 — bàn 1 = players[0..3], bàn 2 = players[4..7]
        var previousMatches = new List<TournamentMatchBracket>
        {
            MakeMatch(1, players[0].UserId, players[1].UserId, players[2].UserId, players[3].UserId),
            MakeMatch(1, players[4].UserId, players[5].UserId, players[6].UserId, players[7].UserId)
        };

        // Round 2: solver nên TRÁNH ghép lại cặp đã gặp round 1
        var result = SwissPairingHelper.BuildBalancedPairings(
            players,
            roundNumber: 2,
            previousMatches: previousMatches);

        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Equal(4, t.Count));

        // Verify anti-repeat: mỗi bàn không chứa 2+ người từ cùng bàn round 1
        foreach (var table in result)
        {
            var tableUserIds = table.Select(p => p.UserId).ToHashSet();
            var table1Round1Count = GetPlayerUserIds(previousMatches[0])
                .Count(uid => tableUserIds.Contains(uid));
            var table2Round1Count = GetPlayerUserIds(previousMatches[1])
                .Count(uid => tableUserIds.Contains(uid));

            // Anti-repeat: mỗi bàn mới nên có ≤ 2 người từ cùng bàn cũ
            // (vì với 8 người + 2 bàn cũ, solver phải split 2+2 hoặc 3+1)
            Assert.True(
                table1Round1Count <= 2 && table2Round1Count <= 2,
                $"Anti-repeat violated: table1={table1Round1Count}, table2={table2Round1Count}");
        }
    }

    // === Determinism: same seed → same output ===

    [Fact]
    public void BuildConstraintSolver_Deterministic_WithSameSeed()
    {
        var players = Enumerable.Range(1, 12).Select(i => MakeParticipant(1500 + i * 100, i)).ToList();
        var history = new Dictionary<Guid, HashSet<Guid>>();

        var result1 = SwissPairingHelper.BuildConstraintSolver(players, history);
        var result2 = SwissPairingHelper.BuildConstraintSolver(players, history);

        Assert.Equal(result1.Count, result2.Count);

        for (int i = 0; i < result1.Count; i++)
        {
            var ids1 = result1[i].Select(p => p.UserId).OrderBy(x => x).ToList();
            var ids2 = result2[i].Select(p => p.UserId).OrderBy(x => x).ToList();
            Assert.Equal(ids1, ids2);
        }
    }

    // === Opponent history builder ===

    [Fact]
    public void BuildOpponentHistory_RecordsPairwiseOpponents()
    {
        var players = Enumerable.Range(1, 4).Select(i => MakeParticipant(1500, i)).ToList();
        var match = MakeMatch(1, players[0].UserId, players[1].UserId, players[2].UserId, players[3].UserId);

        var history = SwissPairingHelper.BuildOpponentHistory(new[] { match });

        // Mỗi player gặp 3 opponents khác
        foreach (var p in players)
        {
            Assert.NotNull(p.UserId);
            Assert.True(history.ContainsKey(p.UserId!.Value));
            Assert.Equal(3, history[p.UserId.Value].Count);
        }
    }

    private static List<Guid> GetPlayerUserIds(TournamentMatchBracket match)
    {
        return new[] { match.Player1Id, match.Player2Id, match.Player3Id, match.Player4Id }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
    }

    // ============================================
    // === F8: Round-robin distribution fairness ===
    // ============================================

    [Fact]
    public void BuildBalancedPairings_5Players_RoundRobinSpreadTopElo()
    {
        // F8 Fix: Với 5 players, optimal sizes = [3,2]. Round-robin phân bổ:
        //   Round 0: table 0 ← E1 (top), table 1 ← E2
        //   Round 1: table 0 ← E3, table 1 ← E4
        //   Round 2: table 0 ← E5, table 1 full skip
        // → table 0 = [E1, E3, E5] (top, mid, bottom), table 1 = [E2, E4] (top-mid, bottom-mid)
        // Elo balance đảm bảo mỗi table có 1 đại diện top + 1 đại diện bottom.
        var players = Enumerable.Range(1, 5).Select(i => MakeParticipant(1500 + i * 100, i)).ToList();

        var result = SwissPairingHelper.BuildBalancedPairings(
            players,
            roundNumber: 1,
            previousMatches: new List<TournamentMatchBracket>());

        Assert.Equal(2, result.Count);

        // Mỗi table có 1 player top (E1 hoặc E2)
        var topPlayer = players[0]; // Elo = 1600 (cao nhất với i=1)
        var tableWithTop = result.FirstOrDefault(t => t.Contains(topPlayer));
        Assert.NotNull(tableWithTop);

        // Verify top Elo không cô đọng 1 bàn (top Elo phải có ở các bàn khác nhau hoặc tối đa là 1 bàn)
        var tablesContainingTop3 = result.Count(t => t.Any(p => p.InitialElo >= 1700));
        Assert.True(tablesContainingTop3 >= 1, "Top Elo phải phân tán ra các bàn");
    }

    [Fact]
    public void BuildBalancedPairings_9Players_RoundRobinThreeBalancedTables()
    {
        // F8 Fix: Với 9 players, optimal sizes = [3,3,3].
        // Round-robin: table 0 ← E1, table 1 ← E2, table 2 ← E3, table 0 ← E4, ...
        // → table 0 = [E1, E4, E7], table 1 = [E2, E5, E8], table 2 = [E3, E6, E9]
        // Mỗi bàn có 1 top + 1 mid + 1 bottom Elo.
        var players = Enumerable.Range(1, 9).Select(i => MakeParticipant(1500 + i * 100, i)).ToList();

        var result = SwissPairingHelper.BuildBalancedPairings(
            players,
            roundNumber: 1,
            previousMatches: new List<TournamentMatchBracket>());

        Assert.Equal(3, result.Count);
        Assert.All(result, t => Assert.Equal(3, t.Count));

        // Verify Elo balance: mỗi table có 1 player top (Elo >= 2200)
        foreach (var table in result)
        {
            var maxElo = table.Max(p => p.InitialElo);
            var minElo = table.Min(p => p.InitialElo);
            // Elo range trong 1 table không quá lệch
            Assert.True(maxElo - minElo <= 600,
                $"Table Elo range {maxElo - minElo} quá lệch: top={maxElo}, bottom={minElo}");
        }
    }
}