using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class GameTemplateServiceTests
{
    private readonly Mock<IGameTemplateRepository> _gameRepo = new();
    private readonly Mock<ICafeInventoryRepository> _inventoryRepo = new();
    private GameTemplateService CreateService() => new(_gameRepo.Object, _inventoryRepo.Object);

    private static GameTemplate BuildGameTemplate(Guid id, bool isActive = true)
    {
        return new GameTemplate
        {
            Id = id,
            Name = "Catan",
            Description = "Trade and build",
            MinPlayers = 3,
            MaxPlayers = 4,
            PlayTime = 90,
            BggId = 13,
            IsActive = isActive,
            Components = new List<GameComponentTemplate>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    GameTemplateId = id,
                    ComponentName = "Road",
                    ComponentKind = BoardGameComponentKind.Token,
                    DefaultQuantity = 15,
                    CreatedAt = DateTime.UtcNow
                }
            },
            Categories = new List<GameTemplateCategory>
            {
                new() { GameTemplateId = id, CategoryId = Guid.NewGuid(), Category = new Category { Id = Guid.NewGuid(), Name = "Strategy", SortOrder = 1 } }
            }
        };
    }

    [Fact]
    public async Task GetMasterGameByIdAsync_WhenNotFound_ThrowsBoardGameNotFound()
    {
        var id = Guid.NewGuid();
        _gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(id)).ReturnsAsync((GameTemplate?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<BoardGameNotFoundException>(() => svc.GetMasterGameByIdAsync(id));
    }

    [Fact]
    public async Task GetMasterGameByIdAsync_WithoutCafe_ReturnsDtoWithComponents()
    {
        var id = Guid.NewGuid();
        var game = BuildGameTemplate(id);
        _gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(id)).ReturnsAsync(game);

        var svc = CreateService();

        var result = await svc.GetMasterGameByIdAsync(id);

        Assert.Equal(id, result.Id);
        Assert.Equal("Catan", result.Name);
        Assert.Single(result.Components);
        Assert.Single(result.Categories);
        Assert.Null(result.AlreadyInInventory);
    }

    [Fact]
    public async Task GetMasterGameByIdAsync_WithCafe_MarksInventoryFlag()
    {
        var id = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var game = BuildGameTemplate(id);
        _gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(id)).ReturnsAsync(game);
        _inventoryRepo.Setup(r => r.GetActiveGameTemplateIdsByCafeAsync(cafeId)).ReturnsAsync(new HashSet<Guid> { id });

        var svc = CreateService();

        var result = await svc.GetMasterGameByIdAsync(id, cafeId);

        Assert.True(result.AlreadyInInventory);
    }

    [Fact]
    public async Task GetMasterGameByIdAsync_WithCafeGameNotInInventory_MarksFalse()
    {
        var id = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var game = BuildGameTemplate(id);
        _gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(id)).ReturnsAsync(game);
        _inventoryRepo.Setup(r => r.GetActiveGameTemplateIdsByCafeAsync(cafeId)).ReturnsAsync(new HashSet<Guid>());

        var svc = CreateService();

        var result = await svc.GetMasterGameByIdAsync(id, cafeId);

        Assert.False(result.AlreadyInInventory);
    }

    [Fact]
    public async Task GetMasterGamesAsync_WithoutCafe_PassesMetaThrough()
    {
        var query = new GetMasterGamesQuery { PageNumber = 1, PageSize = 20 };
        var paged = new PaginatedResponse<GameTemplate>
        {
            Data = new List<GameTemplate> { BuildGameTemplate(Guid.NewGuid()) },
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 1, TotalPages = 1 }
        };
        _gameRepo.Setup(r => r.GetPagedAsync(query)).ReturnsAsync(paged);

        var svc = CreateService();

        var result = await svc.GetMasterGamesAsync(query);

        Assert.Single(result.Data);
        Assert.Equal(1, result.Meta.TotalItems);
        Assert.Null(result.Data.First().AlreadyInInventory);
        _inventoryRepo.Verify(r => r.GetActiveGameTemplateIdsByCafeAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task GetMasterGamesAsync_WithCafe_MarksInventoryForEachGame()
    {
        var idInInventory = Guid.NewGuid();
        var idNotInInventory = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var query = new GetMasterGamesQuery { CafeId = cafeId, PageNumber = 1, PageSize = 20 };
        var paged = new PaginatedResponse<GameTemplate>
        {
            Data = new List<GameTemplate> { BuildGameTemplate(idInInventory), BuildGameTemplate(idNotInInventory) },
            Meta = new PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 2, TotalPages = 1 }
        };
        _gameRepo.Setup(r => r.GetPagedAsync(query)).ReturnsAsync(paged);
        _inventoryRepo.Setup(r => r.GetActiveGameTemplateIdsByCafeAsync(cafeId)).ReturnsAsync(new HashSet<Guid> { idInInventory });

        var svc = CreateService();

        var result = await svc.GetMasterGamesAsync(query);

        Assert.True(result.Data.Single(d => d.Id == idInInventory).AlreadyInInventory);
        Assert.False(result.Data.Single(d => d.Id == idNotInInventory).AlreadyInInventory);
    }
}