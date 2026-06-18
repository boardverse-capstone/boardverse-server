namespace BoardVerse.Core.Data
{
    /// <summary>
    /// Fixed IDs for local/dev seed data. Use these when testing Manager inventory APIs.
    /// </summary>
    public static class DevSeedConstants
    {
        public static readonly Guid ManagerUserId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public static readonly Guid DemoCafeId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public static readonly Guid AdminUserId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
        public static readonly Guid DemoPlayer1UserId = new("dddddddd-dddd-dddd-dddd-dddddddddd01");
        public static readonly Guid DemoPlayer2UserId = new("dddddddd-dddd-dddd-dddd-dddddddddd02");
        public static readonly Guid DemoPlayer3UserId = new("dddddddd-dddd-dddd-dddd-dddddddddd03");
        public static readonly Guid DemoMatchLobbyId = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01");
        public static readonly Guid DemoKarmaLobbyId = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee02");
        public static readonly Guid DemoPosTableId = new("ffffffff-ffff-ffff-ffff-ffffffff0001");
        public static readonly Guid DemoCatanInventoryId = new("ffffffff-ffff-ffff-ffff-ffffffff0101");

        /// <summary>Deterministic POS barcode for <see cref="DemoCatanInventoryId"/> box #1.</summary>
        public static string DemoPosBoxBarcode =>
            $"BV-{DemoCafeId.ToString("N")[..8]}-{DemoCatanInventoryId.ToString("N")[..8]}-001";

        public const string ManagerEmail = "manager@boardverse.dev";
        public const string ManagerPassword = "Manager@123";
        public const string ManagerUsername = "demomanager";
        public const string AdminEmail = "admin@boardverse.dev";
        public const string AdminPassword = "Admin@123";
        public const string AdminUsername = "demoadmin";
        public const string Player1Email = "player1@boardverse.dev";
        public const string Player2Email = "player2@boardverse.dev";
        public const string Player3Email = "player3@boardverse.dev";
        public const string Player1Username = "demoplayer1";
        public const string Player2Username = "demoplayer2";
        public const string Player3Username = "demoplayer3";
        public const string DemoPlayerPassword = "Player@123";
        public const string DemoCafeName = "BoardVerse Demo Cafe";
        public const string DemoCafeAddress = "123 Board Game Street, Ho Chi Minh City";
        public const double DemoCafeLatitude = 10.776889;
        public const double DemoCafeLongitude = 106.700806;
    }
}
