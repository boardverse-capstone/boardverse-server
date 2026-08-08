using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Geocoding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BoardVerse.Tests.Services;

public class UserProfileLocationServiceTests
{
    private static readonly Guid UserId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");

    private static PlayerGeocodingService CreateGeocodingService(
        IPlayerGeocodingService? impl = null)
    {
        // Default: trả null, không gọi Nominatim
        impl ??= new Mock<IPlayerGeocodingService>().Object;
        return new PlayerGeocodingService(
            new Mock<IGeocodingClient>().Object,
            Options.Create(new NominatimSettings()),
            new NoopCache(),
            NullLogger<PlayerGeocodingService>.Instance);
    }

    [Fact]
    public async Task GetCurrentLocationAsync_NoUser_ThrowsUserNotFound()
    {
        var repo = new Mock<IUserProfileRepository>();
        repo.Setup(r => r.GetByIdWithProfileAsync(UserId)).ReturnsAsync((User?)null);

        var service = new UserProfileService(repo.Object, Mock.Of<ILevelingService>(), new Mock<IPlayerGeocodingService>().Object);

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
                LastLocationUpdatedAt = DateTime.UtcNow,
                LastResolvedDistrict = "Quận 1",
                LastResolvedCity = "TP. Hồ Chí Minh",
                LastResolvedCountry = "Việt Nam",
                LastResolvedDisplayName = "Quận 1, TP. Hồ Chí Minh, Việt Nam",
                LastResolvedAt = DateTime.UtcNow
            }
        });

        var service = new UserProfileService(repo.Object, Mock.Of<ILevelingService>(), new Mock<IPlayerGeocodingService>().Object);
        var result = await service.GetCurrentLocationAsync(UserId);

        Assert.True(result.HasLocation);
        Assert.Equal(10.776889, result.Latitude);
        Assert.Equal(106.700806, result.Longitude);
        Assert.True(result.HasResolvedName);
        Assert.Equal("Quận 1", result.District);
        Assert.Equal("TP. Hồ Chí Minh", result.City);
        Assert.Equal("Quận 1, TP. Hồ Chí Minh, Việt Nam", result.DisplayName);
    }

    [Fact]
    public async Task UpdateCurrentLocationAsync_InvalidLatitude_ThrowsBadRequest()
    {
        var repo = new Mock<IUserProfileRepository>();
        repo.Setup(r => r.GetByIdWithProfileAsync(UserId))
            .ReturnsAsync(new User { Id = UserId, Email = "player@test.dev", Username = "player", Profile = new UserProfile { UserId = UserId } });

        var service = new UserProfileService(repo.Object, Mock.Of<ILevelingService>(), new Mock<IPlayerGeocodingService>().Object);

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

        var geocodingStub = new Mock<IPlayerGeocodingService>();
        geocodingStub.Setup(g => g.ReverseGeocodeAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReverseGeocodeResult?)null);

        var service = new UserProfileService(repo.Object, Mock.Of<ILevelingService>(), geocodingStub.Object);
        var result = await service.UpdateCurrentLocationAsync(UserId, new UpdatePlayerLocationRequestDto
        {
            Latitude = 10.776889,
            Longitude = 106.700806,
            Source = PlayerLocationSource.Manual
        });

        Assert.True(result.HasLocation);
        Assert.Equal("Manual", result.Source);
        // Geocoding stub trả null → LastResolved* vẫn null
        Assert.Null(result.District);
        Assert.False(result.HasResolvedName);
        repo.Verify(r => r.AddPlayerLocationHistoryAsync(It.IsAny<PlayerLocationHistory>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateCurrentLocationAsync_NominatimSuccess_PersistsLabelAndHistory()
    {
        var repo = new Mock<IUserProfileRepository>();
        var profile = new UserProfile { UserId = UserId, KarmaPoints = 100 };
        repo.Setup(r => r.GetByIdWithProfileAsync(UserId))
            .ReturnsAsync(new User { Id = UserId, Email = "player@test.dev", Username = "player", Profile = profile });

        var geocodingStub = new Mock<IPlayerGeocodingService>();
        geocodingStub.Setup(g => g.ReverseGeocodeAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReverseGeocodeResult
            {
                District = "Quận 1",
                City = "TP. Hồ Chí Minh",
                Country = "Việt Nam",
                DisplayName = "Quận 1, TP. Hồ Chí Minh, Việt Nam"
            });

        var service = new UserProfileService(repo.Object, Mock.Of<ILevelingService>(), geocodingStub.Object);
        var result = await service.UpdateCurrentLocationAsync(UserId, new UpdatePlayerLocationRequestDto
        {
            Latitude = 10.776889,
            Longitude = 106.700806,
            Source = PlayerLocationSource.Gps
        });

        Assert.True(result.HasLocation);
        Assert.True(result.HasResolvedName);
        Assert.Equal("Quận 1", result.District);
        Assert.Equal("TP. Hồ Chí Minh", result.City);
        Assert.Equal("Việt Nam", result.Country);
        Assert.Equal("Quận 1, TP. Hồ Chí Minh, Việt Nam", result.DisplayName);

        // Verify profile được cập nhật
        Assert.Equal("Quận 1", profile.LastResolvedDistrict);
        Assert.Equal("TP. Hồ Chí Minh", profile.LastResolvedCity);
        Assert.NotNull(profile.LastResolvedAt);
    }

    [Fact]
    public async Task UpdateCurrentLocationAsync_NominatimThrows_DoesNotPropagate()
    {
        var repo = new Mock<IUserProfileRepository>();
        var profile = new UserProfile { UserId = UserId, KarmaPoints = 100 };
        repo.Setup(r => r.GetByIdWithProfileAsync(UserId))
            .ReturnsAsync(new User { Id = UserId, Email = "player@test.dev", Username = "player", Profile = profile });

        var geocodingStub = new Mock<IPlayerGeocodingService>();
        geocodingStub.Setup(g => g.ReverseGeocodeAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated"));

        var service = new UserProfileService(repo.Object, Mock.Of<ILevelingService>(), geocodingStub.Object);

        // Service phải fail-soft: lat/lng vẫn lưu, không throw ra controller
        var result = await service.UpdateCurrentLocationAsync(UserId, new UpdatePlayerLocationRequestDto
        {
            Latitude = 10.776889,
            Longitude = 106.700806,
            Source = PlayerLocationSource.Gps
        });

        Assert.True(result.HasLocation);
        Assert.False(result.HasResolvedName);
        Assert.Null(result.District);
    }

    [Fact]
    public async Task ClearCurrentLocationAsync_NoSavedLocation_ThrowsNotFound()
    {
        var repo = new Mock<IUserProfileRepository>();
        repo.Setup(r => r.GetProfileByUserIdAsync(UserId))
            .ReturnsAsync(new UserProfile { UserId = UserId });

        var service = new UserProfileService(repo.Object, Mock.Of<ILevelingService>(), new Mock<IPlayerGeocodingService>().Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ClearCurrentLocationAsync(UserId));
    }

    [Fact]
    public async Task ClearCurrentLocationAsync_ClearsProfileLocationAndLabel()
    {
        var repo = new Mock<IUserProfileRepository>();
        var profile = new UserProfile
        {
            UserId = UserId,
            LastKnownLatitude = 10.0,
            LastKnownLongitude = 106.0,
            LastLocationSource = PlayerLocationSource.Gps,
            LastResolvedDistrict = "Quận 1",
            LastResolvedCity = "HCM",
            LastResolvedCountry = "VN",
            LastResolvedDisplayName = "Quận 1, HCM, VN",
            LastResolvedAt = DateTime.UtcNow
        };
        repo.Setup(r => r.GetProfileByUserIdAsync(UserId)).ReturnsAsync(profile);

        var service = new UserProfileService(repo.Object, Mock.Of<ILevelingService>(), new Mock<IPlayerGeocodingService>().Object);
        await service.ClearCurrentLocationAsync(UserId);

        Assert.Null(profile.LastKnownLatitude);
        Assert.Null(profile.LastKnownLongitude);
        Assert.Null(profile.LastResolvedDistrict);
        Assert.Null(profile.LastResolvedCity);
        Assert.Null(profile.LastResolvedDisplayName);
        Assert.Null(profile.LastResolvedAt);
        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    private sealed class NoopCache : IMemoryCacheGeocoding
    {
        public bool TryGetValue<T>(string key, out T? value)
        {
            value = default;
            return false;
        }
        public void Set<T>(string key, T value, TimeSpan ttl) { }
    }
}
