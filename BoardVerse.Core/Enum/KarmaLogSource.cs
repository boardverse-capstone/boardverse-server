namespace BoardVerse.Core.Enum
{
public enum KarmaLogSource
{
    PlayerCrossRating = 0,
    SystemAutomatic = 1,
    AdminManual = 2,

    /// <summary>Tournament bonus/penalty (winner, finalist, no-show).</summary>
    TournamentReward = 3
}
}
