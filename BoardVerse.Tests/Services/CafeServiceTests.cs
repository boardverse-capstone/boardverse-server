using BoardVerse.Core.Common;
using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class CafeServiceTests
{
    private static readonly Guid GameTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetNearbyCafesAsync_EmptyGameTemplateId_ThrowsBadRequest()
    {
        var service = BuildService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetNearbyCafesAsync(10.0, 106.0, 15, Guid.Empty, new PaginationParams()));
    }

    [Theory]
    [InlineData(-91, 106)]
    [InlineData(10, 181)]
    public async Task GetNearbyCafesAsync_InvalidCoordinates_ThrowsBadRequest(double lat, double lng)
    {
        var service = BuildService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetNearbyCafesAsync(lat, lng, 15, GameTemplateId, new PaginationParams()));
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(100)]
    public async Task GetNearbyCafesAsync_InvalidRadius_ThrowsBadRequest(double radiusKm)
    {
        var service = BuildService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetNearbyCafesAsync(10.0, 106.0, radiusKm, GameTemplateId, new PaginationParams()));
    }

    [Fact]
    public async Task GetNearbyCafesAsync_WithResults_EnrichesWaitAndSkipsAlternatives()
    {
        var cafeRepo = new Mock<ICafeRepository>();
        var nearby = new PaginatedResponse<NearbyCafeDto>
        {
            Data = [new NearbyCafeDto { Id = Guid.NewGuid(), Name = "Demo Cafe" }],
            Meta = new PaginationMeta { TotalItems = 1 }
        };

        cafeRepo.Setup(r => r.GetNearbyAsync(
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                GameTemplateId,
                It.IsAny<PaginationParams>()))
            .ReturnsAsync(nearby);

        var service = BuildService(cafeRepo: cafeRepo);

        var result = await service.GetNearbyCafesAsync(10.776889, 106.700806, 15, GameTemplateId, new PaginationParams());

        Assert.Null(result.EmptyResultMessage);
        Assert.Empty(result.AlternativeSuggestions);
        cafeRepo.Verify(r => r.EnrichNearbyWithGameWaitAsync(It.IsAny<IList<NearbyCafeDto>>(), GameTemplateId), Times.Once);
    }

    [Fact]
    public async Task GetNearbyCafesAsync_NoResults_ReturnsEmptyMessageAndAlternatives()
    {
        var cafeRepo = new Mock<ICafeRepository>();
        var empty = new PaginatedResponse<NearbyCafeDto>
        {
            Data = [],
            Meta = new PaginationMeta { TotalItems = 0 }
        };
        var alternatives = new List<NearbyAlternativeGameSuggestionDto>
        {
            new() { GameTemplateId = Guid.NewGuid(), GameName = "Catan", NearbyCafeCount = 2 }
        };

        cafeRepo.Setup(r => r.GetNearbyAsync(
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                GameTemplateId,
                It.IsAny<PaginationParams>()))
            .ReturnsAsync(empty);
        cafeRepo.Setup(r => r.GetAlternativeGameSuggestionsAsync(
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                GameTemplateId,
                It.IsAny<int>()))
            .ReturnsAsync(alternatives);

        var service = BuildService(cafeRepo: cafeRepo);

        var result = await service.GetNearbyCafesAsync(10.776889, 106.700806, 15, GameTemplateId, new PaginationParams());

        Assert.NotNull(result.EmptyResultMessage);
        Assert.Single(result.AlternativeSuggestions);
    }

    [Fact]
    public async Task GetNearbyCafesForCurrentUserAsync_NoSavedLocation_ThrowsBadRequest()
    {
        var profileRepo = new Mock<IUserProfileRepository>();
        profileRepo.Setup(r => r.GetProfileByUserIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new UserProfile { UserId = Guid.NewGuid() });

        var service = BuildService(profileRepo: profileRepo);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetNearbyCafesForCurrentUserAsync(Guid.NewGuid(), 15, GameTemplateId, new PaginationParams()));
    }

    [Fact]
    public async Task GetNearbyCafesForCurrentUserAsync_UsesProfileCoordinates()
    {
        var userId = Guid.NewGuid();
        var profileRepo = new Mock<IUserProfileRepository>();
        profileRepo.Setup(r => r.GetProfileByUserIdAsync(userId))
            .ReturnsAsync(new UserProfile
            {
                UserId = userId,
                LastKnownLatitude = 10.776889,
                LastKnownLongitude = 106.700806
            });

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetNearbyAsync(
                10.776889,
                106.700806,
                It.IsAny<double>(),
                GameTemplateId,
                It.IsAny<PaginationParams>()))
            .ReturnsAsync(new PaginatedResponse<NearbyCafeDto> { Data = [], Meta = new PaginationMeta() });
        cafeRepo.Setup(r => r.GetAlternativeGameSuggestionsAsync(
                10.776889,
                106.700806,
                It.IsAny<double>(),
                GameTemplateId,
                It.IsAny<int>()))
            .ReturnsAsync([]);

        var service = BuildService(cafeRepo: cafeRepo, profileRepo: profileRepo);

        await service.GetNearbyCafesForCurrentUserAsync(userId, 15, GameTemplateId, new PaginationParams());

        cafeRepo.Verify(r => r.GetNearbyAsync(
            10.776889,
            106.700806,
            It.IsAny<double>(),
            GameTemplateId,
            It.IsAny<PaginationParams>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCafeAsync_PartialCoordinates_ThrowsBadRequest()
    {
        var managerId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetByIdAsync(cafeId))
            .ReturnsAsync(new Cafe { Id = cafeId, ManagerId = managerId, Name = "Cafe", Address = "Addr" });

        var service = BuildService(cafeRepo: cafeRepo);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.UpdateCafeAsync(cafeId, managerId, new UpdateCafeRequestDto { Latitude = 10.0 }));
    }

    private static CafeService BuildService(
        Mock<ICafeRepository>? cafeRepo = null,
        Mock<IUserProfileRepository>? profileRepo = null,
        Mock<ISystemConfigurationProvider>? config = null)
    {
        cafeRepo ??= new Mock<ICafeRepository>();
        profileRepo ??= new Mock<IUserProfileRepository>();
        config ??= new Mock<ISystemConfigurationProvider>();

        config.Setup(c => c.GetDoubleAsync(SystemConfigKeys.MatchmakingRadiusKm, GeoLocationHelper.DefaultNearbyRadiusKm))
            .ReturnsAsync(GeoLocationHelper.DefaultNearbyRadiusKm);

        return new CafeService(cafeRepo.Object, profileRepo.Object, config.Object);
    }
}
