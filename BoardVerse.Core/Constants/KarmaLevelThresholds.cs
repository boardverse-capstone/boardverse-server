namespace BoardVerse.Core.Constants;

/// <summary>
/// GAP-R6-KARMA-08 Fix: Centralized karma level thresholds.
/// Tránh magic number trong KarmaService/KarmaRatingService. Threshold theo BR-KARMA-02.
/// </summary>
public static class KarmaLevelThresholds
{
    /// <summary>≥ Excellent threshold → KarmaLevel.Excellent.</summary>
    public const int Excellent = 90;

    /// <summary>≥ Good threshold → KarmaLevel.Good.</summary>
    public const int Good = 70;

    /// <summary>≥ Average threshold → KarmaLevel.Average.</summary>
    public const int Average = 50;

    /// <summary>≥ Low threshold → KarmaLevel.Low.</summary>
    public const int Low = 30;

    /// <summary>≥ Poor threshold → KarmaLevel.Poor.</summary>
    public const int Poor = 10;

    /// <summary>Default karma points cho user mới (BR-LOBBY default).</summary>
    public const int Default = 100;

    /// <summary>BR-KARMA-03: minimum scheduled minutes khi restricted.</summary>
    public const int RestrictedMinimumMinutes = 240;
}
