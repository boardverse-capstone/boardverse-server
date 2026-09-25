using CheckDb;

// ══════════════════════════════════════════════════════════════════════════════
// ⚠️  PRODUCTION SEED SCRIPT RUNNER
// ⚠️  This will seed TEST DATA into PRODUCTION database
// ══════════════════════════════════════════════════════════════════════════════

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("⚠️  BOARDVERSE PRODUCTION SEED SCRIPT");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("⚠️  WARNING: This script will add TEST DATA to PRODUCTION database!");
Console.WriteLine();
Console.WriteLine("Target: ep-morning-feather-ao1lnyg0 (PRODUCTION)");
Console.WriteLine();
Console.WriteLine("Data to be seeded:");
Console.WriteLine("  • 5 test Bookings");
Console.WriteLine("  • 3 test BookingDeposits");
Console.WriteLine("  • 5 test ActiveSessionMembers");
Console.WriteLine("  • 5 test LobbyMembers");
Console.WriteLine("  • 5 test Transactions");
Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();

await SeedProductionData.RunAsync();
