using Npgsql;

var connStr = "Host=ep-morning-feather-ao1lnyg0.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true";

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

Console.WriteLine("=== [PRODUCTION] Enum Value Summary ===\n");

// Define enum expected values
var enums = new Dictionary<string, (string col, string[] expected)>
{
    ["Users.Role"] = ("Users", ["Player", "Admin", "CafeStaff", "Manager"]),
    ["Lobbies.Status"] = ("Lobbies", ["Open", "Full", "TimeoutFailed", "HostCancelled", "InProgress", "Closed", "RatingOpen"]),
    ["ActiveSessions.Status"] = ("ActiveSessions", ["Active=0", "Checking=1", "Unpaid=2", "Paid=3"]),
    ["LobbyMembers.Status"] = ("LobbyMembers", ["Joined=0", "Ready=1", "Kicked=2", "Left=3"]),
    ["BookingDeposits.Status"] = ("BookingDeposits", ["Pending=0", "Paid=1", "Released=2", "Refunded=3", "Forfeited=4"]),
    ["Friendships.Status"] = ("Friendships", ["Pending=0", "Accepted=1", "Blocked=2"]),
    ["TournamentParticipants.Status"] = ("TournamentParticipants", ["Registered=0", "Participated=1", "NoShow=2", "DroppedOut=3", "Disqualified=4"]),
    ["Tournaments.Status"] = ("Tournaments", ["RegistrationOpen=0", "RegistrationClosed=1", "InProgress=2", "Completed=3", "Cancelled=4"]),
    ["Tournaments.PairingMode"] = ("Tournaments", ["Swiss=0", "RoundRobin=1", "SingleElimination=2", "DoubleElimination=3"]),
    ["Cafes.BillingModel"] = ("Cafes", ["ByHour=0", "FlatEntry=1"]),
    ["CafePartnerApplications.Status"] = ("CafePartnerApplications", ["PendingApproval", "Approved", "Rejected"]),
    ["CafeGameInventories.Status"] = ("CafeGameInventories", ["Available", "Maintenance"]),
    ["CafeTables.Status"] = ("CafeTables", ["Available", "InUse"]),
};

foreach (var (key, (table, expected)) in enums)
{
    Console.WriteLine($"--- {key} ---");
    Console.WriteLine($"  Expected: {string.Join(", ", expected)}");
    
    try
    {
        var parts = key.Split('.');
        var col = parts[1];
        await using (var c = new NpgsqlCommand($@"
            SELECT DISTINCT ""{col}"" as val, COUNT(*) as cnt
            FROM ""{table}""
            WHERE ""{col}"" IS NOT NULL
            GROUP BY ""{col}""", conn))
        await using (var r = await c.ExecuteReaderAsync())
        {
            var values = new List<string>();
            while (await r.ReadAsync())
            {
                values.Add($"'{r["val"]}' ({r["cnt"]})");
            }
            if (values.Count > 0)
            {
                Console.WriteLine($"  DB values: {string.Join(", ", values)}");
            }
            else
            {
                Console.WriteLine($"  DB values: (empty)");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ERROR: {ex.Message}");
    }
    Console.WriteLine();
}
