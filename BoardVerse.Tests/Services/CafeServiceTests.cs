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
using Microsoft.Extensions.Logging;
using Moq;

using System.Threading;
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
        cafeRepo.Verify(r => r.EnrichNearbyWithGameWaitAsync(It.IsAny<IList<NearbyCafeDto>>(), GameTemplateId, It.IsAny<CancellationToken>()), Times.Once);
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
        cafeRepo.Setup(r => r.GetAllActiveCafesAsync(pagination, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected)
            .Verifiable();

        var service = BuildService(cafeRepo: cafeRepo);

        var result = await service.GetAllActiveCafesAsync(pagination);

        Assert.Same(expected, result);
        cafeRepo.Verify(r => r.GetAllActiveCafesAsync(pagination, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetActiveGamesByCafeAsync_CafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var service = BuildService(cafeRepo: cafeRepo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetActiveGamesByCafeAsync(cafeId, new CafeActiveGamesQueryDto()));
    }

    [Fact]
    public async Task GetActiveGamesByCafeAsync_CafeInactive_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        var cafeRepo = new Mock<ICafeRepository>();
        // GetActiveByIdAsync trả null khi cafe đã IsActive=false hoặc PartnerOperationalStatus != Active.
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var service = BuildService(cafeRepo: cafeRepo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetActiveGamesByCafeAsync(cafeId, new CafeActiveGamesQueryDto()));

        // Đảm bảo repo KHÔNG gọi xuống GetActiveGamesByCafeAsync — phải fail-fast tại validation.
        cafeRepo.Verify(
            r => r.GetActiveGamesByCafeAsync(It.IsAny<Guid>(), It.IsAny<CafeActiveGamesQueryDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetActiveGamesByCafeAsync_CafeActive_DelegatesToRepository()
    {
        var cafeId = Guid.NewGuid();
        var query = new CafeActiveGamesQueryDto
        {
            CategoryId = Guid.NewGuid(),
            GroupSize = 5,
            AvailableOnly = true,
            SearchTerm = "  Catan  ",
            SortBy = CafeActiveGamesSort.AvailableBoxesDesc,
            PageNumber = 1,
            PageSize = 10
        };
        var expected = new PaginatedResponse<CafeActiveGameDto>
        {
            Data = new List<CafeActiveGameDto>
            {
                new()
                {
                    InventoryId = Guid.NewGuid(),
                    GameTemplateId = GameTemplateId,
                    GameName = "Catan",
                    BoxQuantity = 3,
                    AvailableBoxCount = 2,
                    FitsGroupSize = true,
                    Status = "Available"
                }
            },
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 10, TotalItems = 1, TotalPages = 1 }
        };

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Active Cafe", Address = "123 Test Street" });
        cafeRepo.Setup(r => r.GetActiveGamesByCafeAsync(
                cafeId,
                It.Is<CafeActiveGamesQueryDto>(q =>
                    q.CategoryId == query.CategoryId
                    && q.GroupSize == query.GroupSize
                    && q.AvailableOnly == query.AvailableOnly
                    && q.SearchTerm == query.SearchTerm
                    && q.SortBy == query.SortBy),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected)
            .Verifiable();

        var service = BuildService(cafeRepo: cafeRepo);

        var result = await service.GetActiveGamesByCafeAsync(cafeId, query);

        Assert.Same(expected, result);
        cafeRepo.Verify(
            r => r.GetActiveGamesByCafeAsync(cafeId, It.IsAny<CafeActiveGamesQueryDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetActiveGamesByCafeAsync_DefaultsApplied_WhenQueryIsEmpty()
    {
        var cafeId = Guid.NewGuid();
        // Query không truyền gì → mặc định: PageNumber=1, PageSize=20, AvailableOnly=false,
        // SortBy=Name, các filter khác null.
        var rawQuery = new CafeActiveGamesQueryDto();

        var capturedQuery = (CafeActiveGamesQueryDto?)null;
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Cafe", Address = "Addr" });
        cafeRepo.Setup(r => r.GetActiveGamesByCafeAsync(
                cafeId,
                It.IsAny<CafeActiveGamesQueryDto>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, CafeActiveGamesQueryDto, CancellationToken>((_, q, _) => capturedQuery = q)
            .ReturnsAsync(new PaginatedResponse<CafeActiveGameDto>
            {
                Data = [],
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 0, TotalPages = 0 }
            });

        var service = BuildService(cafeRepo: cafeRepo);

        await service.GetActiveGamesByCafeAsync(cafeId, rawQuery);

        Assert.NotNull(capturedQuery);
        Assert.Null(capturedQuery!.CategoryId);
        Assert.Null(capturedQuery.GroupSize);
        Assert.False(capturedQuery.AvailableOnly);
        Assert.Null(capturedQuery.SearchTerm);
        Assert.Equal(CafeActiveGamesSort.Name, capturedQuery.SortBy);
        Assert.Equal(1, capturedQuery.PageNumber);
        Assert.Equal(20, capturedQuery.PageSize);
    }

    public class SafeTimeOnlyTests
    {
        private static readonly Guid CafeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        [Fact]
        public void NullValue_ReturnsNull()
        {
            var service = BuildService();

            var result = service.SafeTimeOnly(CafeId, "WeekdayOpen", null);

            Assert.Null(result);
        }

        [Fact]
        public void NormalTime_ReturnsExpectedTimeOnly()
        {
            var service = BuildService();

            var result = service.SafeTimeOnly(CafeId, "WeekdayOpen", new TimeSpan(8, 30, 0));

            Assert.Equal(new TimeOnly(8, 30, 0), result);
        }

        [Fact]
        public void MaximumValidTime_ReturnsExpectedTimeOnly()
        {
            // 23:59:59.9999999 = 863,999,999,999 ticks — đúng ranh giới trên của TimeOnly.
            var service = BuildService();

            var result = service.SafeTimeOnly(CafeId, "WeekdayClose", new TimeSpan(0, 23, 59, 59, 999).Add(TimeSpan.FromTicks(9999)));

            Assert.NotNull(result);
            Assert.Equal(23, result.Value.Hour);
            Assert.Equal(59, result.Value.Minute);
        }

        /// <summary>
        /// Regression test cho bug 2026-10-03: dữ liệu cũ lưu "24:00:00" (đóng cửa lúc nửa đêm) gây
        /// <c>ArgumentOutOfRangeException: Ticks must be between 0 and TimeOnly.MaxValue.Ticks</c>
        /// khi gọi <c>TimeOnly.FromTimeSpan</c> tại <c>MapToManagerDtoAsync</c>. Giờ phải map về 00:00:00.
        /// </summary>
        [Fact]
        public void Exactly24Hours_ClampsToMidnight()
        {
            var service = BuildService();

            var result = service.SafeTimeOnly(CafeId, "WeekdayClose", TimeSpan.FromHours(24));

            Assert.NotNull(result);
            Assert.Equal(new TimeOnly(0, 0), result.Value);
        }

        [Fact]
        public void Beyond24Hours_ClampsToMidnight()
        {
            var service = BuildService();

            var result = service.SafeTimeOnly(CafeId, "WeekendClose", TimeSpan.FromHours(25));

            Assert.NotNull(result);
            Assert.Equal(new TimeOnly(0, 0), result.Value);
        }

        [Fact]
        public void NegativeTimeSpan_ReturnsNull()
        {
            var service = BuildService();

            var result = service.SafeTimeOnly(CafeId, "WeekdayOpen", TimeSpan.FromHours(-1));

            Assert.Null(result);
        }

        private static CafeService BuildService()
        {
            var cafeRepo = new Mock<ICafeRepository>();
            var profileRepo = new Mock<IUserProfileRepository>();
            var config = new Mock<ISystemConfigurationProvider>();
            var bookingRepo = new Mock<IBookingRepository>();
            var hubService = new Mock<ILobbyHubService>();
            var pushNotificationService = new Mock<IPushNotificationService>();
            var lobbyRepo = new Mock<ILobbyRepository>();
            var reservationRepo = new Mock<IReservationRepository>();
            var activeSessionRepo = new Mock<IActiveSessionRepository>();
            var logger = new Mock<ILogger<CafeService>>();

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
                reservationRepo.Object,
                activeSessionRepo.Object,
                logger.Object);
        }
    }

    public class ParseTimeInputGuardTests
    {
        /// <summary>
        /// Regression test cho input validator <c>CafePartnerApplicationService.ParseTime</c>:
        /// <c>TimeSpan.TryParseExact("hh\:mm", ...)</c> tự chặn các giờ &gt;= 24 hoặc &lt; 0 vì format "hh" chỉ chấp nhận 00-23.
        /// Đây là lớp bảo vệ thực sự tại input. Có thêm range check phòng thủ ở ParseTime (>= 24h || &lt; 0) nhưng
        /// không thể trigger từ input hợp lệ qua TryParseExact — chỉ kích hoạt nếu sau này đổi format.
        /// </summary>
        [Theory]
        [InlineData("24:00")]
        [InlineData("25:00")]
        [InlineData("99:00")]
        public void ParseTime_OutOfRangeHourFormat_ThrowsBadRequestWithTimeFormatMessage(string input)
        {
            var asm = typeof(BoardVerse.Services.Services.CafePartnerApplicationService).Assembly;
            var type = asm.GetType("BoardVerse.Services.Services.CafePartnerApplicationService", throwOnError: true)!;
            var method = type.GetMethod("ParseTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [input, "WeekdayClose"]));
            var inner = Assert.IsType<BadRequestException>(ex.InnerException);
            Assert.Contains("HH:mm", inner.Message);
        }

        [Theory]
        [InlineData("00:00")]
        [InlineData("08:30")]
        [InlineData("23:59")]
        public void ParseTime_InRange_ReturnsTimeSpan(string input)
        {
            var asm = typeof(BoardVerse.Services.Services.CafePartnerApplicationService).Assembly;
            var type = asm.GetType("BoardVerse.Services.Services.CafePartnerApplicationService", throwOnError: true)!;
            var method = type.GetMethod("ParseTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            var result = (TimeSpan)method.Invoke(null, [input, "WeekdayClose"])!;

            Assert.True(result >= TimeSpan.Zero);
            Assert.True(result < TimeSpan.FromHours(24));
        }

        [Fact]
        public void ParseTime_InvalidFormat_ThrowsBadRequestWithTimeFormatMessage()
        {
            var asm = typeof(BoardVerse.Services.Services.CafePartnerApplicationService).Assembly;
            var type = asm.GetType("BoardVerse.Services.Services.CafePartnerApplicationService", throwOnError: true)!;
            var method = type.GetMethod("ParseTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, ["abc", "WeekdayClose"]));
            var inner = Assert.IsType<BadRequestException>(ex.InnerException);
            Assert.Contains("HH:mm", inner.Message);
        }
    }

    private static Mock<IPushNotificationService>? pushNotificationService;

    private static CafeService BuildService(
        Mock<ICafeRepository>? cafeRepo = null,
        Mock<IUserProfileRepository>? profileRepo = null,
        Mock<ISystemConfigurationProvider>? config = null,
        Mock<IBookingRepository>? bookingRepo = null,
        Mock<ILobbyHubService>? hubService = null,
        Mock<IActiveSessionRepository>? activeSessionRepo = null)
    {
        cafeRepo ??= new Mock<ICafeRepository>();
        profileRepo ??= new Mock<IUserProfileRepository>();
        config ??= new Mock<ISystemConfigurationProvider>();
        bookingRepo ??= new Mock<IBookingRepository>();
        hubService ??= new Mock<ILobbyHubService>();
        pushNotificationService ??= new Mock<IPushNotificationService>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var reservationRepo = new Mock<IReservationRepository>();
        activeSessionRepo ??= new Mock<IActiveSessionRepository>();
        var logger = new Mock<ILogger<CafeService>>();

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
            reservationRepo.Object,
            activeSessionRepo.Object,
            logger.Object);
    }
}
