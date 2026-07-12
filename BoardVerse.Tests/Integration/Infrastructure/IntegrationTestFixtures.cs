namespace BoardVerse.Tests.Integration.Infrastructure;

/// <summary>
/// Resolved fixture values set during integration test bootstrap.
/// All IDs are unique per test run to avoid conflicts with existing database data.
/// </summary>
public static class IntegrationTestFixtures
{
    public static Guid ManagerUserId { get; internal set; }
    public static Guid AdminUserId { get; internal set; }
    public static Guid DemoCafeId { get; internal set; }
    public static Guid DemoPlayer1UserId { get; internal set; }
    public static Guid DemoPlayer2UserId { get; internal set; }
    public static Guid DemoPlayer3UserId { get; internal set; }
    public static Guid DemoMatchLobbyId { get; internal set; }
    public static Guid DemoKarmaLobbyId { get; internal set; }
    public static Guid DemoPosTableId { get; internal set; }
    public static Guid DemoCatanInventoryId { get; internal set; }
    public static Guid DemoBookingDepositId { get; internal set; }
    public static string CatanBarcode { get; internal set; } = string.Empty;
    public static string PosBoxBarcode { get; internal set; } = string.Empty;

    // Constants that remain the same across all runs
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
    public const string PlayerPassword = "Player@123";
    public const string CafeName = "BoardVerse Demo Cafe";
    public const string CafeAddress = "123 Board Game Street, Ho Chi Minh City";
    public const double CafeLatitude = 10.776889;
    public const double CafeLongitude = 106.700806;

    /// <summary>
    /// Generates unique IDs for this test run. Call once at test session start.
    /// </summary>
    public static void GenerateUniqueIds()
    {
        var prefix = Guid.NewGuid().ToString("N")[..8];
        ManagerUserId = Guid.Parse($"{prefix}-0001-0001-0001-000000000001");
        AdminUserId = Guid.Parse($"{prefix}-0002-0002-0002-000000000002");
        DemoCafeId = Guid.Parse($"{prefix}-0003-0003-0003-000000000003");
        DemoPlayer1UserId = Guid.Parse($"{prefix}-0004-0004-0004-000000000004");
        DemoPlayer2UserId = Guid.Parse($"{prefix}-0005-0005-0005-000000000005");
        DemoPlayer3UserId = Guid.Parse($"{prefix}-0006-0006-0006-000000000006");
        DemoMatchLobbyId = Guid.Parse($"{prefix}-0007-0007-0007-000000000007");
        DemoKarmaLobbyId = Guid.Parse($"{prefix}-0008-0008-0008-000000000008");
        DemoPosTableId = Guid.Parse($"{prefix}-0009-0009-0009-000000000009");
        DemoCatanInventoryId = Guid.Parse($"{prefix}-000A-000A-000A-00000000000A");
        DemoBookingDepositId = Guid.Parse($"{prefix}-000B-000B-000B-00000000000B");
        CatanBarcode = $"BV-TEST-{prefix[..8]}";
    }
}
