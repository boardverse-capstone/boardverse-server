namespace BoardVerse.Core.Data
{
    /// <summary>
    /// Fixed IDs for local/dev seed data. Use these when testing Manager inventory APIs.
    /// </summary>
    public static class DevSeedConstants
    {
        public static readonly Guid ManagerUserId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public static readonly Guid DemoCafeId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        public const string ManagerEmail = "manager@boardverse.dev";
        public const string ManagerPassword = "Manager@123";
        public const string ManagerUsername = "demomanager";
        public const string DemoCafeName = "BoardVerse Demo Cafe";
        public const string DemoCafeAddress = "123 Board Game Street, Ho Chi Minh City";
        public const double DemoCafeLatitude = 10.776889;
        public const double DemoCafeLongitude = 106.700806;
    }
}
