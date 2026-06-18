using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Game
{
    public class GamePlayNavigationResponseDto
    {
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public PlayerPlayMode PlayMode { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public bool SupportsSoloPlay { get; set; }
        public GamePlayNavigationTarget NavigationTarget { get; set; }
        public GamePlayRoomConfigurationDto? RoomConfiguration { get; set; }
    }
}
