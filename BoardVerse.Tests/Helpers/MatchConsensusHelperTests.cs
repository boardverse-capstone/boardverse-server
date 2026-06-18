using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class MatchConsensusHelperTests
{
    private static readonly Guid Player1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Player2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Evaluate_WhenSubmissionsIncomplete_ReturnsAwaiting()
    {
        var result = MatchConsensusHelper.Evaluate([(Player1, MatchOutcome.Win)], requiredMemberCount: 2);

        Assert.Equal(MatchConsensusStatus.AwaitingSubmissions, result.Status);
        Assert.Null(result.WinnerUserId);
    }

    [Fact]
    public void Evaluate_WhenBothReportDraw_ReturnsFinalizedDraw()
    {
        var result = MatchConsensusHelper.Evaluate(
            [(Player1, MatchOutcome.Draw), (Player2, MatchOutcome.Draw)],
            requiredMemberCount: 2);

        Assert.Equal(MatchConsensusStatus.Finalized, result.Status);
        Assert.True(result.IsDraw);
        Assert.Null(result.WinnerUserId);
    }

    [Fact]
    public void Evaluate_WhenOneWinOneLoss_ReturnsFinalizedWithWinner()
    {
        var result = MatchConsensusHelper.Evaluate(
            [(Player1, MatchOutcome.Win), (Player2, MatchOutcome.Loss)],
            requiredMemberCount: 2);

        Assert.Equal(MatchConsensusStatus.Finalized, result.Status);
        Assert.Equal(Player1, result.WinnerUserId);
        Assert.False(result.IsDraw);
    }

    [Fact]
    public void Evaluate_WhenBothClaimWin_ReturnsConflict()
    {
        var result = MatchConsensusHelper.Evaluate(
            [(Player1, MatchOutcome.Win), (Player2, MatchOutcome.Win)],
            requiredMemberCount: 2);

        Assert.Equal(MatchConsensusStatus.Conflict, result.Status);
        Assert.NotNull(result.ConflictReason);
    }
}
