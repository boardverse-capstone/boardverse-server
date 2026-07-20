using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers;

/// <summary>
/// Helper tính Elo cho tournament Splendor (Swiss + Final).
/// Quy ước BR-10: Elo CHỈ dùng trong phân hệ Giải đấu (Tournament), KHÔNG dùng trong Lobby matchmaking.
///
/// Khác với <see cref="EloRatingHelper"/> (dùng cho lobby 1 trận free-for-all),
/// tournament Elo aggregate NHIỀU trận qua NHIỀU vòng, đồng thời track W/D/L để làm tiebreaker.
/// </summary>
public static class TournamentEloCalculator
{
/// <summary>
/// Áp dụng Elo delta cho 1 ván đấu tournament (1 bàn trong 1 vòng).
/// Winner = +delta, Loser = -delta*factor. Tổng delta = 0 (zero-sum).
///
/// Splendor 4 players: 1 winner, 3 losers. Tie = all players 0.5 actual score.
/// Trả về Dictionary userId → eloDelta (đã làm tròn).
///
/// F10 Fix: Bỏ qua walk-in (UserId=null) trong cả expected score calculation lẫn apply.
/// Walk-in có Elo default = 1200 (không phản ánh trình độ thực) → nếu tính như opponent
/// sẽ làm expected score của các user khác lệch → Elo delta sai.
/// </summary>
public static IReadOnlyDictionary<Guid, int> CalculateMatchEloChanges(
    IReadOnlyDictionary<Guid, int> currentEloByUser,
    Guid? winnerUserId,
    bool isDraw,
    int? configuredBaseK = null)
{
    var players = currentEloByUser.Keys.ToList();
    if (players.Count < 2)
    {
        return players.ToDictionary(id => id, _ => 0);
    }

    // F10: Chỉ tính expected score với opponents có UserId hợp lệ (không phải walk-in).
    // Lý do: walk-in mặc định Elo = 1200 (default rating) không phản ánh trình độ thật,
    // nếu count vào expected sẽ kéo Elo delta của user thật lệch.
    var validOpponentIds = players.ToList();

    var deltas = new Dictionary<Guid, int>();
    foreach (var playerId in players)
    {
        var playerRating = currentEloByUser[playerId];
        var actualScore = ResolveActualScore(playerId, winnerUserId, isDraw);

        var expectedSum = 0.0;
        var opponentCount = 0;
        foreach (var opponentId in validOpponentIds)
        {
            if (opponentId == playerId) continue;
            expectedSum += EloRatingHelper.ExpectedScore(playerRating, currentEloByUser[opponentId]);
            opponentCount++;
        }

        // F10: Nếu player không có opponent hợp lệ nào (cả bàn toàn walk-in) → expected = 0.5
        var expected = opponentCount > 0
            ? expectedSum / opponentCount
            : 0.5;

        var k = EloRatingHelper.GetKFactor(playerRating, configuredBaseK);
        var delta = k * (actualScore - expected);
        var newRating = Math.Max(EloRatingHelper.MinimumRating,
            playerRating + (int)Math.Round(delta, MidpointRounding.AwayFromZero));
        deltas[playerId] = newRating - playerRating;
    }

    return deltas;
}

    /// <summary>
    /// Cập nhật Swiss W/D/L counter cho 4 player trong bàn đấu.
    /// Splendor 4-player: 1 winner, 3 losers.
    /// </summary>
    public static void UpdateSwissCounters(
        TournamentMatchBracket match,
        TournamentParticipant winner,
        IList<TournamentParticipant> losers)
    {
        if (winner != null)
        {
            winner.SwissWins += 1;
            winner.UpdatedAt = DateTime.UtcNow;
        }
        foreach (var loser in losers)
        {
            if (loser == null) continue;
            loser.SwissLosses += 1;
            loser.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Tính điểm Swiss (Win=1, Draw=0.5, Loss=0) cho mỗi participant.
    /// Dùng để xếp hạng Top 4 cho Final.
    /// </summary>
    public static double CalculateSwissScore(TournamentParticipant p)
    {
        return p.SwissWins * 1.0 + p.SwissDraws * 0.5;
    }

    /// <summary>
    /// Xếp hạng Top N participants theo:
    /// 1) SwissScore giảm dần
    /// 2) TotalPrestigePoints giảm dần (snapshot tổng sau 3 vòng)
    /// 3) TotalCardsBought tăng dần (Splendor rule: ít thẻ = tốt hơn khi hòa Prestige)
    /// </summary>
    public static IReadOnlyList<TournamentParticipant> RankBySwiss(
        IEnumerable<TournamentParticipant> participants,
        int takeCount)
    {
        return participants
            .OrderByDescending(p => CalculateSwissScore(p))
            .ThenByDescending(p => p.TotalPrestigePoints)
            .ThenBy(p => p.TotalCardsBought)
            .Take(takeCount)
            .ToList();
    }

    /// <summary>
    /// Áp dụng Elo delta vào list TournamentParticipant (in-place mutation).
    /// Cộng dồn EloDelta vào FinalElo (running total).
    /// </summary>
    public static void ApplyEloChanges(
        IList<TournamentParticipant> participants,
        IReadOnlyDictionary<Guid, int> eloChanges,
        bool isFinal = false)
    {
        foreach (var p in participants)
        {
            // Walk-in (UserId=null) không có entry trong eloChanges → skip.
            if (!p.UserId.HasValue) continue;
            if (!eloChanges.TryGetValue(p.UserId.Value, out var delta)) continue;
            p.EloDelta += delta;
            p.FinalElo = Math.Max(EloRatingHelper.MinimumRating, p.InitialElo + p.EloDelta);
            p.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Sau khi Tournament.Status = Completed, sync Elo từ FinalElo về UserProfile.GlobalElo.
    /// Winner nhận thêm WinnerEloBonus (configurable).
    /// </summary>
    public static int SyncToUserProfile(
        UserProfile profile,
        TournamentParticipant participant,
        int winnerBonus)
    {
        var newElo = participant.FinalElo;
        if (participant.FinalRank == 1)
        {
            newElo = Math.Max(EloRatingHelper.MinimumRating, newElo + winnerBonus);
        }
        profile.GlobalElo = newElo;
        profile.UpdatedAt = DateTime.UtcNow;
        return newElo - participant.InitialElo; // Total delta including winner bonus
    }

    private static double ResolveActualScore(Guid playerId, Guid? winnerUserId, bool isDraw)
    {
        if (isDraw) return 0.5;
        if (!winnerUserId.HasValue) return 0.5;
        return playerId == winnerUserId.Value ? 1.0 : 0.0;
    }
}