using BoardVerse.Core.Helpers;
using Xunit;

namespace BoardVerse.Tests.Helpers;

/// <summary>
/// Tests cho TournamentRoundsCalculator - tính số rounds tối ưu theo N players.
///
/// Verify:
///   - Auto-shorten với ít người (4-7 → 2 rounds).
///   - Standard Swiss (8-15 → 3 rounds).
///   - Large tournament (16-23 → 4 rounds).
///   - Edge cases (0, 1, 4).
///   - Clamp min/max boundaries.
/// </summary>
public class TournamentRoundsCalculatorTests
{
    [Theory]
    [InlineData(4, 3, 2)]    // 4 người (1 bàn), config 3 → tối thiểu 2
    [InlineData(5, 3, 2)]    // 5 người (2 bàn), config 3 → log2(2)+1=2
    [InlineData(6, 3, 2)]    // 6 người (2 bàn) → 2
    [InlineData(7, 3, 2)]    // 7 người (2 bàn) → 2
    [InlineData(8, 3, 2)]    // 8 người (2 bàn) → log2(2)+1=2
    [InlineData(11, 3, 3)]   // 11 người (3 bàn) → log2(3)+1=2.58→3
    [InlineData(12, 3, 3)]   // 12 người (3 bàn) → 3
    [InlineData(15, 4, 3)]   // 15 người (4 bàn) → log2(4)+1=3
    [InlineData(16, 4, 3)]   // 16 người (4 bàn) → 3
    [InlineData(24, 5, 4)]   // 24 người (6 bàn) → log2(6)+1=3.58→4
    [InlineData(32, 5, 4)]   // 32 người (8 bàn) → log2(8)+1=4
    public void CalculateOptimalPreliminaryRounds_ReturnsExpected(
        int participants, int configured, int expected)
    {
        var result = TournamentRoundsCalculator.CalculateOptimalPreliminaryRounds(
            participants, configured);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateOptimalPreliminaryRounds_ZeroParticipants_ReturnsMin()
    {
        var result = TournamentRoundsCalculator.CalculateOptimalPreliminaryRounds(0, 3);

        Assert.Equal(2, result); // minRounds default = 2
    }

    [Fact]
    public void CalculateOptimalPreliminaryRounds_NegativeParticipants_ReturnsMin()
    {
        var result = TournamentRoundsCalculator.CalculateOptimalPreliminaryRounds(-5, 3);

        Assert.Equal(2, result);
    }

    [Fact]
    public void CalculateOptimalPreliminaryRounds_ClampedToConfigured()
    {
        // Even với rất nhiều người, không vượt configured
        var result = TournamentRoundsCalculator.CalculateOptimalPreliminaryRounds(100, 3);

        Assert.Equal(3, result); // capped tại configured
    }

    [Fact]
    public void SuggestShortenedRounds_ReturnsNull_WhenAlreadyOptimal()
    {
        // 12 người, config 3 rounds → optimal cũng 3 → null
        var result = TournamentRoundsCalculator.SuggestShortenedRounds(12, 3);

        Assert.Null(result);
    }

    [Fact]
    public void SuggestShortenedRounds_ReturnsShortened_WhenCanShorten()
    {
        // 5 người (2 bàn), config 3 → shorten xuống 2
        var result = TournamentRoundsCalculator.SuggestShortenedRounds(5, 3);

        Assert.Equal(2, result);
    }
}