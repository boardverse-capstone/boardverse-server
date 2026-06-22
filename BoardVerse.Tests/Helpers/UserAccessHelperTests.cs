using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class UserAccessHelperTests
{
    [Fact]
    public void TryClearExpiredSuspension_ClearsExpiredLockout()
    {
        var utcNow = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);
        var user = CreateUser(
            UserAccountStatus.Suspended,
            lockoutEnd: utcNow.AddMinutes(-1));

        var cleared = UserAccessHelper.TryClearExpiredSuspension(user, utcNow);

        Assert.True(cleared);
        Assert.Equal(UserAccountStatus.Active, user.AccountStatus);
        Assert.Null(user.LockoutEndDate);
        Assert.Null(user.BlockReason);
    }

    [Fact]
    public void IsAccessRestricted_BannedUserIsBlocked()
    {
        var user = CreateUser(UserAccountStatus.Banned, blockReason: "Cheating");

        var restricted = UserAccessHelper.IsAccessRestricted(user, DateTime.UtcNow, out var message);

        Assert.True(restricted);
        Assert.Contains("banned", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cheating", message);
    }

    [Fact]
    public void IsAccessRestricted_ActiveUserIsAllowed()
    {
        var user = CreateUser();

        var restricted = UserAccessHelper.IsAccessRestricted(user, DateTime.UtcNow, out var message);

        Assert.False(restricted);
        Assert.Empty(message);
    }

    [Fact]
    public void IsAccessRestricted_DeactivatedUserIsBlocked()
    {
        var user = CreateUser(isActive: false);

        var restricted = UserAccessHelper.IsAccessRestricted(user, DateTime.UtcNow, out var message);

        Assert.True(restricted);
        Assert.Contains("deactivated", message, StringComparison.OrdinalIgnoreCase);
    }

    private static User CreateUser(
        UserAccountStatus status = UserAccountStatus.Active,
        bool isActive = true,
        DateTime? lockoutEnd = null,
        string? blockReason = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            Email = "test@boardverse.dev",
            Role = UserRole.Player,
            AccountStatus = status,
            IsActive = isActive,
            LockoutEndDate = lockoutEnd,
            BlockReason = blockReason
        };
}
