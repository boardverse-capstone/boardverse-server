using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho BoardGameService — search/detail/categories/play configuration + navigation routing.
/// </summary>
public class BoardGameServiceTests
{
    private static readonly Guid GameId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CategoryId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static IBoardGameService BuildService(
        Mock<IGameTemplateRepository> gameRepo,
        Mock<ICategoryRepository>? categoryRepo = null,
        Mock<ICafeRepository>? cafeRepo = null,
        Mock<IPlayerBoardGameSaveRepository>? saveRepo = null,
        IMemoryCache? memoryCache = null) =>
        new BoardGameService(
            gameRepo.Object,
            (categoryRepo ?? new Mock<ICategoryRepository>()).Object,
            (cafeRepo ?? new Mock<ICafeRepository>()).Object,
            (saveRepo ?? new Mock<IPlayerBoardGameSaveRepository>()).Object,
            memoryCache ?? new MemoryCache(new MemoryCacheOptions()));

    private static GameTemplate SoloGame() => new()
    {
        Id = GameId,
        Name = "Solo Game",
        Description = "Desc",
        ThumbnailUrl = "https://cdn/solo.jpg",
        MinPlayers = 1,
        MaxPlayers = 1,
        PlayTime = 15,
        Categories = [],
        Components = []
    };

    private static GameTemplate MultiplayerGame() => new()
    {
        Id = GameId,
        Name = "Catan",
        Description = "Desc",
        ThumbnailUrl = "https://cdn/catan.jpg",
        MinPlayers = 3,
        MaxPlayers = 4,
        PlayTime = 60,
        Categories = [],
        Components = []
    };

    // =============== SearchBoardGamesAsync ===============

