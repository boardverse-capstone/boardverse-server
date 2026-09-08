using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
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
        Mock<ICategoryRepository>? categoryRepo = null) =>
        new BoardGameService(
            gameRepo.Object,
            (categoryRepo ?? new Mock<ICategoryRepository>()).Object);

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
}
