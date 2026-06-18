using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Match
{
    public class MatchResultStatusDto
    {
        public Guid LobbyId { get; set; }
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public bool SupportsMatchResults { get; set; }
        public MatchConsensusStatus ConsensusStatus { get; set; }
        public int SubmittedCount { get; set; }
        public int RequiredCount { get; set; }
        public string? ConflictReason { get; set; }
        public Guid? MatchHistoryId { get; set; }
        public Guid? WinnerUserId { get; set; }
        public bool? IsDraw { get; set; }
        public IReadOnlyList<MatchOutcomeOptionDto> AvailableOutcomes { get; set; } = [];
        public IReadOnlyList<MatchMemberSubmissionDto> Submissions { get; set; } = [];
    }

    public class MatchOutcomeOptionDto
    {
        public MatchOutcome Outcome { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    public class MatchMemberSubmissionDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public MatchOutcome? Outcome { get; set; }
        public bool IsCurrentUser { get; set; }
    }
}
