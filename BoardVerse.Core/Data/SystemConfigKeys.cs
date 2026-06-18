namespace BoardVerse.Core.Data
{
    public static class SystemConfigKeys
    {
        public const string EloKFactor = "elo_k_factor";
        public const string KarmaPenaltyCancel = "karma_penalty_cancel";
        public const string KarmaPenaltyNoshow = "karma_penalty_noshow";
        public const string MatchmakingRadiusKm = "matchmaking_radius_km";
        public const string MatchmakingEloDiff = "matchmaking_elo_diff";
        public const string PlatformCommissionRate = "platform_commission_rate";

        public const int KarmaSafetyThreshold = 50;

        public static IReadOnlyDictionary<string, (string Value, string Description)> SeedDefaults { get; } =
            new Dictionary<string, (string, string)>
            {
                [EloKFactor] = ("32", "Base K-factor for competitive Elo rating updates."),
                [KarmaPenaltyCancel] = ("-3", "Default karma penalty when a player cancels a deposit late."),
                [KarmaPenaltyNoshow] = ("-5", "Default karma penalty for no-show after scheduled play time."),
                [MatchmakingRadiusKm] = ("15", "Default matchmaking / nearby cafe search radius in kilometers."),
                [MatchmakingEloDiff] = ("200", "Maximum allowed Elo difference between players in matchmaking queue."),
                [PlatformCommissionRate] = ("0.15", "Platform commission rate charged to partner cafes (0-1 decimal).")
            };
    }
}