    [Fact]
    public async Task SearchBoardGamesAsync_MapsListItemsWithComponentCount()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        var componentId = Guid.NewGuid();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = [new GameTemplate
                {
                    Id = GameId,
                    Name = "Catan",
                    Description = "Desc",
                    ThumbnailUrl = "thumb",
                    MinPlayers = 3,
                    MaxPlayers = 4,
                    PlayTime = 60,
                    Categories = [],
                    Components = []
                }],
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 10, TotalItems = 1, TotalPages = 1 }
            });
        gameRepo.Setup(r => r.GetComponentCountsByGameIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { { GameId, 12 } });

        var service = BuildService(gameRepo);
        var result = await service.SearchBoardGamesAsync(new GetBoardGamesQuery { PageNumber = 1, PageSize = 10 });

        Assert.Single(result.Data);
        var first = result.Data.First();
        Assert.Equal(12, first.ComponentCount);
        Assert.Equal(GameId, first.Id);
    }

    [Fact]
    public async Task SearchBoardGamesAsync_ComponentCountMissing_DefaultsToZero()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = [new GameTemplate { Id = GameId, Name = "Catan", Categories = [], Components = [] }],
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 10, TotalItems = 1, TotalPages = 1 }
            });
        gameRepo.Setup(r => r.GetComponentCountsByGameIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var service = BuildService(gameRepo);
        var result = await service.SearchBoardGamesAsync(new GetBoardGamesQuery { PageNumber = 1, PageSize = 10 });

        Assert.Equal(0, result.Data.First().ComponentCount);
    }

    [Fact]
    public async Task SearchBoardGamesAsync_EmptyResult_ReturnsEmptyPage()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = [],
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 10, TotalItems = 0, TotalPages = 0 }
            });
        gameRepo.Setup(r => r.GetComponentCountsByGameIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var service = BuildService(gameRepo);
        var result = await service.SearchBoardGamesAsync(new GetBoardGamesQuery { PageNumber = 1, PageSize = 10 });

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Meta.TotalItems);
    }

    // =============== GetBoardGameByIdAsync ===============

    [Fact]
    public async Task GetBoardGameByIdAsync_GameFound_ReturnsDetailDto()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var service = BuildService(gameRepo);
        var dto = await service.GetBoardGameByIdAsync(GameId);

        Assert.Equal(GameId, dto.Id);
        Assert.Equal("Catan", dto.Name);
        Assert.Equal(3, dto.MinPlayers);
        Assert.Equal(4, dto.MaxPlayers);
        Assert.Equal(60, dto.PlayTime);
    }

    [Fact]
    public async Task GetBoardGameByIdAsync_GameMissing_ThrowsNotFound()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameTemplate?)null);

        var service = BuildService(gameRepo);

        await Assert.ThrowsAsync<BoardGameNotFoundException>(() =>
            service.GetBoardGameByIdAsync(GameId));
    }

    // =============== GetBoardGameByIdAsync — IsSaved (player favorites) ===============

    [Fact]
    public async Task GetBoardGameByIdAsync_AnonymousUser_IsSavedIsFalse_NoRepositoryCall()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>(MockBehavior.Strict);
        // userId = null → KHÔNG được phép gọi ExistsAsync (sẽ fail test nếu service cố truy vấn).
        saveRepo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Throws(new Exception("Should not be called for anonymous viewer"));

        var service = BuildService(gameRepo, saveRepo: saveRepo);
        var dto = await service.GetBoardGameByIdAsync(GameId, userId: null);

        Assert.False(dto.IsSaved);
        saveRepo.Verify(
            r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetBoardGameByIdAsync_PlayerHasSaved_IsSavedIsTrue()
    {
        var playerId = Guid.NewGuid();
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.ExistsAsync(playerId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = BuildService(gameRepo, saveRepo: saveRepo);
        var dto = await service.GetBoardGameByIdAsync(GameId, playerId);

        Assert.True(dto.IsSaved);
        saveRepo.Verify(r => r.ExistsAsync(playerId, GameId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBoardGameByIdAsync_PlayerHasNotSaved_IsSavedIsFalse()
    {
        var playerId = Guid.NewGuid();
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.ExistsAsync(playerId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = BuildService(gameRepo, saveRepo: saveRepo);
        var dto = await service.GetBoardGameByIdAsync(GameId, playerId);

        Assert.False(dto.IsSaved);
        saveRepo.Verify(r => r.ExistsAsync(playerId, GameId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =============== GetCategoriesAsync ===============

    [Fact]
    public async Task GetCategoriesAsync_MapsAllActiveCategories()
    {
        var categoryRepo = new Mock<ICategoryRepository>();
        categoryRepo.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Category { Id = CategoryId, Name = "Strategy", Slug = "strategy", Description = "desc", SortOrder = 1 },
                new Category { Id = Guid.NewGuid(), Name = "Family", Slug = "family", Description = null, SortOrder = 2 }
            ]);

        var service = BuildService(new Mock<IGameTemplateRepository>(), categoryRepo);
        var result = await service.GetCategoriesAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("strategy", result[0].Slug);
        Assert.Null(result[1].Description);
    }

    [Fact]
    public async Task GetCategoriesAsync_NoCategories_ReturnsEmptyList()
    {
        var categoryRepo = new Mock<ICategoryRepository>();
        categoryRepo.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = BuildService(new Mock<IGameTemplateRepository>(), categoryRepo);
        var result = await service.GetCategoriesAsync();

        Assert.Empty(result);
    }

    // =============== GetPlayConfigurationAsync ===============

    [Fact]
    public async Task GetPlayConfigurationAsync_SoloGame_ExposesSoloAndGroupModes()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SoloGame());

        var service = BuildService(gameRepo);
        var config = await service.GetPlayConfigurationAsync(GameId);

        Assert.True(config.SupportsSoloPlay);
        Assert.Contains(PlayerPlayMode.Solo, config.AvailablePlayModes);
        Assert.Contains(PlayerPlayMode.Group, config.AvailablePlayModes);
    }

    [Fact]
    public async Task GetPlayConfigurationAsync_MultiplayerOnly_DoesNotExposeSolo()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var service = BuildService(gameRepo);
        var config = await service.GetPlayConfigurationAsync(GameId);

        Assert.False(config.SupportsSoloPlay);
        Assert.DoesNotContain(PlayerPlayMode.Solo, config.AvailablePlayModes);
        Assert.Single(config.AvailablePlayModes);
    }

    [Fact]
    public async Task GetPlayConfigurationAsync_GameMissing_ThrowsNotFound()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameTemplate?)null);

        var service = BuildService(gameRepo);

        await Assert.ThrowsAsync<BoardGameNotFoundException>(() =>
            service.GetPlayConfigurationAsync(GameId));
    }

    // =============== ResolvePlayNavigationAsync ===============

    [Fact]
    public async Task ResolvePlayNavigationAsync_SoloOnMultiplayerGame_ThrowsBadRequest()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var service = BuildService(gameRepo);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ResolvePlayNavigationAsync(GameId, new ResolveGamePlayNavigationRequestDto
            {
                PlayMode = PlayerPlayMode.Solo
            }));
    }

    [Fact]
    public async Task ResolvePlayNavigationAsync_GroupMode_ReturnsLobbyCreation()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var service = BuildService(gameRepo);
        var result = await service.ResolvePlayNavigationAsync(GameId, new ResolveGamePlayNavigationRequestDto
        {
            PlayMode = PlayerPlayMode.Group
        });

        Assert.Equal(GamePlayNavigationTarget.LobbyCreation, result.NavigationTarget);
        Assert.NotNull(result.RoomConfiguration);
        Assert.Equal(3, result.RoomConfiguration.MinPlayers);
        Assert.Equal(4, result.RoomConfiguration.MaxPlayers);
        Assert.Equal(3, result.RoomConfiguration.DefaultPlayerCount);
    }

    [Fact]
    public async Task ResolvePlayNavigationAsync_SoloModeOnSoloGame_ReturnsSoloSession()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SoloGame());

        var service = BuildService(gameRepo);
        var result = await service.ResolvePlayNavigationAsync(GameId, new ResolveGamePlayNavigationRequestDto
        {
            PlayMode = PlayerPlayMode.Solo
        });

        Assert.Equal(GamePlayNavigationTarget.SoloBooking, result.NavigationTarget);
        Assert.Equal(1, result.MinPlayers);
        Assert.True(result.SupportsSoloPlay);
    }

    [Fact]
    public async Task ResolvePlayNavigationAsync_GameMissing_ThrowsNotFound()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameTemplate?)null);

        var service = BuildService(gameRepo);

        await Assert.ThrowsAsync<BoardGameNotFoundException>(() =>
            service.ResolvePlayNavigationAsync(GameId, new ResolveGamePlayNavigationRequestDto
            {
                PlayMode = PlayerPlayMode.Group
            }));
    }

    // =============== GetTopPlayedBoardGamesAsync ===============

    [Fact]
    public async Task GetTopPlayedBoardGamesAsync_TopCountPositive_ReturnsListFromRepository()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        var expected = new List<TopBoardGameDto>
        {
            new() { Id = GameId, Name = "Catan", PlayCount = 42 }
        };
        gameRepo.Setup(r => r.GetTopPlayedBoardGamesAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var service = BuildService(gameRepo);
        var result = await service.GetTopPlayedBoardGamesAsync(5);

        Assert.Single(result);
        Assert.Equal("Catan", result[0].Name);
        Assert.Equal(42, result[0].PlayCount);
        gameRepo.Verify(r => r.GetTopPlayedBoardGamesAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTopPlayedBoardGamesAsync_TopCountZero_ThrowsBadRequest()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = BuildService(gameRepo);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetTopPlayedBoardGamesAsync(0));

        Assert.Contains("lớn hơn 0", ex.Message);
    }

    [Fact]
    public async Task GetTopPlayedBoardGamesAsync_TopCountNegative_ThrowsBadRequest()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = BuildService(gameRepo);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetTopPlayedBoardGamesAsync(-1));

        Assert.Contains("lớn hơn 0", ex.Message);
    }

    [Fact]
    public async Task GetTopPlayedBoardGamesAsync_DefaultTopCount_Uses5()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetTopPlayedBoardGamesAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = BuildService(gameRepo);
        await service.GetTopPlayedBoardGamesAsync();

        gameRepo.Verify(r => r.GetTopPlayedBoardGamesAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTopPlayedBoardGamesAsync_CachesResult_BetweenCalls()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetTopPlayedBoardGamesAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TopBoardGameDto { Id = GameId, Name = "Catan", PlayCount = 10 }]);

        var service = BuildService(gameRepo);

        var first = await service.GetTopPlayedBoardGamesAsync();
        var second = await service.GetTopPlayedBoardGamesAsync();

        Assert.Single(first);
        Assert.Single(second);
        gameRepo.Verify(r => r.GetTopPlayedBoardGamesAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =============== ResolvePlayNavigationAsync boundary ===============

    [Fact]
    public async Task ResolvePlayNavigationAsync_MinPlayersTwo_SoloMode_TreatedAsMultiplayer()
    {
        // Boundary test: MinPlayers = 2 là ngưỡng nhỏ nhất của group game, không phải solo.
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameTemplate
            {
                Id = GameId,
                Name = "TwoPlayerOnly",
                Description = "Desc",
                ThumbnailUrl = "https://cdn/two.jpg",
                MinPlayers = 2,
                MaxPlayers = 2,
                PlayTime = 30,
                Categories = [],
                Components = []
            });

        var service = BuildService(gameRepo);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ResolvePlayNavigationAsync(GameId, new ResolveGamePlayNavigationRequestDto
            {
                PlayMode = PlayerPlayMode.Solo
            }));
    }

    [Fact]
    public async Task ResolvePlayNavigationAsync_MinPlayersTwo_GroupMode_AllowsTwo()
    {
        // Boundary test: MinPlayers = MaxPlayers = 2 → group mode hợp lệ với default 2 người.
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameTemplate
            {
                Id = GameId,
                Name = "TwoPlayerOnly",
                Description = "Desc",
                ThumbnailUrl = "https://cdn/two.jpg",
                MinPlayers = 2,
                MaxPlayers = 2,
                PlayTime = 30,
                Categories = [],
                Components = []
            });

        var service = BuildService(gameRepo);
        var result = await service.ResolvePlayNavigationAsync(GameId, new ResolveGamePlayNavigationRequestDto
        {
            PlayMode = PlayerPlayMode.Group
        });

        Assert.Equal(GamePlayNavigationTarget.LobbyCreation, result.NavigationTarget);
        Assert.NotNull(result.RoomConfiguration);
        Assert.Equal(2, result.RoomConfiguration.MinPlayers);
        Assert.Equal(2, result.RoomConfiguration.MaxPlayers);
        Assert.Equal(2, result.RoomConfiguration.DefaultPlayerCount);
    }

    // =============== GetActiveCafesByBoardGameAsync ===============

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_BoardGameMissing_ThrowsNotFound()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameTemplate?)null);

        var service = BuildService(gameRepo);

        await Assert.ThrowsAsync<BoardGameNotFoundException>(() =>
            service.GetActiveCafesByBoardGameAsync(GameId, userId: null, new ActiveCafesByBoardGameQueryDto()));
    }

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_BoardGameMissing_DoesNotCallCafeRepository()
    {
        // Negative test: khi board game không tồn tại, service phải fail-fast và KHÔNG query xuống CafeRepository.
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameTemplate?)null);

        var cafeRepo = new Mock<ICafeRepository>();
        var service = BuildService(gameRepo, cafeRepo: cafeRepo);

        await Assert.ThrowsAsync<BoardGameNotFoundException>(() =>
            service.GetActiveCafesByBoardGameAsync(GameId, userId: null, new ActiveCafesByBoardGameQueryDto()));

        cafeRepo.Verify(
            r => r.GetActiveCafesByBoardGameAsync(
                It.IsAny<Guid>(),
                It.IsAny<double?>(),
                It.IsAny<double?>(),
                It.IsAny<string?>(),
                It.IsAny<PaginationParams>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_NoLocation_DelegatesToRepository()
    {
        var query = new ActiveCafesByBoardGameQueryDto
        {
            Latitude = null,
            Longitude = null,
            Name = "  Catan Cafe  ",
            PageNumber = 2,
            PageSize = 10
        };
        var expected = new PaginatedResponse<NearbyCafeDto>
        {
            Data = [new NearbyCafeDto { Id = Guid.NewGuid(), Name = "Catan Cafe Thủ Đức" }],
            Meta = new PaginationMeta { CurrentPage = 2, PageSize = 10, TotalItems = 1, TotalPages = 1 }
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveCafesByBoardGameAsync(
                GameId,
                It.Is<double?>(v => v == null),
                It.Is<double?>(v => v == null),
                "  Catan Cafe  ",
                It.Is<PaginationParams>(p => p.PageNumber == 2 && p.PageSize == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected)
            .Verifiable();

        var service = BuildService(gameRepo, cafeRepo: cafeRepo);
        var result = await service.GetActiveCafesByBoardGameAsync(GameId, userId: null, query);

        Assert.Same(expected, result.Cafes);
        cafeRepo.Verify();
    }

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_WithLocation_PassesCoordinatesToRepository()
    {
        var lat = 10.776889;
        var lng = 106.700806;
        var query = new ActiveCafesByBoardGameQueryDto
        {
            Latitude = lat,
            Longitude = lng,
            PageNumber = 1,
            PageSize = 20
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveCafesByBoardGameAsync(
                GameId,
                lat,
                lng,
                null,
                It.IsAny<PaginationParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<NearbyCafeDto>
            {
                Data = [],
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 0, TotalPages = 0 }
            })
            .Verifiable();

        var service = BuildService(gameRepo, cafeRepo: cafeRepo);
        await service.GetActiveCafesByBoardGameAsync(GameId, userId: null, query);

        cafeRepo.Verify();
    }

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_PartialLocation_TreatsAsNoLocation()
    {
        // Chỉ truyền Latitude mà không truyền Longitude → coi như không có location,
        // tránh tính khoảng cách sai trong SQL.
        var query = new ActiveCafesByBoardGameQueryDto
        {
            Latitude = 10.776889,
            Longitude = null,
            PageNumber = 1,
            PageSize = 20
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveCafesByBoardGameAsync(
                GameId,
                It.Is<double?>(v => v == null),
                It.Is<double?>(v => v == null),
                null,
                It.IsAny<PaginationParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<NearbyCafeDto>
            {
                Data = [],
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 0, TotalPages = 0 }
            })
            .Verifiable();

        var service = BuildService(gameRepo, cafeRepo: cafeRepo);
        await service.GetActiveCafesByBoardGameAsync(GameId, userId: null, query);

        cafeRepo.Verify();
    }

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_EmptyResult_ReturnsEmptyPage()
    {
        var query = new ActiveCafesByBoardGameQueryDto();
        var empty = new PaginatedResponse<NearbyCafeDto>
        {
            Data = [],
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 0, TotalPages = 0 }
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveCafesByBoardGameAsync(
                GameId,
                It.IsAny<double?>(),
                It.IsAny<double?>(),
                It.IsAny<string?>(),
                It.IsAny<PaginationParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(empty);

        var service = BuildService(gameRepo, cafeRepo: cafeRepo);
        var result = await service.GetActiveCafesByBoardGameAsync(GameId, userId: null, query);

        Assert.Empty(result.Cafes.Data);
        Assert.Equal(0, result.Cafes.Meta.TotalItems);
    }

    // =============== GetActiveCafesByBoardGameAsync — IsSaved ===============

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_AnonymousUser_IsSavedIsFalse()
    {
        // Player chưa đăng nhập (userId = null) → IsSaved luôn false, KHÔNG gọi save repository.
        var query = new ActiveCafesByBoardGameQueryDto();
        var cafesPage = new PaginatedResponse<NearbyCafeDto>
        {
            Data = [new NearbyCafeDto { Id = Guid.NewGuid(), Name = "Cafe A" }],
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 1, TotalPages = 1 }
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveCafesByBoardGameAsync(
                GameId, It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<string?>(),
                It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cafesPage);

        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>(MockBehavior.Strict);
        // KHÔNG setup ExistsAsync — nếu service gọi save repo với userId=null, mock sẽ throw.

        var service = BuildService(gameRepo, cafeRepo: cafeRepo, saveRepo: saveRepo);
        var result = await service.GetActiveCafesByBoardGameAsync(GameId, userId: null, query);

        Assert.False(result.IsSaved);
        Assert.Single(result.Cafes.Data);
        saveRepo.Verify(
            r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_LoggedInUser_GameSaved_IsSavedIsTrue()
    {
        // Player đã đăng nhập + game có trong danh sách yêu thích → IsSaved = true.
        var query = new ActiveCafesByBoardGameQueryDto();
        var userId = Guid.NewGuid();
        var cafesPage = new PaginatedResponse<NearbyCafeDto>
        {
            Data = [new NearbyCafeDto { Id = Guid.NewGuid(), Name = "Cafe A" }],
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 1, TotalPages = 1 }
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveCafesByBoardGameAsync(
                GameId, It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<string?>(),
                It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cafesPage);

        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.ExistsAsync(userId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Verifiable();

        var service = BuildService(gameRepo, cafeRepo: cafeRepo, saveRepo: saveRepo);
        var result = await service.GetActiveCafesByBoardGameAsync(GameId, userId, query);

        Assert.True(result.IsSaved);
        Assert.Single(result.Cafes.Data);
        saveRepo.Verify();
    }

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_LoggedInUser_GameNotSaved_IsSavedIsFalse()
    {
        // Player đã đăng nhập + game CHƯA có trong danh sách yêu thích → IsSaved = false.
        var query = new ActiveCafesByBoardGameQueryDto();
        var userId = Guid.NewGuid();
        var cafesPage = new PaginatedResponse<NearbyCafeDto>
        {
            Data = [new NearbyCafeDto { Id = Guid.NewGuid(), Name = "Cafe A" }],
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 1, TotalPages = 1 }
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveCafesByBoardGameAsync(
                GameId, It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<string?>(),
                It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cafesPage);

        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.ExistsAsync(userId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .Verifiable();

        var service = BuildService(gameRepo, cafeRepo: cafeRepo, saveRepo: saveRepo);
        var result = await service.GetActiveCafesByBoardGameAsync(GameId, userId, query);

        Assert.False(result.IsSaved);
        saveRepo.Verify();
    }

    [Fact]
    public async Task GetActiveCafesByBoardGameAsync_EmptyCafes_StillResolvesIsSaved()
    {
        // Edge case: 0 cafe match → IsSaved vẫn phải đúng (không phụ thuộc vào danh sách cafe).
        var query = new ActiveCafesByBoardGameQueryDto();
        var userId = Guid.NewGuid();
        var emptyCafes = new PaginatedResponse<NearbyCafeDto>
        {
            Data = [],
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 0, TotalPages = 0 }
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MultiplayerGame());

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveCafesByBoardGameAsync(
                GameId, It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<string?>(),
                It.IsAny<PaginationParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyCafes);

        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.ExistsAsync(userId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = BuildService(gameRepo, cafeRepo: cafeRepo, saveRepo: saveRepo);
        var result = await service.GetActiveCafesByBoardGameAsync(GameId, userId, query);

        Assert.Empty(result.Cafes.Data);
        Assert.True(result.IsSaved); // ← quan trọng: IsSaved resolve dù không có cafe nào
    }
}
