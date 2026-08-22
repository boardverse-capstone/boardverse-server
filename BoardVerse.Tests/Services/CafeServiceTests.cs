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

    // Note: GetNearbyCafesAsync không còn reject Guid.Empty cho gameTemplateId — giờ đây gameTemplateId
    // là optional (BR-MATCH-04: filter theo game nếu user chỉ định, ngược lại trả all nearby cafes).
    // Test này đã bị xóa vì behavior thay đổi.

    [Theory]
    [InlineData(-91, 106)]
    [InlineData(10, 181)]
    public async Task GetNearbyCafesAsync_InvalidCoordinates_ThrowsBadRequest(double lat, double lng)
    {
        var service = BuildService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetNearbyCafesAsync(lat, lng, 15, GameTemplateId, null, new PaginationParams()));
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(100)]
    public async Task GetNearbyCafesAsync_InvalidRadius_ThrowsBadRequest(double radiusKm)
    {
        var service = BuildService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetNearbyCafesAsync(10.0, 106.0, radiusKm, GameTemplateId, null, new PaginationParams()));
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
                It.IsAny<string?>(),
                It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nearby);

        var service = BuildService(cafeRepo: cafeRepo);

        var result = await service.GetNearbyCafesAsync(10.776889, 106.700806, 15, GameTemplateId, null, new PaginationParams());

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
                It.IsAny<string?>(),
                It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(empty);
        cafeRepo.Setup(r => r.GetAlternativeGameSuggestionsAsync(
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                GameTemplateId,
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(alternatives);

        var service = BuildService(cafeRepo: cafeRepo);

        var result = await service.GetNearbyCafesAsync(10.776889, 106.700806, 15, GameTemplateId, null, new PaginationParams());

        Assert.NotNull(result.EmptyResultMessage);
        Assert.Single(result.AlternativeSuggestions);
    }

    [Fact]
    public async Task GetNearbyCafesForCurrentUserAsync_NoSavedLocation_ThrowsBadRequest()
    {
        var profileRepo = new Mock<IUserProfileRepository>();
        profileRepo.Setup(r => r.GetProfileByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserProfile { UserId = Guid.NewGuid() });

        var service = BuildService(profileRepo: profileRepo);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetNearbyCafesForCurrentUserAsync(Guid.NewGuid(), 15, GameTemplateId, null, new PaginationParams()));
    }

    [Fact]
    public async Task GetNearbyCafesForCurrentUserAsync_UsesProfileCoordinates()
    {
        var userId = Guid.NewGuid();
        var profileRepo = new Mock<IUserProfileRepository>();
        profileRepo.Setup(r => r.GetProfileByUserIdAsync(userId, It.IsAny<CancellationToken>()))
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
                It.IsAny<string?>(),
                It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<NearbyCafeDto> { Data = [], Meta = new PaginationMeta() });
        cafeRepo.Setup(r => r.GetAlternativeGameSuggestionsAsync(
                10.776889,
                106.700806,
                It.IsAny<double>(),
                GameTemplateId,
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = BuildService(cafeRepo: cafeRepo, profileRepo: profileRepo);

        await service.GetNearbyCafesForCurrentUserAsync(userId, 15, GameTemplateId, null, new PaginationParams());

        cafeRepo.Verify(r => r.GetNearbyAsync(
            10.776889,
            106.700806,
            It.IsAny<double>(),
            GameTemplateId,
            It.IsAny<string?>(),
            It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCafeAsync_PartialCoordinates_ThrowsBadRequest()
    {
        var managerId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, ManagerId = managerId, Name = "Cafe", Address = "Addr" });

        var service = BuildService(cafeRepo: cafeRepo);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.UpdateCafeAsync(cafeId, managerId, new UpdateCafeRequestDto { Latitude = 10.0 }));
    }

    [Fact]
    public async Task GetAllActiveCafesAsync_DelegatesToRepository()
    {
        var pagination = new PaginationParams { PageNumber = 2, PageSize = 5 };
        var expected = new PaginatedResponse<NearbyCafeDto>
        {
            Data = [new NearbyCafeDto { Id = Guid.NewGuid(), Name = "Cafe A" }],
            Meta = new PaginationMeta { CurrentPage = 2, PageSize = 5, TotalItems = 6, TotalPages = 2 }
        };

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetAllActiveCafesAsync(pagination, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected)
            .Verifiable();

        var service = BuildService(cafeRepo: cafeRepo);

        var result = await service.GetAllActiveCafesAsync(pagination);

        Assert.Same(expected, result);
        cafeRepo.Verify(r => r.GetAllActiveCafesAsync(pagination, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IPushNotificationService>? pushNotificationService;

    private static CafeService BuildService(
        Mock<ICafeRepository>? cafeRepo = null,
        Mock<IUserProfileRepository>? profileRepo = null,
        Mock<ISystemConfigurationProvider>? config = null,
        Mock<IBookingRepository>? bookingRepo = null,
        Mock<ILobbyHubService>? hubService = null)
    {
        cafeRepo ??= new Mock<ICafeRepository>();
        profileRepo ??= new Mock<IUserProfileRepository>();
        config ??= new Mock<ISystemConfigurationProvider>();
        bookingRepo ??= new Mock<IBookingRepository>();
        hubService ??= new Mock<ILobbyHubService>();
        pushNotificationService ??= new Mock<IPushNotificationService>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var reservationRepo = new Mock<IReservationRepository>();

        config.Setup(c => c.GetDoubleAsync(SystemConfigKeys.MatchmakingRadiusKm, GeoLocationHelper.DefaultNearbyRadiusKm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeoLocationHelper.DefaultNearbyRadiusKm);

        return new CafeService(
            cafeRepo.Object,
            profileRepo.Object,
            config.Object,
            bookingRepo.Object,
            hubService.Object,
            pushNotificationService.Object,
            lobbyRepo.Object,
            reservationRepo.Object);
    }
}
