using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class UserProfileLocationServiceTests
{
    private static readonly Guid UserId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");

    [Fact]
    public async Task GetCurrentLocationAsync_NoUser_ThrowsUserNotFound()
    {
        var repo = new Mock<IUserProfileRepository>();
        repo.Setup(r => r.GetByIdWithProfileAsync(UserId)).ReturnsAsync((User?)null);

        var service = new UserProfileService(repo.Object);

        await Assert.ThrowsAsync<UserNotFoundException>(() =>
            service.GetCurrentLocationAsync(UserId));
    }

    [Fact]
    public async Task GetCurrentLocationAsync_ReturnsSavedCoordinates()
    {
        var repo = new Mock<IUserProfileRepository>();
        repo.Setup(r => r.GetByIdWithProfileAsync(UserId)).ReturnsAsync(new User
        {
            Id = UserId,
            Email = "player@test.dev",
            Username = "player",
            Profile = new UserProfile
            {
                UserId = UserId,
                LastKnownLatitude = 10.776889,
                LastKnownLongitude = 106.700806,
                LastLocationSource = PlayerLocationSource.Gps,
                LastLocationUpdatedAt = DateTime.UtcNow
            }
        });

        var service = new UserProfileService(repo.Object);
        var result = await service.GetCurrentLocationAsync(UserId);

        Assert.True(result.HasLocation);
        Assert.Equal(10.776889, result.Latitude);
        Assert.Equal(106.700806, result.Longitude);
    }

    [Fact]
    public async Task UpdateCurrentLocationAsync_InvalidLatitude_ThrowsBadRequest()
    {
        var repo = new Mock<IUserProfileRepository>();
        repo.Setup(r => r.GetByIdWithProfileAsync(UserId))
            .ReturnsAsync(new User { Id = UserId, Email = "player@test.dev", Username = "player", Profile = new UserProfile { UserId = UserId } });

        var service = new UserProfileService(repo.Object);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.UpdateCurrentLocationAsync(UserId, new UpdatePlayerLocationRequestDto
            {
                Latitude = 100,
                Longitude = 106
            }));
    }

    [Fact]
    public async Task UpdateCurrentLocationAsync_SavesLocationAndHistory()
    {
        var repo = new Mock<IUserProfileRepository>();
        var profile = new UserProfile { UserId = UserId, KarmaPoints = 100 };
        repo.Setup(r => r.GetByIdWithProfileAsync(UserId))
            .ReturnsAsync(new User { Id = UserId, Email = "player@test.dev", Username = "player", Profile = profile });

        var service = new UserProfileService(repo.Object);
        var result = await service.UpdateCurrentLocationAsync(UserId, new UpdatePlayerLocationRequestDto
        {
            Latitude = 10.776889,
            Longitude = 106.700806,
            Source = PlayerLocationSource.Manual
        });

        Assert.True(result.HasLocation);
        Assert.Equal("Manual", result.Source);
        repo.Verify(r => r.AddPlayerLocationHistoryAsync(It.IsAny<PlayerLocationHistory>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearCurrentLocationAsync_NoSavedLocation_ThrowsNotFound()
    {
        var repo = new Mock<IUserProfileRepository>();
        repo.Setup(r => r.GetProfileByUserIdAsync(UserId))
            .ReturnsAsync(new UserProfile { UserId = UserId });

        var service = new UserProfileService(repo.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ClearCurrentLocationAsync(UserId));
    }

    [Fact]
    public async Task ClearCurrentLocationAsync_ClearsProfileLocation()
    {
        var repo = new Mock<IUserProfileRepository>();
        var profile = new UserProfile
        {
            UserId = UserId,
            LastKnownLatitude = 10.0,
            LastKnownLongitude = 106.0,
            LastLocationSource = PlayerLocationSource.Gps
        };
        repo.Setup(r => r.GetProfileByUserIdAsync(UserId)).ReturnsAsync(profile);

        var service = new UserProfileService(repo.Object);
        await service.ClearCurrentLocationAsync(UserId);

        Assert.Null(profile.LastKnownLatitude);
        Assert.Null(profile.LastKnownLongitude);
        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }
}
