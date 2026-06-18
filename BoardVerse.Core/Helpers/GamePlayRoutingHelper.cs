using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
    public static class GamePlayRoutingHelper
    {
        public static bool SupportsSoloPlay(int minPlayers) => minPlayers == 1;

        public static IReadOnlyList<PlayerPlayMode> GetAvailablePlayModes(int minPlayers)
        {
            if (SupportsSoloPlay(minPlayers))
            {
                return [PlayerPlayMode.Solo, PlayerPlayMode.Group];
            }

            return [PlayerPlayMode.Group];
        }

        public static GamePlayNavigationTarget ResolveNavigationTarget(int minPlayers, PlayerPlayMode playMode)
        {
            if (playMode == PlayerPlayMode.Solo)
            {
                return GamePlayNavigationTarget.SoloBooking;
            }

            return GamePlayNavigationTarget.LobbyCreation;
        }

        public static GamePlayRoomConfigurationDto BuildRoomConfiguration(
            int minPlayers,
            int maxPlayers,
            GamePlayNavigationTarget navigationTarget)
        {
            if (navigationTarget == GamePlayNavigationTarget.SoloBooking)
            {
                return new GamePlayRoomConfigurationDto
                {
                    MinPlayers = 1,
                    MaxPlayers = 1,
                    DefaultPlayerCount = 1
                };
            }

            return new GamePlayRoomConfigurationDto
            {
                MinPlayers = minPlayers,
                MaxPlayers = maxPlayers,
                DefaultPlayerCount = minPlayers
            };
        }
    }
}
