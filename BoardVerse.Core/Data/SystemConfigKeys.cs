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
        public const string BypassTimeWindowValidations = "bypass_time_window_validations";

        // Demo Mode: Toggle on để nới lỏng ràng buộc BR-USER-LIMIT-01/04/05 + BR-LOBBY-01a/b +
        // BR-NEW-05 + BR-CHECKIN-01 khi demo happy case. Mặc định false.
        // CHỈ bật trên Neon testing branch (`br-sparkling-salad-aota3n5d`), KHÔNG bật production.
        // BR-DEMO-01: ràng buộc user limit (1 host + 1 member + 2 tổng + cross-role).
        // BR-DEMO-02: buffer (recruitmentDeadline - now) - bỏ qua check ≥ 60/120 phút.
        // BR-DEMO-03: max 5 lần tạo/hủy / playDate (BR-NEW-05).
        // BR-DEMO-04: early grace check-in 15 phút (BR-CHECKIN-01).
        public const string DemoLoosenLobbyConstraints = "demo_loosen_lobby_constraints";

        public const int KarmaSafetyThreshold = 50;

        public static IReadOnlyDictionary<string, (string Value, string Description)> SeedDefaults { get; } =
            new Dictionary<string, (string, string)>
            {
                [EloKFactor] = ("32", "Base K-factor for competitive Elo rating updates."),
                [KarmaPenaltyCancel] = ("-3", "Default karma penalty when a player cancels a deposit late."),
                [KarmaPenaltyNoshow] = ("-5", "Default karma penalty for no-show after scheduled play time."),
                [MatchmakingRadiusKm] = ("15", "Default matchmaking / nearby cafe search radius in kilometers."),
                [MatchmakingEloDiff] = ("200", "Maximum allowed Elo difference between players in matchmaking queue."),
                [PlatformCommissionRate] = ("0.15", "Platform commission rate charged to partner cafes (0-1 decimal)."),
                [BypassTimeWindowValidations] = ("false", "Dev bypass for time-window checks (check-in window, lobby deadline, refund milestones, no-show grace, walk-in window, tournament start). Toggle via admin endpoint POST/DELETE /api/v1/admin/system-config/bypass-time-window or per-request via header X-Bypass-Time-Window. Production default = false."),
                [DemoLoosenLobbyConstraints] = ("false", "Demo mode: relax BR-USER-LIMIT-01/04/05, BR-LOBBY-01a/b (buffer 60/120 phút), BR-NEW-05 (max 5 tạo/hủy), BR-CHECKIN-01 (early grace 15 phút). Chỉ bật trên Neon testing branch, không bật production.")
            };
    }
}
