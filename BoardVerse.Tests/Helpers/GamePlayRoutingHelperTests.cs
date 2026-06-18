using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class GamePlayRoutingHelperTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void SupportsSoloPlay_OnlyWhenMinPlayersIsOne(int minPlayers, bool expected)
    {
        Assert.Equal(expected, GamePlayRoutingHelper.SupportsSoloPlay(minPlayers));
    }

    [Fact]
    public void GetAvailablePlayModes_SoloGameIncludesSoloAndGroup()
    {
        var modes = GamePlayRoutingHelper.GetAvailablePlayModes(minPlayers: 1);

        Assert.Contains(PlayerPlayMode.Solo, modes);
        Assert.Contains(PlayerPlayMode.Group, modes);
    }

    [Fact]
    public void GetAvailablePlayModes_MultiplayerGameIsGroupOnly()
    {
        var modes = GamePlayRoutingHelper.GetAvailablePlayModes(minPlayers: 3);

        Assert.Single(modes);
        Assert.Equal(PlayerPlayMode.Group, modes[0]);
    }

    [Fact]
    public void BuildRoomConfiguration_SoloBookingForcesSinglePlayerRoom()
    {
        var room = GamePlayRoutingHelper.BuildRoomConfiguration(
            minPlayers: 1,
            maxPlayers: 4,
            GamePlayNavigationTarget.SoloBooking);

        Assert.Equal(1, room.MinPlayers);
        Assert.Equal(1, room.MaxPlayers);
        Assert.Equal(1, room.DefaultPlayerCount);
    }

    [Fact]
    public void ResolveNavigationTarget_SoloModeRoutesToSoloBooking()
    {
        var target = GamePlayRoutingHelper.ResolveNavigationTarget(minPlayers: 1, PlayerPlayMode.Solo);
        Assert.Equal(GamePlayNavigationTarget.SoloBooking, target);
    }
}
