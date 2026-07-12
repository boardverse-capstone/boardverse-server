using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class KarmaRatingHelperTests
{
    [Theory]
    [InlineData(KarmaRatingTag.Friendly, KarmaRatingTag.OnTime, 1.0)]
    [InlineData(KarmaRatingTag.Toxic, KarmaRatingTag.NoShow, -4.0)]
    [InlineData(KarmaRatingTag.Friendly, KarmaRatingTag.Toxic, -1.5)]
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
        Assert.Equal(0.5m, delta);
    }

    [Theory]
    [InlineData(100, 1.0, 101)]
    [InlineData(100, -1.5, 99)]
    [InlineData(1, -5.0, 0)]
    public void ApplyDeltaToKarmaPoints_RoundsAndFloorsAtZero(
        int current,
        decimal delta,
        int expected)
    {
        var result = KarmaRatingHelper.ApplyDeltaToKarmaPoints(current, delta);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(40, GamerTier.Bronze)]
    [InlineData(150, GamerTier.Silver)]
    [InlineData(300, GamerTier.Gold)]
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
