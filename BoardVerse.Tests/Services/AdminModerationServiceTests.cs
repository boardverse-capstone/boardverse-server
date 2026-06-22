using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class AdminModerationServiceTests
{
    private static readonly Guid AdminId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TargetId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");

    [Fact]
    public async Task PunishUserAsync_Warning_AddsKarmaLogWithoutChangingUser()
    {
        var repo = new Mock<IAdminModerationRepository>();
        var user = BuildTargetUser(karma: 85);
        repo.Setup(r => r.GetUserWithProfileForUpdateAsync(TargetId)).ReturnsAsync(user);

        var service = new AdminModerationService(repo.Object);
        var result = await service.PunishUserAsync(AdminId, TargetId, new AdminPunishUserRequestDto
        {
            ActionType = AdminPunishmentActionType.Warning,
            Reason = "  Be nicer  "
        });

        Assert.Equal("Warning", result.ActionType);
        Assert.Equal("Be nicer", result.Reason);
        repo.Verify(r => r.AddKarmaLogAsync(It.Is<KarmaLog>(l =>
            l.ViolationCategory == KarmaViolationCategory.AdminWarning &&
            l.KarmaPointsChange == 0 &&
            l.KarmaBefore == 85)), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task PunishUserAsync_SuspendWithoutDuration_ThrowsBadRequest()
    {
        var repo = new Mock<IAdminModerationRepository>();
        repo.Setup(r => r.GetUserWithProfileForUpdateAsync(TargetId))
            .ReturnsAsync(BuildTargetUser());

        var service = new AdminModerationService(repo.Object);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.PunishUserAsync(AdminId, TargetId, new AdminPunishUserRequestDto
            {
                ActionType = AdminPunishmentActionType.Suspend,
                Reason = "Toxic chat"
            }));
    }

    [Fact]
    public async Task PunishUserAsync_Suspend_SetsLockout()
    {
        var repo = new Mock<IAdminModerationRepository>();
        var user = BuildTargetUser();
        repo.Setup(r => r.GetUserWithProfileForUpdateAsync(TargetId)).ReturnsAsync(user);

        var service = new AdminModerationService(repo.Object);
        var result = await service.PunishUserAsync(AdminId, TargetId, new AdminPunishUserRequestDto
        {
            ActionType = AdminPunishmentActionType.Suspend,
            DurationDays = 7,
            Reason = "Repeat offender"
        });

        Assert.Equal(UserAccountStatus.Suspended, user.AccountStatus);
        Assert.NotNull(user.LockoutEndDate);
        Assert.Equal("Suspended", result.AccountStatus);
    }

    [Fact]
    public async Task PunishUserAsync_CannotPunishAdmin_ThrowsForbidden()
    {
        var repo = new Mock<IAdminModerationRepository>();
        repo.Setup(r => r.GetUserWithProfileForUpdateAsync(TargetId))
            .ReturnsAsync(new User { Id = TargetId, Email = "admin@test.dev", Username = "admin", Role = UserRole.Admin });

        var service = new AdminModerationService(repo.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.PunishUserAsync(AdminId, TargetId, new AdminPunishUserRequestDto
            {
                ActionType = AdminPunishmentActionType.Warning,
                Reason = "Test"
            }));
    }

    [Fact]
    public async Task AdjustKarmaAsync_AppliesDeltaAndLogs()
    {
        var repo = new Mock<IAdminModerationRepository>();
        var profile = new UserProfile { UserId = TargetId, KarmaPoints = 100, GamerTier = GamerTier.Bronze };
        repo.Setup(r => r.GetProfileForUpdateAsync(TargetId)).ReturnsAsync(profile);

        var service = new AdminModerationService(repo.Object);
        var result = await service.AdjustKarmaAsync(AdminId, TargetId, new AdminAdjustKarmaRequestDto
        {
            Amount = -5,
            Reason = "Manual correction"
        });

        Assert.Equal(95, profile.KarmaPoints);
        Assert.Equal(95, result.NewKarma);
        repo.Verify(r => r.AddKarmaLogAsync(It.Is<KarmaLog>(l =>
            l.IsAdminAdjustment &&
            l.KarmaPointsChange == -5)), Times.Once);
    }

    [Fact]
    public async Task AdjustKarmaAsync_ZeroAmount_ThrowsBadRequest()
    {
        var service = new AdminModerationService(new Mock<IAdminModerationRepository>().Object);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.AdjustKarmaAsync(AdminId, TargetId, new AdminAdjustKarmaRequestDto
            {
                Amount = 0,
                Reason = "noop"
            }));
    }

    private static User BuildTargetUser(int karma = 100) => new()
    {
        Id = TargetId,
        Email = "player@test.dev",
        Username = "player",
        Role = UserRole.Player,
        Profile = new UserProfile { UserId = TargetId, KarmaPoints = karma, GamerTier = GamerTier.Bronze }
    };
}
