using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Match
{
    public class SubmitMatchResultResponseDto
    {
        public Guid LobbyId { get; set; }
        public MatchConsensusStatus ConsensusStatus { get; set; }
        public int SubmittedCount { get; set; }
        public int RequiredCount { get; set; }
        public string? ConflictReason { get; set; }
        public Guid? MatchHistoryId { get; set; }
        public IReadOnlyList<MatchEloUpdateDto> EloUpdates { get; set; } = [];
    }

    public class MatchEloUpdateDto
    {
        public Guid UserId { get; set; }
        public MatchOutcome ReportedOutcome { get; set; }
        public int EloBefore { get; set; }
        public int EloAfter { get; set; }
        public int EloDelta { get; set; }
    }
}
