using Npgsql;

var prodConn = "Host=ep-morning-feather-ao1lnyg0.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true";
var testConn = "Host=ep-morning-darkness-aof95ckg.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true";

async Task CheckColumns(string name, string connStr, string table, string[] expectedColumns)
{
    Console.WriteLine($"\n{'=',60}");
    Console.WriteLine($"| {name}");
    Console.WriteLine($"| Table: {table}");
    Console.WriteLine($"{'=',60}");
    
    try
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        
        await using var cmd = new NpgsqlCommand($@"
            SELECT column_name, data_type, character_maximum_length
            FROM information_schema.columns 
            WHERE table_name = '{table}' AND table_schema = 'public'
            ORDER BY ordinal_position;", conn);
        
        var dbCols = new List<(string name, string type, int? len)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var colName = reader.GetString(0);
                var dataType = reader.GetString(1);
                int? len = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                dbCols.Add((colName, dataType, len));
            }
        }
        
        Console.WriteLine($"\nAll columns in DB:");
        foreach (var (colName, dataType, len) in dbCols)
        {
            var lenStr = len.HasValue ? $"({len})" : "";
            Console.WriteLine($"  {colName,-30} {dataType}{lenStr}");
        }
        
        Console.WriteLine($"\nChecking expected columns:");
        var missing = expectedColumns.Where(e => !dbCols.Any(d => d.name.Equals(e, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Any())
        {
            Console.WriteLine($"  ❌ MISSING: {string.Join(", ", missing)}");
        }
        else
        {
            Console.WriteLine($"  ✅ All expected columns exist!");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ ERROR: {ex.Message}");
    }
}

await CheckColumns("PRODUCTION", prodConn, "Friendships", 
    new[] { "Id", "RequesterId", "AddresseeId", "Status", "Message", "AcceptedAt", "UpdatedAt", "CreatedAt", "AddresseeReadAt" });

await CheckColumns("PRODUCTION", prodConn, "FriendNotes", 
    new[] { "Id", "OwnerUserId", "FriendUserId", "Alias", "Note", "Tags", "CreatedAt", "UpdatedAt" });

await CheckColumns("PRODUCTION", prodConn, "FriendReports", 
    new[] { "Id", "ReporterId", "TargetUserId", "Category", "Reason", "Status", "ReviewedByAdminId", "AdminNote", "CreatedAt", "ReviewedAt" });

await CheckColumns("PRODUCTION", prodConn, "LobbyInvites", 
    new[] { "Id", "LobbyId", "InviterId", "InviteeId", "Status", "ExpiresAt", "RespondedAt", "Message", "CreatedAt" });

await CheckColumns("PRODUCTION", prodConn, "LobbyMessages", 
    new[] { "Id", "LobbyId", "SenderId", "Content", "IsSystem", "CreatedAt" });

await CheckColumns("PRODUCTION", prodConn, "UserProfiles", 
    new[] { "AcceptFriendRequestsFrom", "FriendLimit", "IsFriendListPublic", "LastActiveAt" });

Console.WriteLine($"\n{'=',60}");
Console.WriteLine("DONE - TESTING");

await CheckColumns("TESTING", testConn, "Friendships", 
    new[] { "Id", "RequesterId", "AddresseeId", "Status", "Message", "AcceptedAt", "UpdatedAt", "CreatedAt", "AddresseeReadAt" });

await CheckColumns("TESTING", testConn, "FriendNotes", 
    new[] { "Id", "OwnerUserId", "FriendUserId", "Alias", "Note", "Tags", "CreatedAt", "UpdatedAt" });

await CheckColumns("TESTING", testConn, "FriendReports", 
    new[] { "Id", "ReporterId", "TargetUserId", "Category", "Reason", "Status", "ReviewedByAdminId", "AdminNote", "CreatedAt", "ReviewedAt" });

await CheckColumns("TESTING", testConn, "LobbyInvites", 
    new[] { "Id", "LobbyId", "InviterId", "InviteeId", "Status", "ExpiresAt", "RespondedAt", "Message", "CreatedAt" });

await CheckColumns("TESTING", testConn, "LobbyMessages", 
    new[] { "Id", "LobbyId", "SenderId", "Content", "IsSystem", "CreatedAt" });

await CheckColumns("TESTING", testConn, "UserProfiles", 
    new[] { "AcceptFriendRequestsFrom", "FriendLimit", "IsFriendListPublic", "LastActiveAt" });

Console.WriteLine($"\n{'=',60}");
Console.WriteLine("DONE");
