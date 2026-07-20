using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

/// <summary>
/// Unit tests cho <see cref="TournamentEloCalculator"/> — Elo delta cho multi-player Splendor tournament.
/// </summary>
public class TournamentEloCalculatorTests
{
    /// <summary>
    /// F10 Fix: Khi có walk-in (UserId = null không thể làm key trong dictionary, nhưng calculator
    /// giả định walk-in vẫn có UserId guid với rating = default 1200). Verify expected score
    /// được tính với tất cả opponents có trong dict (không bỏ qua ai).
    /// Đây là behavior hợp lệ vì Service layer filter walk-in trước khi pass vào calculator.
    /// </summary>
    [Fact]
    public void CalculateMatchEloChanges_4Players_AllStartAt1200_WinnerGainsLosersLose()
    {
        // Arrange: 4 players cùng rating 1200, 1 winner 3 losers.
        // expected mỗi người = 0.5 (3 opponents cùng 1200).
        // Winner delta = K*0.5 > 0; losers delta = K*(-0.5) < 0.
        // Tổng delta ≈ -K (asymmetric vì 1 win + 3 loss = 1 điểm actual, expected = 2 điểm).
        // Multi-player Elo không zero-sum với kết quả asymmetric — đây là behavior thiết kế.
        var ratings = new Dictionary<Guid, int>
        {
            [Guid.NewGuid()] = 1200,
            [Guid.NewGuid()] = 1200,
            [Guid.NewGuid()] = 1200,
            [Guid.NewGuid()] = 1200
        };
        var winner = ratings.Keys.First();

        // Act
        var deltas = TournamentEloCalculator.CalculateMatchEloChanges(ratings, winner, isDraw: false);

        // Assert: Winner có delta > 0
        Assert.True(deltas[winner] > 0);
        // 3 losers có delta < 0
        var losers = deltas.Where(kv => kv.Key != winner).Select(kv => kv.Value);
        Assert.All(losers, d => Assert.True(d < 0, $"Loser delta should be negative, got {d}"));

        // Verify asymmetry: winners gain lớn hơn losers mất (multi-player design).
        // Với K-factor tiêu chuẩn (32-64 cho rating < 2000), winner +16..+32, losers -16..-32 each.
        // Total = winner_gain + 3 * (loser_loss) = 1 * K * 0.5 + 3 * (-K * 0.5) = -K
        // → total âm (negative), magnitude bằng K-factor.
        var sum = deltas.Values.Sum();
        Assert.True(sum < 0, $"Multi-player asymmetric: total Elo delta should be negative (Elo drain from match), got {sum}");
        Assert.InRange(Math.Abs(sum), 16, 80); // K-factor trong khoảng 16-80
    }

    [Fact]
    public void CalculateMatchEloChanges_UnderdogWins_GainsMoreThanEvenMatch()
    {
        // Arrange: player mạng 1500 thắng player yếu 1100 → underdog = big loser
        var weakWinner = Guid.NewGuid();
        var strongLoser1 = Guid.NewGuid();
        var strongLoser2 = Guid.NewGuid();
        var weakTeammate = Guid.NewGuid();

        var ratings = new Dictionary<Guid, int>
        {
            [weakWinner] = 1100,
            [strongLoser1] = 1500,
            [strongLoser2] = 1500,
            [weakTeammate] = 1100
        };

        // Act
        var deltas = TournamentEloCalculator.CalculateMatchEloChanges(ratings, weakWinner, isDraw: false);

        // Assert: weak winner có delta rất lớn (upset bonus)
        Assert.True(deltas[weakWinner] >= 20, $"Underdog winner should gain big, got {deltas[weakWinner]}");
        // Strong losers bị trừ nhiều (gặp weak)
        Assert.True(deltas[strongLoser1] <= -15, $"Strong loser should lose big, got {deltas[strongLoser1]}");
        // Weak teammate (cùng team mạng weak) mất ít
        Assert.True(Math.Abs(deltas[weakTeammate]) < Math.Abs(deltas[strongLoser1]),
            "Weak teammate should lose less than strong losers");
    }

    [Fact]
    public void CalculateMatchEloChanges_Draw_AllGetZeroDelta()
    {
        // Arrange: tất cả cùng rating, hòa → expected = 0.5, actual = 0.5 → delta = 0
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var p3 = Guid.NewGuid();
        var p4 = Guid.NewGuid();

        var ratings = new Dictionary<Guid, int>
        {
            [p1] = 1200, [p2] = 1200, [p3] = 1200, [p4] = 1200
        };

        // Act
        var deltas = TournamentEloCalculator.CalculateMatchEloChanges(ratings, null, isDraw: true);

        // Assert: tất cả delta = 0
        Assert.Equal(0, deltas[p1]);
        Assert.Equal(0, deltas[p2]);
        Assert.Equal(0, deltas[p3]);
        Assert.Equal(0, deltas[p4]);
    }

    [Fact]
    public void CalculateMatchEloChanges_FewerThan2Players_NoOp()
    {
        var ratings = new Dictionary<Guid, int> { [Guid.NewGuid()] = 1200 };
        var deltas = TournamentEloCalculator.CalculateMatchEloChanges(ratings, null, isDraw: false);
        Assert.Single(deltas);
        Assert.Equal(0, deltas.Values.First());
    }

    [Fact]
    public void CalculateSwissScore_3Wins2Losses_DefaultPoints()
    {
        var participant = new Core.Entities.TournamentParticipant
        {
            SwissWins = 3,
            SwissDraws = 0,
            SwissLosses = 2
        };

        // Win = 1 điểm, Loss = 0, Draw = 0.5
        var score = TournamentEloCalculator.CalculateSwissScore(participant);

        Assert.Equal(3.0, score); // 3 * 1 + 0 * 0.5 + 2 * 0
    }

    [Fact]
    public void CalculateSwissScore_WithDraws()
    {
        var participant = new Core.Entities.TournamentParticipant
        {
            SwissWins = 2,
            SwissDraws = 1,
            SwissLosses = 1
        };

        var score = TournamentEloCalculator.CalculateSwissScore(participant);

        // 2*1 + 1*0.5 + 1*0 = 2.5
        Assert.Equal(2.5, score);
    }
}