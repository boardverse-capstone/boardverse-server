using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class UserProfileServiceTests
{
    private readonly Mock<IUserProfileRepository> _profileRepo = new();
    private UserProfileService CreateService() => new(_profileRepo.Object);

    private static User BuildUser(Guid id, string username = "alice", UserRole role = UserRole.Player, UserProfile? profile = null)
    {
        var u = new User
        {
            Id = id,
            Username = username,
            Email = $"{username}@boardverse.test",
            Role = role,
            IsActive = true,
            AccountStatus = UserAccountStatus.Active
        };
        u.Profile = profile ?? new UserProfile
        {
            UserId = id,
            KarmaPoints = 100,
            GamerTier = GamerTier.Gold,
            GlobalElo = 1200,
            Level = 1,
            IsActive = true
        };
        return u;
    }

    #region GetPublicProfileAsync

    [Fact]
    public async Task GetPublicProfileAsync_WhenUserMissing_ThrowsUserNotFound()
    {
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<UserNotFoundException>(() => svc.GetPublicProfileAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetPublicProfileAsync_WhenProfileActive_ReturnsMapped()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId, profile: new UserProfile
        {
            UserId = userId,
            Bio = "Cat lover",
            AvatarUrl = "avatar.png",
            KarmaPoints = 80,
            GamerTier = GamerTier.Silver,
            GlobalElo = 1400,
            Level = 5,
            IsActive = true
        });
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        var result = await svc.GetPublicProfileAsync(userId);

        Assert.Equal(userId, result.UserId);
        Assert.Equal("Cat lover", result.Bio);
        Assert.Equal(80, result.KarmaPoints);
        Assert.Equal(GamerTier.Silver.ToString(), result.GamerTier);
        Assert.True(result.HasProfile);
    }

    [Fact]
    public async Task GetPublicProfileAsync_WhenProfileInactive_ReturnsDefaults()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId, role: UserRole.Admin);
        user.Profile!.IsActive = false;
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        var result = await svc.GetPublicProfileAsync(userId);

        Assert.Null(result.Bio);
        Assert.Equal(100, result.KarmaPoints);
        Assert.True(result.HasProfile); // Admin always has profile
    }

    #endregion

    #region GetInternalProfileAsync

    [Fact]
    public async Task GetInternalProfileAsync_WhenProfileDisabled_ThrowsProfileDisabled()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId);
        user.Profile!.IsActive = false;
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        await Assert.ThrowsAsync<ProfileDisabledException>(() => svc.GetInternalProfileAsync(userId));
    }

    [Fact]
    public async Task GetInternalProfileAsync_WhenValid_ReturnsFull()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId);
        user.Profile!.FirstName = "An";
        user.Profile.LastName = "Nguyen";
        user.Profile.DateOfBirth = new DateOnly(2000, 1, 1);
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        var result = await svc.GetInternalProfileAsync(userId);

        Assert.Equal("An", result.FirstName);
        Assert.Equal("Nguyen", result.LastName);
        Assert.Equal(new DateOnly(2000, 1, 1), result.DateOfBirth);
    }

    #endregion

    #region CreateProfileAsync

    [Fact]
    public async Task CreateProfileAsync_WhenUserMissing_ThrowsUserNotFound()
    {
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<UserNotFoundException>(() =>
            svc.CreateProfileAsync(Guid.NewGuid(), new ProfileCreateDto { Bio = "Hi" }));
    }

    [Fact]
    public async Task CreateProfileAsync_WhenActiveProfileExists_ThrowsProfileAlreadyExists()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId);
        user.Profile!.IsActive = true;
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        await Assert.ThrowsAsync<ProfileAlreadyExistsException>(() =>
            svc.CreateProfileAsync(userId, new ProfileCreateDto()));
    }

    [Fact]
    public async Task CreateProfileAsync_WhenNoProfile_CreatesNew()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Username = "alice", Email = "a@b.test", Role = UserRole.Player, IsActive = true, AccountStatus = UserAccountStatus.Active };
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        UserProfile? captured = null;
        _profileRepo.Setup(r => r.AddUserProfileAsync(It.IsAny<UserProfile>()))
            .Callback<UserProfile>(p => captured = p)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        await svc.CreateProfileAsync(userId, new ProfileCreateDto { Bio = "Hello", FirstName = "An" });

        Assert.NotNull(captured);
        Assert.Equal("Hello", captured!.Bio);
        Assert.Equal("An", captured.FirstName);
        Assert.True(captured.IsActive);
        Assert.Equal(100, captured.KarmaPoints);
        _profileRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region UpdateProfileAsync

    [Fact]
    public async Task UpdateProfileAsync_WhenUserMissing_Throws()
    {
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<UserNotFoundException>(() =>
            svc.UpdateProfileAsync(Guid.NewGuid(), new ProfileUpdateDto()));
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenProfileExists_UpdatesFields()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId);
        user.Profile!.KarmaPoints = 200;
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        await svc.UpdateProfileAsync(userId, new ProfileUpdateDto { Bio = "New bio", FirstName = "Bob" });

        Assert.Equal("New bio", user.Profile!.Bio);
        Assert.Equal("Bob", user.Profile.FirstName);
        Assert.Equal(200, user.Profile.KarmaPoints); // unchanged
        _profileRepo.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdateProfileAsync_WithZeroKarma_ResetsTo100()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId);
        user.Profile!.KarmaPoints = 0;
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        await svc.UpdateProfileAsync(userId, new ProfileUpdateDto { Bio = "Try" });

        Assert.Equal(100, user.Profile!.KarmaPoints);
    }

    #endregion

    #region UpdateProgressAsync

    [Fact]
    public async Task UpdateProgressAsync_SetsEloAndLevel()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId);
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        await svc.UpdateProgressAsync(userId, new ProfileProgressUpdateDto { GlobalElo = 1600, Level = 8 });

        Assert.Equal(1600, user.Profile!.GlobalElo);
        Assert.Equal(8, user.Profile.Level);
    }

    #endregion

    #region DeleteProfileAsync

    [Fact]
    public async Task DeleteProfileAsync_WhenNoProfile_NoOp()
    {
        _profileRepo.Setup(r => r.GetProfileByUserIdAsync(It.IsAny<Guid>())).ReturnsAsync((UserProfile?)null);

        var svc = CreateService();

        await svc.DeleteProfileAsync(Guid.NewGuid()); // does not throw

        _profileRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteProfileAsync_WhenProfileExists_Deactivates()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile { UserId = userId, IsActive = true };
        _profileRepo.Setup(r => r.GetProfileByUserIdAsync(userId)).ReturnsAsync(profile);

        var svc = CreateService();

        await svc.DeleteProfileAsync(userId);

        Assert.False(profile.IsActive);
        _profileRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region Avatar / KarmaState

    [Fact]
    public async Task UpdateAvatarAsync_WithoutProfile_CreatesOneAndSaves()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Username = "alice", Email = "a@b.test", Role = UserRole.Player, IsActive = true, AccountStatus = UserAccountStatus.Active };
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        UserProfile? captured = null;
        _profileRepo.Setup(r => r.AddUserProfileAsync(It.IsAny<UserProfile>()))
            .Callback<UserProfile>(p => captured = p)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        await svc.UpdateAvatarAsync(userId, new UpdateAvatarRequestDto { AvatarUrl = "https://cdn.example/avatar.png" });

        Assert.NotNull(captured);
        Assert.Equal("https://cdn.example/avatar.png", captured!.AvatarUrl);
    }

    [Fact]
    public async Task GetKarmaStateAsync_WhenUserMissing_Throws()
    {
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<UserNotFoundException>(() => svc.GetKarmaStateAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetKarmaStateAsync_WhenNoProfile_ReturnsDefaultKarma()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Username = "alice", Email = "a@b.test", Role = UserRole.Player };
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        var result = await svc.GetKarmaStateAsync(userId);

        Assert.Equal(100, result.KarmaPoints);
        Assert.Equal(GamerTier.Gold.ToString(), result.GamerTier);
    }

    #endregion

    #region CreateOrGetProfileAsync

    [Fact]
    public async Task CreateOrGetProfileAsync_WhenMissing_CreatesAndReturns()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Username = "alice", Email = "a@b.test", Role = UserRole.Player };
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);
        _profileRepo.Setup(r => r.AddUserProfileAsync(It.IsAny<UserProfile>())).Returns(Task.CompletedTask);

        var svc = CreateService();

        var result = await svc.CreateOrGetProfileAsync(userId);

        Assert.NotNull(result);
        _profileRepo.Verify(r => r.AddUserProfileAsync(It.IsAny<UserProfile>()), Times.Once);
        _profileRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateOrGetProfileAsync_WhenAlreadyExists_NoNewProfile()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId);
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        var result = await svc.CreateOrGetProfileAsync(userId);

        Assert.NotNull(result);
        _profileRepo.Verify(r => r.AddUserProfileAsync(It.IsAny<UserProfile>()), Times.Never);
        _profileRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region Location

    [Fact]
    public async Task GetCurrentLocationAsync_WhenUserMissing_Throws()
    {
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<UserNotFoundException>(() => svc.GetCurrentLocationAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateCurrentLocationAsync_InvalidLatitude_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId);
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.UpdateCurrentLocationAsync(userId, new UpdatePlayerLocationRequestDto { Latitude = 200, Longitude = 0 }));
    }

    [Fact]
    public async Task UpdateCurrentLocationAsync_ValidCoordinates_SavesAndRecordsHistory()
    {
        var userId = Guid.NewGuid();
        var user = BuildUser(userId);
        _profileRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(user);

        PlayerLocationHistory? capturedHistory = null;
        _profileRepo.Setup(r => r.AddPlayerLocationHistoryAsync(It.IsAny<PlayerLocationHistory>()))
            .Callback<PlayerLocationHistory>(h => capturedHistory = h)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        var result = await svc.UpdateCurrentLocationAsync(userId, new UpdatePlayerLocationRequestDto
        {
            Latitude = 10.762622,
            Longitude = 106.660172,
            Source = PlayerLocationSource.Gps
        });

        Assert.True(result.HasLocation);
        Assert.Equal(10.762622, result.Latitude);
        Assert.Equal(106.660172, result.Longitude);
        Assert.NotNull(capturedHistory);
        Assert.Equal(userId, capturedHistory!.UserId);
        _profileRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearCurrentLocationAsync_WhenNoProfile_ThrowsProfileNotFound()
    {
        _profileRepo.Setup(r => r.GetProfileByUserIdAsync(It.IsAny<Guid>())).ReturnsAsync((UserProfile?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<ProfileNotFoundException>(() => svc.ClearCurrentLocationAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ClearCurrentLocationAsync_WhenNoSavedLocation_ThrowsNotFound()
    {
        var userId = Guid.NewGuid();
        _profileRepo.Setup(r => r.GetProfileByUserIdAsync(userId)).ReturnsAsync(new UserProfile { UserId = userId });

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.ClearCurrentLocationAsync(userId));
    }

    [Fact]
    public async Task ClearCurrentLocationAsync_WhenLocationExists_ClearsAndSaves()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile
        {
            UserId = userId,
            LastKnownLatitude = 10.0,
            LastKnownLongitude = 20.0,
            LastLocationSource = PlayerLocationSource.Gps,
            LastLocationUpdatedAt = DateTime.UtcNow
        };
        _profileRepo.Setup(r => r.GetProfileByUserIdAsync(userId)).ReturnsAsync(profile);

        var svc = CreateService();

        await svc.ClearCurrentLocationAsync(userId);

        Assert.Null(profile.LastKnownLatitude);
        Assert.Null(profile.LastKnownLongitude);
        _profileRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion
}