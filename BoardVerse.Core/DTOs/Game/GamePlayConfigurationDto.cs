using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Game
{
    public class GamePlayConfigurationDto
    {
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public bool SupportsSoloPlay { get; set; }
        public IReadOnlyList<PlayerPlayMode> AvailablePlayModes { get; set; } = [];
    }
}
