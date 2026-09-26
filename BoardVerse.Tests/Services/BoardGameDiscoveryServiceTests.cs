using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Discovery;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho BoardGameDiscoveryService — survey scoring, save/unsave, saved-games list.
/// </summary>
public class BoardGameDiscoveryServiceTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GameId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static IBoardGameDiscoveryService BuildService(
        Mock<IGameTemplateRepository> gameRepo,
        Mock<IPlayerBoardGameSaveRepository> saveRepo,
        Mock<ICategoryRepository> categoryRepo,
        Mock<ICafeService> cafeService,
        Mock<ILobbyService> lobbyService,
        Mock<IActiveSessionRepository> activeSessionRepo)
    {
        // SetupDefaults MUST run before any test-specific Setup because Moq uses the LAST matching setup.
        // Tests should call SetupDefaults(...) first, then add their specific setups, then this method.
        return new BoardGameDiscoveryService(
            gameRepo.Object,
            saveRepo.Object,
            categoryRepo.Object,
            cafeService.Object,
            lobbyService.Object,
            activeSessionRepo.Object);
    }

    private static void SetupDefaults(
        Mock<IPlayerBoardGameSaveRepository> saveRepo,
        Mock<ICategoryRepository> categoryRepo,
        Mock<ILobbyService> lobbyService,
        Mock<IActiveSessionRepository> activeSessionRepo)
    {
        lobbyService.Setup(l => l.GetDiscoverableLobbiesAsync(
            It.IsAny<Guid?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<double?>(), It.IsAny<int>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new List<LobbyResponseDto>());

        categoryRepo.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Category>());

        saveRepo.Setup(r => r.GetSavedGameTemplateIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // ExtractPreferencesFromSavedGames calls GetByUserAsync — must return empty so the method returns null
        // instead of throwing NullReferenceException when accessing saves.Count.
        saveRepo.Setup(r => r.GetByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerBoardGameSave>());

        // Default: user has no play history (no penalty applied). Tests that need a populated history override this.
        activeSessionRepo.Setup(r => r.GetUserPlayHistoryAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid GameTemplateId, string GameName, int PlayCount, DateTime? LastPlayedAt, int TotalMinutesPlayed)>());
    }

    private static GameTemplate BuildGame(
        Guid id,
        string name,
        int minPlayers,
        int maxPlayers,
        int playTime,
        List<(Guid Id, string Name, string Slug)>? categories = null) =>
        new()
        {
            Id = id,
            Name = name,
            Description = name + " desc",
            ThumbnailUrl = $"https://cdn/{name}.jpg",
            MinPlayers = minPlayers,
            MaxPlayers = maxPlayers,
            PlayTime = playTime,
            Categories = (categories ?? new List<(Guid, string, string)>())
                .Select(c => new GameTemplateCategory
                {
                    CategoryId = c.Id,
                    Category = new Category { Id = c.Id, Name = c.Name, Slug = c.Slug }
                })
                .ToList(),
            Components = []
        };

    // ===================== RunSurveyAsync =====================

    [Fact]
    public async Task RunSurveyAsync_PlayerCountBelow1_ThrowsBadRequest()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var request = new BoardGameSurveyRequestDto { PlayerCount = 0 };

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => service.RunSurveyAsync(request, UserId, null, null));
        Assert.Contains("1", ex.Message);
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public async Task RunSurveyAsync_PlayerCountAbove5_ThrowsBadRequest()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var request = new BoardGameSurveyRequestDto { PlayerCount = 6 };

        await Assert.ThrowsAsync<BadRequestException>(
            () => service.RunSurveyAsync(request, UserId, null, null));
    }

    [Fact]
    public async Task RunSurveyAsync_EmptyResults_ReturnsZeroGames()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = [],
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 0, TotalPages = 0 }
            });
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var request = new BoardGameSurveyRequestDto { PlayerCount = 4 };

        var result = await service.RunSurveyAsync(request, UserId, null, null);

        Assert.NotNull(result);
        Assert.Empty(result.Games);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task RunSurveyAsync_WithSavedGames_MarksIsSavedCorrectly()
    {
        var savedId = GameId;
        var otherId = Guid.NewGuid();

        var games = new List<GameTemplate>
        {
            BuildGame(savedId, "Catan", 3, 4, 60),
            BuildGame(otherId, "Splendor", 2, 4, 30),
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = games,
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 2, TotalPages = 1 }
            });
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        saveRepo.Setup(r => r.GetSavedGameTemplateIdsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { savedId });
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var request = new BoardGameSurveyRequestDto { PlayerCount = 4 };

        var result = await service.RunSurveyAsync(request, UserId, null, null);

        Assert.Equal(2, result.Games.Count);
        var saved = result.Games.First(g => g.Id == savedId);
        var unsaved = result.Games.First(g => g.Id == otherId);
        Assert.True(saved.IsSaved);
        Assert.False(unsaved.IsSaved);
    }

    [Fact]
    public async Task RunSurveyAsync_NoUserId_DoesNotSetIsSaved()
    {
        var games = new List<GameTemplate>
        {
            BuildGame(GameId, "Catan", 3, 4, 60),
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = games,
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 1, TotalPages = 1 }
            });
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var request = new BoardGameSurveyRequestDto { PlayerCount = 4 };

        var result = await service.RunSurveyAsync(request, null, null, null);

        Assert.Single(result.Games);
        Assert.False(result.Games[0].IsSaved);
        saveRepo.Verify(r => r.GetSavedGameTemplateIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunSurveyAsync_PlayerCount5_UsesMaxPlayers()
    {
        var games = new List<GameTemplate>
        {
            BuildGame(GameId, "Catan", 3, 6, 60),
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = games,
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 1, TotalPages = 1 }
            });
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var request = new BoardGameSurveyRequestDto { PlayerCount = 5 };

        var result = await service.RunSurveyAsync(request, null, null, null);

        Assert.Equal(5, result.SurveyedPlayerCount);
    }

    [Fact]
    public async Task RunSurveyAsync_CategoryMatch_AddsScore()
    {
        var games = new List<GameTemplate>
        {
            BuildGame(GameId, "Catan", 3, 4, 60, new List<(Guid, string, string)> { (CategoryId, "Strategy", "chien-thuat") }),
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = games,
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 1, TotalPages = 1 }
            });
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var request = new BoardGameSurveyRequestDto
        {
            PlayerCount = 4,
            CategoryIds = new List<Guid> { CategoryId }
        };

        var result = await service.RunSurveyAsync(request, null, null, null);

        // 40 category + 30 duration (60min = 30to60) + 30 player range fit (3-4 tight) = 100
        Assert.True(result.Games[0].MatchScore > 50,
            $"Score should reflect category + duration + fit, but was {result.Games[0].MatchScore}");
    }

    [Fact]
    public async Task RunSurveyAsync_InvalidDuration_ThrowsBadRequest()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var request = new BoardGameSurveyRequestDto
        {
            PlayerCount = 4,
            PreferredDurations = new List<string> { "invalid_duration" }
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => service.RunSurveyAsync(request, null, null, null));
        Assert.Contains("PreferredDurations", ex.Message);
    }

    [Fact]
    public async Task RunSurveyAsync_NoLocation_SkipsNearestCafeLookup()
    {
        var games = new List<GameTemplate>
        {
            BuildGame(GameId, "Catan", 3, 4, 60),
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = games,
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 1, TotalPages = 1 }
            });
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var request = new BoardGameSurveyRequestDto { PlayerCount = 4 };

        var result = await service.RunSurveyAsync(request, UserId, null, null);

        Assert.Null(result.NearestCafe);
        cafeService.Verify(c => c.GetNearbyCafesAsync(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<PaginationParams>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===================== ToggleSaveAsync =====================

    [Fact]
    public async Task ToggleSaveAsync_GameNotFound_ThrowsBoardGameNotFound()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.ExistsAsync(GameId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        await Assert.ThrowsAsync<BoardGameNotFoundException>(
            () => service.ToggleSaveAsync(UserId, GameId));
    }

    [Fact]
    public async Task ToggleSaveAsync_NotYetSaved_AddsSaveRecord()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.ExistsAsync(GameId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.GetByUserAndGameAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerBoardGameSave?)null);
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var result = await service.ToggleSaveAsync(UserId, GameId);

        Assert.True(result.IsSaved);
        Assert.NotNull(result.SavedAt);
        saveRepo.Verify(r => r.AddAsync(It.IsAny<PlayerBoardGameSave>(), It.IsAny<CancellationToken>()), Times.Once);
        saveRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToggleSaveAsync_AlreadySaved_RemovesSaveRecord()
    {
        var existingSave = new PlayerBoardGameSave
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            GameTemplateId = GameId,
            SavedAt = DateTime.UtcNow.AddDays(-1)
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.ExistsAsync(GameId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.GetByUserAndGameAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSave);
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var result = await service.ToggleSaveAsync(UserId, GameId);

        Assert.False(result.IsSaved);
        Assert.Null(result.SavedAt);
        saveRepo.Verify(r => r.DeleteAsync(UserId, GameId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===================== UnsaveGameAsync =====================

    [Fact]
    public async Task UnsaveGameAsync_GameNotFound_ThrowsBoardGameNotFound()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.ExistsAsync(GameId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        await Assert.ThrowsAsync<BoardGameNotFoundException>(
            () => service.UnsaveGameAsync(UserId, GameId));
    }

    [Fact]
    public async Task UnsaveGameAsync_SaveNotFound_ThrowsNotFound()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.ExistsAsync(GameId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.GetByUserAndGameAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerBoardGameSave?)null);
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.UnsaveGameAsync(UserId, GameId));
    }

    [Fact]
    public async Task UnsaveGameAsync_HappyPath_DeletesAndReturns()
    {
        var existingSave = new PlayerBoardGameSave
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            GameTemplateId = GameId,
            SavedAt = DateTime.UtcNow.AddDays(-2)
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.ExistsAsync(GameId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.GetByUserAndGameAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSave);
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var result = await service.UnsaveGameAsync(UserId, GameId);

        Assert.False(result.IsSaved);
        Assert.Equal(GameId, result.GameTemplateId);
        saveRepo.Verify(r => r.DeleteAsync(UserId, GameId, It.IsAny<CancellationToken>()), Times.Once);
        saveRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===================== GetSavedGamesAsync =====================

    [Fact]
    public async Task GetSavedGamesAsync_NoSaves_ReturnsEmptyList()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.GetByUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerBoardGameSave>());
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        SetupDefaults(saveRepo, categoryRepo, lobbyService, activeSessionRepo);
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var result = await service.GetSavedGamesAsync(UserId);

        Assert.Empty(result);
        gameRepo.Verify(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetSavedGamesAsync_WithSaves_ReturnsMatchingGames()
    {
        var savedGameId = GameId;
        var savedRecord = new PlayerBoardGameSave
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            GameTemplateId = savedGameId,
            SavedAt = DateTime.UtcNow
        };
        var allGames = new List<GameTemplate>
        {
            BuildGame(savedGameId, "Catan", 3, 4, 60),
            BuildGame(Guid.NewGuid(), "Splendor", 2, 4, 30),
        };

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetBoardGamesPagedAsync(It.IsAny<GetMasterGamesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResponse<GameTemplate>
            {
                Data = allGames,
                Meta = new PaginationMeta { CurrentPage = 1, PageSize = 100, TotalItems = 2, TotalPages = 1 }
            });
        var saveRepo = new Mock<IPlayerBoardGameSaveRepository>();
        saveRepo.Setup(r => r.GetByUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerBoardGameSave> { savedRecord });
        var categoryRepo = new Mock<ICategoryRepository>();
        var cafeService = new Mock<ICafeService>();
        var lobbyService = new Mock<ILobbyService>();
        lobbyService.Setup(l => l.GetDiscoverableLobbiesAsync(
            It.IsAny<Guid>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<double?>(), It.IsAny<int>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new List<LobbyResponseDto>());
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        var service = BuildService(gameRepo, saveRepo, categoryRepo, cafeService, lobbyService, activeSessionRepo);

        var result = await service.GetSavedGamesAsync(UserId);

        Assert.Single(result);
        Assert.Equal("Catan", result[0].GameName);
        Assert.Equal(savedRecord.Id, result[0].Id);
    }
}