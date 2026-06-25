using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.Helpers
{
    public readonly record struct MatchConsensusEvaluation(
        MatchConsensusStatus Status,
        Guid? WinnerUserId,
        bool IsDraw,
        string? ConflictReason);

    public static class MatchConsensusHelper
    {
        public static MatchConsensusEvaluation Evaluate(
            IReadOnlyList<(Guid UserId, MatchOutcome Outcome)> submissions,
            int requiredMemberCount)
        {
            if (submissions.Count < requiredMemberCount)
            {
                return new MatchConsensusEvaluation(
                    MatchConsensusStatus.AwaitingSubmissions,
                    null,
                    false,
                    null);
            }

            var drawCount = submissions.Count(s => s.Outcome == MatchOutcome.Draw);
            if (drawCount == submissions.Count)
            {
                return new MatchConsensusEvaluation(
                    MatchConsensusStatus.Finalized,
                    null,
                    true,
                    null);
            }

            var wins = submissions.Where(s => s.Outcome == MatchOutcome.Win).ToList();
            var losses = submissions.Where(s => s.Outcome == MatchOutcome.Loss).ToList();

            if (wins.Count == 1 && losses.Count == submissions.Count - 1)
            {
                var winnerId = wins[0].UserId;
                var consistent = submissions.All(s =>
                    s.UserId == winnerId
                        ? s.Outcome == MatchOutcome.Win
                        : s.Outcome == MatchOutcome.Loss);

                if (consistent)
                {
                    return new MatchConsensusEvaluation(
                        MatchConsensusStatus.Finalized,
                        winnerId,
                        false,
                        null);
                }
            }

            return new MatchConsensusEvaluation(
                MatchConsensusStatus.Conflict,
                null,
                false,
                ApiErrorMessages.Match.MatchResultsConflict);
        }
    }
}
