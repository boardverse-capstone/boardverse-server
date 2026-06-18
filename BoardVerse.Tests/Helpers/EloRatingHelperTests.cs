using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class EloRatingHelperTests
{
    private static readonly Guid Player1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Player2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(1200, 32)]
    [InlineData(2200, 24)]
    [InlineData(2500, 16)]
    public void GetKFactor_UsesConfiguredBaseK(int rating, int expectedK)
    {
        Assert.Equal(expectedK, EloRatingHelper.GetKFactor(rating, configuredBaseK: 32));
    }

    [Fact]
    public void ExpectedScore_IsHigherForStrongerPlayer()
    {
        var strongerExpected = EloRatingHelper.ExpectedScore(1400, 1200);
        var weakerExpected = EloRatingHelper.ExpectedScore(1200, 1400);

        Assert.True(strongerExpected > weakerExpected);
        Assert.InRange(strongerExpected + weakerExpected, 0.99, 1.01);
    }

    [Fact]
    public void CalculateRatingChanges_WinnerGainsAndLoserLoses()
    {
        var ratings = new Dictionary<Guid, int>
        {
            [Player1] = 1200,
            [Player2] = 1200
        };

        var deltas = EloRatingHelper.CalculateRatingChanges(ratings, Player1, isDraw: false, configuredBaseK: 32);

        Assert.True(deltas[Player1] > 0);
        Assert.True(deltas[Player2] < 0);
        Assert.Equal(0, deltas[Player1] + deltas[Player2]);
    }

    [Fact]
    public void CalculateRatingChanges_DrawKeepsTotalRatingStable()
    {
        var ratings = new Dictionary<Guid, int>
        {
            [Player1] = 1200,
            [Player2] = 1200
        };

        var deltas = EloRatingHelper.CalculateRatingChanges(ratings, winnerUserId: null, isDraw: true, configuredBaseK: 32);

        Assert.Equal(0, deltas[Player1]);
        Assert.Equal(0, deltas[Player2]);
    }
}
