namespace BoardVerse.Core.DTOs.Discovery;

/// <summary>
/// User's inferred preferences extracted from saved games.
/// Used for personalization in discovery algorithms.
/// </summary>
public class UserGamePreference
{
    /// <summary>
    /// User ID.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Top 3 most saved categories.
    /// </summary>
    public List<Guid> TopCategoryIds { get; set; } = [];

    /// <summary>
    /// Average BGG weight of saved games (1.0 - 5.0).
    /// Null if no saved games have weight data.
    /// </summary>
    public double? AverageWeight { get; set; }

    /// <summary>
    /// Average playtime in minutes of saved games.
    /// </summary>
    public double AverageDuration { get; set; }

    /// <summary>
    /// Total number of saved games.
    /// </summary>
    public int SavedGameCount { get; set; }
}
