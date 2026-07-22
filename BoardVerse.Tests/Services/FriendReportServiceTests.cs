using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class FriendReportServiceTests
{
    private readonly Mock<IFriendReportRepository> _reportRepo = new();
    private readonly Mock<IFriendshipRepository> _friendshipRepo = new();
    private readonly Mock<IUserManagementRepository> _userRepo = new();
    private FriendReportService CreateService() => new(_reportRepo.Object, _friendshipRepo.Object, _userRepo.Object);

    private static User BuildUser(Guid id, string username = "alice", UserRole role = UserRole.Player)
    {
        return new User
        {
            Id = id,
            Username = username,
            Email = $"{username}@boardverse.test",
            Role = role,
            IsActive = true,
            AccountStatus = UserAccountStatus.Active
        };
    }

    [Fact]
    public async Task SubmitReportAsync_WhenTargetIsSelf_ThrowsBadRequest()
    {
        var meId = Guid.NewGuid();

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.SubmitReportAsync(meId, new CreateFriendReportDto
            {
                TargetUserId = meId,
                Category = "Spam",
                Reason = "Spammy behavior"
            }));
    }

    [Fact]
    public async Task SubmitReportAsync_WhenTargetNotFound_ThrowsNotFound()
    {
        var meId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.SubmitReportAsync(meId, new CreateFriendReportDto
            {
                TargetUserId = targetId,
                Category = "Spam",
                Reason = "Test reason long enough"
            }));
    }

    [Fact]
    public async Task SubmitReportAsync_WhenTargetIsAdmin_ThrowsForbidden()
    {
        var meId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync(BuildUser(targetId, "admin", UserRole.Admin));

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SubmitReportAsync(meId, new CreateFriendReportDto
            {
                TargetUserId = targetId,
                Category = "Spam",
                Reason = "Test reason long enough"
            }));
    }

    [Fact]
    public async Task SubmitReportAsync_WhenNotFriend_ThrowsBadRequest()
    {
        var meId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync(BuildUser(targetId, "bob"));
        _friendshipRepo.Setup(r => r.GetByPairAsync(meId, targetId)).ReturnsAsync((Friendship?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.SubmitReportAsync(meId, new CreateFriendReportDto
            {
                TargetUserId = targetId,
                Category = "Harassment",
                Reason = "Bad behavior"
            }));
    }

    [Fact]
    public async Task SubmitReportAsync_WhenFriendshipNotAccepted_ThrowsBadRequest()
    {
        var meId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync(BuildUser(targetId, "bob"));
        _friendshipRepo.Setup(r => r.GetByPairAsync(meId, targetId))
            .ReturnsAsync(new Friendship { Id = Guid.NewGuid(), RequesterId = meId, AddresseeId = targetId, Status = FriendshipStatus.Pending });

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.SubmitReportAsync(meId, new CreateFriendReportDto
            {
                TargetUserId = targetId,
                Category = "Harassment",
                Reason = "Bad behavior"
            }));
    }

    [Fact]
    public async Task SubmitReportAsync_WhenPendingReportExists_ThrowsConflict()
    {
        var meId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync(BuildUser(targetId, "bob"));
        _friendshipRepo.Setup(r => r.GetByPairAsync(meId, targetId))
            .ReturnsAsync(new Friendship { Id = Guid.NewGuid(), RequesterId = meId, AddresseeId = targetId, Status = FriendshipStatus.Accepted });
        _reportRepo.Setup(r => r.GetPendingByReporterAndTargetAsync(meId, targetId))
            .ReturnsAsync(new FriendReport { Id = Guid.NewGuid() });

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.SubmitReportAsync(meId, new CreateFriendReportDto
            {
                TargetUserId = targetId,
                Category = "Harassment",
                Reason = "Bad behavior"
            }));
    }

    [Fact]
    public async Task SubmitReportAsync_ValidRequest_PersistsWithMappedCategory()
    {
        var meId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var target = BuildUser(targetId, "bob");
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync(target);
        _friendshipRepo.Setup(r => r.GetByPairAsync(meId, targetId))
            .ReturnsAsync(new Friendship { Id = Guid.NewGuid(), RequesterId = meId, AddresseeId = targetId, Status = FriendshipStatus.Accepted });
        _reportRepo.Setup(r => r.GetPendingByReporterAndTargetAsync(meId, targetId)).ReturnsAsync((FriendReport?)null);

        FriendReport? captured = null;
        _reportRepo.Setup(r => r.AddAsync(It.IsAny<FriendReport>()))
            .Callback<FriendReport>(rep => captured = rep)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        var result = await svc.SubmitReportAsync(meId, new CreateFriendReportDto
        {
            TargetUserId = targetId,
            Category = "Harassment",
            Reason = "  Very rude during Catan game  "
        });

        Assert.NotNull(captured);
        Assert.Equal(meId, captured!.ReporterId);
        Assert.Equal(targetId, captured.TargetUserId);
        Assert.Equal(FriendReportCategory.Harassment, captured.Category);
        Assert.Equal("Very rude during Catan game", captured.Reason);
        Assert.Equal("Pending", captured.Status);
        _reportRepo.Verify(r => r.SaveChangesAsync(), Times.Once);

        Assert.Equal(targetId, result.TargetUserId);
        Assert.Equal("bob", result.TargetUsername);
        Assert.Equal("Harassment", result.Category);
        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task SubmitReportAsync_UnknownCategory_FallsBackToOther()
    {
        var meId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync(BuildUser(targetId, "bob"));
        _friendshipRepo.Setup(r => r.GetByPairAsync(meId, targetId))
            .ReturnsAsync(new Friendship { Id = Guid.NewGuid(), RequesterId = meId, AddresseeId = targetId, Status = FriendshipStatus.Accepted });
        _reportRepo.Setup(r => r.GetPendingByReporterAndTargetAsync(meId, targetId)).ReturnsAsync((FriendReport?)null);

        FriendReport? captured = null;
        _reportRepo.Setup(r => r.AddAsync(It.IsAny<FriendReport>()))
            .Callback<FriendReport>(rep => captured = rep)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        await svc.SubmitReportAsync(meId, new CreateFriendReportDto
        {
            TargetUserId = targetId,
            Category = "SomeMadeUpCategory",
            Reason = "Test reason long enough"
        });

        Assert.NotNull(captured);
        Assert.Equal(FriendReportCategory.Other, captured!.Category);
    }

    [Fact]
    public async Task GetMyReportsAsync_WhenEmpty_ReturnsEmpty()
    {
        var meId = Guid.NewGuid();
        _reportRepo.Setup(r => r.GetByReporterAsync(meId)).ReturnsAsync(new List<FriendReport>());

        var svc = CreateService();

        var result = await svc.GetMyReportsAsync(meId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMyReportsAsync_ResolvesTargetUsernames()
    {
        var meId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var reports = new List<FriendReport>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ReporterId = meId,
                TargetUserId = targetId,
                Category = FriendReportCategory.Spam,
                Reason = "Spammy",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            }
        };
        var users = new List<User> { BuildUser(targetId, "bob") };
        _reportRepo.Setup(r => r.GetByReporterAsync(meId)).ReturnsAsync(reports);
        _userRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>())).ReturnsAsync(users);

        var svc = CreateService();

        var result = await svc.GetMyReportsAsync(meId);

        Assert.Single(result);
        Assert.Equal("bob", result[0].TargetUsername);
        Assert.Equal("Spam", result[0].Category);
        Assert.Equal("Pending", result[0].Status);
    }

    [Fact]
    public async Task GetMyReportsAsync_UnknownTarget_UsesUnknownPlaceholder()
    {
        var meId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var reports = new List<FriendReport>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ReporterId = meId,
                TargetUserId = targetId,
                Category = FriendReportCategory.Other,
                Reason = "Mystery",
                Status = "Reviewed",
                CreatedAt = DateTime.UtcNow
            }
        };
        _reportRepo.Setup(r => r.GetByReporterAsync(meId)).ReturnsAsync(reports);
        _userRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>())).ReturnsAsync(new List<User>());

        var svc = CreateService();

        var result = await svc.GetMyReportsAsync(meId);

        Assert.Single(result);
        Assert.Equal("(unknown)", result[0].TargetUsername);
    }
}