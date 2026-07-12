using BoardVerse.Data;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.Infrastructure;

internal static class PaymentTestSeed
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

            var orderId = "WEB001";
            var testDepositId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            // First, delete any existing deposit with this OrderId, testDepositId, or empty OrderId (idempotent)
            await db.Database.ExecuteSqlRawAsync($@"
                DELETE FROM ""BookingDeposits"" 
                WHERE ""OrderId"" = '{orderId}' 
                   OR ""Id"" = '{testDepositId}'
                   OR (""OrderId"" IS NULL OR ""OrderId"" = '')");

            // Check if the referenced entities exist
            var cafeExists = await db.Cafes.AnyAsync(c => c.Id == DevSeedConstants.DemoCafeId);
            var managerExists = await db.Users.AnyAsync(u => u.Id == DevSeedConstants.ManagerUserId);

            if (!cafeExists || !managerExists)
            {
                // Skip if required entities don't exist (test environment with unique IDs)
                return;
            }

            var now = DateTime.UtcNow;

            // Use raw INSERT to avoid EF tracking issues
            await db.Database.ExecuteSqlRawAsync($@"
                INSERT INTO ""BookingDeposits""
                (""Id"", ""ActiveSessionId"", ""Amount"", ""CafeId"", ""CafeManagerId"",
                 ""CreatedAt"", ""ForfeitedAt"", ""MasterAccountId"", ""OrderId"", ""PaidAt"",
                 ""RefundPolicy"", ""RefundedAt"", ""ReleasedAt"", ""ScheduledAt"",
                 ""SePayTransactionId"", ""SePayTransferId"", ""Status"", ""TransferContent"", ""UpdatedAt"")
                VALUES
                ('{testDepositId}', '22222222-2222-2222-2222-222222222222', 50000,
                 '{DevSeedConstants.DemoCafeId}', '{DevSeedConstants.ManagerUserId}',
                 '{now:O}', NULL, NULL, '{orderId}', NULL,
                 {(int)DepositRefundPolicy.Full}, NULL, NULL, NULL,
                 '{orderId}', NULL, {(int)BookingDepositStatus.Pending}, '{orderId}', '{now:O}')");
        }
        catch (Exception)
        {
            // Ignore seeding failures - tests will handle their own data
        }
    }
}
