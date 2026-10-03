using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using System.Threading;
namespace BoardVerse.Tests.Services;

public class AdminModerationServiceTests
{
    private static readonly Guid AdminId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TargetId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");

    private static AdminModerationService BuildService(
        IAdminModerationRepository repository,
        ICoolingOffService? coolingOff = null) =>
        new AdminModerationService(
            repository,
            coolingOff ?? Mock.Of<ICoolingOffService>(),
            new FakeDbContext(),
            NullLogger<AdminModerationService>.Instance);

    [Fact]
    public async Task PunishUserAsync_Warning_AddsKarmaLogWithoutChangingUser()
    {
        var repo = new Mock<IAdminModerationRepository>();
        var user = BuildTargetUser(karma: 85);
        repo.Setup(r => r.GetUserWithProfileForUpdateAsync(TargetId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var service = BuildService(repo.Object);
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
            l.KarmaBefore == 85), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PunishUserAsync_SuspendWithoutDuration_ThrowsBadRequest()
    {
        var repo = new Mock<IAdminModerationRepository>();
        repo.Setup(r => r.GetUserWithProfileForUpdateAsync(TargetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTargetUser());

        var service = BuildService(repo.Object);

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
        repo.Setup(r => r.GetUserWithProfileForUpdateAsync(TargetId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var service = BuildService(repo.Object);
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
        repo.Setup(r => r.GetUserWithProfileForUpdateAsync(TargetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = TargetId, Email = "admin@test.dev", Username = "admin", Role = UserRole.Admin });

        var service = BuildService(repo.Object);

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
        var profile = new UserProfile { UserId = TargetId, KarmaPoints = 100, GamerTier = GamerTier.Gold };
        repo.Setup(r => r.GetProfileForUpdateAsync(TargetId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);

        var service = BuildService(repo.Object);
        var result = await service.AdjustKarmaAsync(AdminId, TargetId, new AdminAdjustKarmaRequestDto
        {
            Amount = -5,
            Reason = "Manual correction"
        });

        Assert.Equal(95, profile.KarmaPoints);
        Assert.Equal(95, result.NewKarma);
        repo.Verify(r => r.AddKarmaLogAsync(It.Is<KarmaLog>(l =>
            l.IsAdminAdjustment &&
            l.KarmaPointsChange == -5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdjustKarmaAsync_ZeroAmount_ThrowsBadRequest()
    {
        var service = BuildService(new Mock<IAdminModerationRepository>().Object);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.AdjustKarmaAsync(AdminId, TargetId, new AdminAdjustKarmaRequestDto
            {
                Amount = 0,
                Reason = "noop"
            }));
    }

    [Fact]
    public async Task GetCoolingOffUsersAsync_PopulatesSignalsFromCoolingOffService()
    {
        // BR-NEW-10 §XI.1: Cooling-off admin list phải show signals (timeout/cancel/forfeit)
        // cho mỗi user để admin biết lý do user vào cooling-off. Trước đây 3 fields này bị
        // hardcode = 0 trong repository projection — phát hiện khi debug dashboard admin.
        var repo = new Mock<IAdminModerationRepository>();
        var cooling = new Mock<ICoolingOffService>();

        var userAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
        var userBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
        var page = new PaginatedResponse<CoolingOffUserDto>
        {
            Data = new List<CoolingOffUserDto>
            {
                new() { UserId = userAId, Username = "player5", Email = "p5@test.dev",
                        IsCoolingOff = true, CoolingOffExpiresAt = DateTime.UtcNow.AddDays(29) },
                new() { UserId = userBId, Username = "player6", Email = "p6@test.dev",
                        IsCoolingOff = true, CoolingOffExpiresAt = DateTime.UtcNow.AddDays(15) }
            },
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 2, TotalPages = 1 }
        };
        repo.Setup(r => r.GetCoolingOffUsersAsync(It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        // userA: 4 timeout + 0 cancel + 77 forfeit (giống data thật player5).
        cooling.Setup(c => c.DetectSignalsAsync(userAId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((4, 0, 77L));
        // userB: 0 timeout + 2 cancel + 120 forfeit.
        cooling.Setup(c => c.DetectSignalsAsync(userBId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, 2, 120L));

        var service = BuildService(repo.Object, cooling.Object);
        var result = await service.GetCoolingOffUsersAsync(new PaginationParams { PageNumber = 1, PageSize = 20 });

        Assert.Equal(2, result.Data.Count());
        var a = result.Data.Single(u => u.UserId == userAId);
        Assert.Equal(4, a.FailedLobbiesInWeek);
        Assert.Equal(0, a.CancelledLobbiesInWeek);
        Assert.Equal(77L, a.TotalForfeitedBvc);

        var b = result.Data.Single(u => u.UserId == userBId);
        Assert.Equal(0, b.FailedLobbiesInWeek);
        Assert.Equal(2, b.CancelledLobbiesInWeek);
        Assert.Equal(120L, b.TotalForfeitedBvc);

        // Verify cả 2 user đều được tính signals (1 lần / user).
        cooling.Verify(c => c.DetectSignalsAsync(userAId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        cooling.Verify(c => c.DetectSignalsAsync(userBId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCoolingOffUsersAsync_EmptyPage_DoesNotCallSignals()
    {
        var repo = new Mock<IAdminModerationRepository>();
        var cooling = new Mock<ICoolingOffService>();

        repo.Setup(r => r.GetCoolingOffUsersAsync(It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<CoolingOffUserDto>
            {
                Data = new List<CoolingOffUserDto>(),
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 0, TotalPages = 0 }
            });

        var service = BuildService(repo.Object, cooling.Object);
        var result = await service.GetCoolingOffUsersAsync(new PaginationParams());

        Assert.Empty(result.Data);
        cooling.Verify(c => c.DetectSignalsAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCoolingOffUsersAsync_OneUserFails_StillReturnsOthers()
    {
        // Nếu 1 user bị lỗi khi tính signals, vẫn trả kết quả các user khác (resilience).
        var repo = new Mock<IAdminModerationRepository>();
        var cooling = new Mock<ICoolingOffService>();

        var userAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
        var userBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
        var page = new PaginatedResponse<CoolingOffUserDto>
        {
            Data = new List<CoolingOffUserDto>
            {
                new() { UserId = userAId, Username = "player5" },
                new() { UserId = userBId, Username = "player6" }
            },
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 2, TotalPages = 1 }
        };
        repo.Setup(r => r.GetCoolingOffUsersAsync(It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        cooling.Setup(c => c.DetectSignalsAsync(userAId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated DB error"));
        cooling.Setup(c => c.DetectSignalsAsync(userBId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, 0, 10L));

        var service = BuildService(repo.Object, cooling.Object);
        var result = await service.GetCoolingOffUsersAsync(new PaginationParams());

        Assert.Equal(2, result.Data.Count());
        // userA fail → giữ default 0.
        Assert.Equal(0, result.Data.Single(u => u.UserId == userAId).FailedLobbiesInWeek);
        // userB vẫn được enrich.
        Assert.Equal(1, result.Data.Single(u => u.UserId == userBId).FailedLobbiesInWeek);
        Assert.Equal(10L, result.Data.Single(u => u.UserId == userBId).TotalForfeitedBvc);
    }

    private static User BuildTargetUser(int karma = 100) => new()
    {
        Id = TargetId,
        Email = "player@test.dev",
        Username = "player",
        Role = UserRole.Player,
        Profile = new UserProfile { UserId = TargetId, KarmaPoints = karma, GamerTier = KarmaRatingHelper.ResolveTier(karma) }
    };
}
