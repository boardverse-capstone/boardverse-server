using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class KarmaRatingHelperTests
{
    [Theory]
    [InlineData(KarmaRatingTag.Friendly, KarmaRatingTag.OnTime, 0.2)]
    [InlineData(KarmaRatingTag.Toxic, KarmaRatingTag.NoShow, -2.0)]
    [InlineData(KarmaRatingTag.Friendly, KarmaRatingTag.Toxic, -0.9)]
    public void CalculateDelta_SumsDistinctTagWeights(
        KarmaRatingTag first,
        KarmaRatingTag second,
        decimal expected)
    {
        var delta = KarmaRatingHelper.CalculateDelta([first, second]);
        Assert.Equal(expected, delta);
    }

    [Fact]
    public void CalculateDelta_IgnoresDuplicateTags()
    {
        var delta = KarmaRatingHelper.CalculateDelta([KarmaRatingTag.Friendly, KarmaRatingTag.Friendly]);
        Assert.Equal(0.1m, delta);
    }

    [Theory]
    [InlineData(100, 0.5, 100)] // cap tại 100
    [InlineData(100, -0.3, 100)]
    [InlineData(95, 0.5, 96)]
    [InlineData(1, -5.0, 0)] // floor tại 0
    [InlineData(50, 0.0, 50)]
    public void ApplyDeltaToKarmaPoints_ClampsBetween0And100(
        int current,
        decimal delta,
        int expected)
    {
        var result = KarmaRatingHelper.ApplyDeltaToKarmaPoints(current, delta);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(40, GamerTier.Bronze)]
    [InlineData(69, GamerTier.Bronze)]
    [InlineData(70, GamerTier.Silver)]
    [InlineData(89, GamerTier.Silver)]
    [InlineData(90, GamerTier.Gold)]
    [InlineData(100, GamerTier.Gold)]
    public void ResolveTier_MapsKarmaThresholds(int karma, GamerTier expectedTier)
    {
        Assert.Equal(expectedTier, KarmaRatingHelper.ResolveTier(karma));
    }

    [Theory]
    [InlineData(LobbyStatus.InProgress, true)]
    [InlineData(LobbyStatus.Closed, true)]
    [InlineData(LobbyStatus.RatingOpen, false)]
    public void IsRatingAllowed_OnlyWhenRatingOpenOrClosed(LobbyStatus status, bool allowed)
    {
        Assert.Equal(allowed, KarmaRatingHelper.IsRatingAllowed(status));
    }
}
