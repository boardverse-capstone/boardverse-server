using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class AdminMasterCatalogServiceTests
{
    private readonly Mock<ICategoryRepository> _categoryRepo = new();
    private readonly Mock<IGameComponentTemplateRepository> _componentRepo = new();
    private readonly Mock<IGameTemplateRepository> _gameRepo = new();

    private AdminMasterCatalogService CreateService() => new(
        _categoryRepo.Object, _componentRepo.Object, _gameRepo.Object);

    private static Category BuildCategory(Guid id, string name = "Strategy", string slug = "strategy", bool isActive = true)
    {
        return new Category
        {
            Id = id,
            Name = name,
            Slug = slug,
            Description = "Test",
            SortOrder = 1,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    #region Categories

    [Fact]
    public async Task GetCategoriesAsync_ReturnsMappedList()
    {
        _categoryRepo.Setup(r => r.GetAllAsync(true)).ReturnsAsync(new List<Category>
        {
            BuildCategory(Guid.NewGuid(), "Strategy", "strategy"),
            BuildCategory(Guid.NewGuid(), "Family", "family")
        });

        var svc = CreateService();

        var result = await svc.GetCategoriesAsync(true);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task CreateCategoryAsync_WithEmptyName_ThrowsBadRequest()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() => svc.CreateCategoryAsync(new AdminCreateCategoryRequestDto
        {
            Name = "   "
        }));
    }

    [Fact]
    public async Task CreateCategoryAsync_WhenSlugTaken_ThrowsConflict()
    {
        _categoryRepo.Setup(r => r.SlugExistsAsync("strategy", null)).ReturnsAsync(true);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateCategoryAsync(new AdminCreateCategoryRequestDto
        {
            Name = "Strategy",
            Slug = "strategy"
        }));
    }

    [Fact]
    public async Task CreateCategoryAsync_WithoutSlug_GeneratesFromNameAndPersists()
    {
        _categoryRepo.Setup(r => r.SlugExistsAsync(It.IsAny<string>(), null)).ReturnsAsync(false);

        Category? captured = null;
        _categoryRepo.Setup(r => r.AddAsync(It.IsAny<Category>()))
            .Callback<Category>(c => captured = c)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        var result = await svc.CreateCategoryAsync(new AdminCreateCategoryRequestDto
        {
            Name = "Chiến Thuật",
            SortOrder = 5
        });

        Assert.NotNull(captured);
        Assert.Equal("chien-thuat", captured!.Slug);
        Assert.Equal("Chiến Thuật", captured.Name);
        Assert.True(captured.IsActive);
        _categoryRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal("chien-thuat", result.Slug);
    }

    [Fact]
    public async Task UpdateCategoryAsync_WhenNotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _categoryRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Category?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.UpdateCategoryAsync(id, new AdminUpdateCategoryRequestDto()));
    }

    [Fact]
    public async Task UpdateCategoryAsync_PartialUpdate_AppliesFields()
    {
        var id = Guid.NewGuid();
        var category = BuildCategory(id, "Strategy", "strategy");
        _categoryRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(category);

        var svc = CreateService();

        await svc.UpdateCategoryAsync(id, new AdminUpdateCategoryRequestDto
        {
            Name = "Updated",
            Description = "  Updated description  ",
            SortOrder = 10,
            IsActive = false
        });

        Assert.Equal("Updated", category.Name);
        Assert.Equal("Updated description", category.Description);
        Assert.Equal(10, category.SortOrder);
        Assert.False(category.IsActive);
        _categoryRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteCategoryAsync_NotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _categoryRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Category?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.DeleteCategoryAsync(id));
    }

    [Fact]
    public async Task DeleteCategoryAsync_Found_SoftDeletes()
    {
        var id = Guid.NewGuid();
        var category = BuildCategory(id);
        _categoryRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(category);

        var svc = CreateService();

        await svc.DeleteCategoryAsync(id);

        Assert.False(category.IsActive);
        _categoryRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region Components

    [Fact]
    public async Task GetGameComponentsAsync_GameMissing_ThrowsNotFound()
    {
        var gameId = Guid.NewGuid();
        _gameRepo.Setup(r => r.ExistsAsync(gameId)).ReturnsAsync(false);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetGameComponentsAsync(gameId));
    }

    [Fact]
    public async Task GetGameComponentsAsync_WhenExists_ReturnsComponents()
    {
        var gameId = Guid.NewGuid();
        _gameRepo.Setup(r => r.ExistsAsync(gameId)).ReturnsAsync(true);
        _componentRepo.Setup(r => r.GetByGameTemplateIdAsync(gameId)).ReturnsAsync(new List<GameComponentTemplate>
        {
            new()
            {
                Id = Guid.NewGuid(),
                GameTemplateId = gameId,
                ComponentName = "Road",
                ComponentKind = BoardGameComponentKind.Token,
                DefaultQuantity = 15,
                CreatedAt = DateTime.UtcNow
            }
        });

        var svc = CreateService();

        var result = await svc.GetGameComponentsAsync(gameId);

        Assert.Single(result);
        Assert.Equal("Road", result[0].ComponentName);
    }

    [Fact]
    public async Task CreateGameComponentAsync_InvalidKind_ThrowsBadRequest()
    {
        var gameId = Guid.NewGuid();
        _gameRepo.Setup(r => r.ExistsAsync(gameId)).ReturnsAsync(true);

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() => svc.CreateGameComponentAsync(gameId,
            new AdminCreateGameComponentRequestDto
            {
                ComponentName = "X",
                ComponentKind = (BoardGameComponentKind)9999
            }));
    }

    [Fact]
    public async Task CreateGameComponentAsync_NoKind_ResolvesFromNameAndAdds()
    {
        var gameId = Guid.NewGuid();
        _gameRepo.Setup(r => r.ExistsAsync(gameId)).ReturnsAsync(true);

        GameComponentTemplate? captured = null;
        _componentRepo.Setup(r => r.AddAsync(It.IsAny<GameComponentTemplate>()))
            .Callback<GameComponentTemplate>(c => captured = c)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        var result = await svc.CreateGameComponentAsync(gameId, new AdminCreateGameComponentRequestDto
        {
            ComponentName = "Dice",
            DefaultQuantity = 2
        });

        Assert.NotNull(captured);
        Assert.Equal("Dice", captured!.ComponentName);
        Assert.Equal(2, captured.DefaultQuantity);
        _componentRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal("Dice", result.ComponentName);
    }

    [Fact]
    public async Task UpdateGameComponentAsync_ComponentNotFound_ThrowsNotFound()
    {
        var gameId = Guid.NewGuid();
        var compId = Guid.NewGuid();
        _gameRepo.Setup(r => r.ExistsAsync(gameId)).ReturnsAsync(true);
        _componentRepo.Setup(r => r.GetByIdAndGameTemplateIdAsync(compId, gameId)).ReturnsAsync((GameComponentTemplate?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.UpdateGameComponentAsync(gameId, compId, new AdminUpdateGameComponentRequestDto()));
    }

    [Fact]
    public async Task UpdateGameComponentAsync_Valid_UpdatesAndSaves()
    {
        var gameId = Guid.NewGuid();
        var compId = Guid.NewGuid();
        var component = new GameComponentTemplate
        {
            Id = compId,
            GameTemplateId = gameId,
            ComponentName = "Old",
            ComponentKind = BoardGameComponentKind.Token,
            DefaultQuantity = 10,
            CreatedAt = DateTime.UtcNow
        };
        _gameRepo.Setup(r => r.ExistsAsync(gameId)).ReturnsAsync(true);
        _componentRepo.Setup(r => r.GetByIdAndGameTemplateIdAsync(compId, gameId)).ReturnsAsync(component);

        var svc = CreateService();

        await svc.UpdateGameComponentAsync(gameId, compId, new AdminUpdateGameComponentRequestDto
        {
            ComponentName = "New",
            DefaultQuantity = 20
        });

        Assert.Equal("New", component.ComponentName);
        Assert.Equal(20, component.DefaultQuantity);
        _componentRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteGameComponentAsync_WhenReferenced_ThrowsConflict()
    {
        var gameId = Guid.NewGuid();
        var compId = Guid.NewGuid();
        _gameRepo.Setup(r => r.ExistsAsync(gameId)).ReturnsAsync(true);
        _componentRepo.Setup(r => r.GetByIdAndGameTemplateIdAsync(compId, gameId)).ReturnsAsync(new GameComponentTemplate { Id = compId, GameTemplateId = gameId });
        _componentRepo.Setup(r => r.IsReferencedByInventoryPenaltyAsync(compId)).ReturnsAsync(true);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.DeleteGameComponentAsync(gameId, compId));
    }

    [Fact]
    public async Task DeleteGameComponentAsync_NotReferenced_Removes()
    {
        var gameId = Guid.NewGuid();
        var compId = Guid.NewGuid();
        var component = new GameComponentTemplate { Id = compId, GameTemplateId = gameId };
        _gameRepo.Setup(r => r.ExistsAsync(gameId)).ReturnsAsync(true);
        _componentRepo.Setup(r => r.GetByIdAndGameTemplateIdAsync(compId, gameId)).ReturnsAsync(component);
        _componentRepo.Setup(r => r.IsReferencedByInventoryPenaltyAsync(compId)).ReturnsAsync(false);

        var svc = CreateService();

        await svc.DeleteGameComponentAsync(gameId, compId);

        _componentRepo.Verify(r => r.Remove(component), Times.Once);
        _componentRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region Game Categories / BoardGames

    [Fact]
    public async Task GetGameCategoriesAsync_WhenGameMissing_Throws()
    {
        var gameId = Guid.NewGuid();
        _gameRepo.Setup(r => r.GetByIdWithComponentsAsync(gameId)).ReturnsAsync((GameTemplate?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetGameCategoriesAsync(gameId));
    }

    [Fact]
    public async Task GetGameCategoriesAsync_ReturnsActiveSorted()
    {
        var gameId = Guid.NewGuid();
        var game = new GameTemplate
        {
            Id = gameId,
            Categories = new List<GameTemplateCategory>
            {
                new() { GameTemplateId = gameId, Category = BuildCategory(Guid.NewGuid(), "Active1", "a1"), CategoryId = Guid.NewGuid() },
                new() { GameTemplateId = gameId, Category = BuildCategory(Guid.NewGuid(), "Inactive", "i", isActive: false), CategoryId = Guid.NewGuid() }
            }
        };
        _gameRepo.Setup(r => r.GetByIdWithComponentsAsync(gameId)).ReturnsAsync(game);

        var svc = CreateService();

        var result = await svc.GetGameCategoriesAsync(gameId);

        Assert.Single(result);
        Assert.Equal("Active1", result[0].Name);
    }

    [Fact]
    public async Task SetGameCategoriesAsync_GameMissing_Throws()
    {
        var gameId = Guid.NewGuid();
        _gameRepo.Setup(r => r.GetByIdWithCategoriesForUpdateAsync(gameId)).ReturnsAsync((GameTemplate?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.SetGameCategoriesAsync(gameId,
            new AdminSetGameCategoriesRequestDto { CategoryIds = new List<Guid> { Guid.NewGuid() } }));
    }

    [Fact]
    public async Task SetGameCategoriesAsync_AllExistingIds_AssignsAndReturns()
    {
        var gameId = Guid.NewGuid();
        var cat1 = Guid.NewGuid();
        var cat2 = Guid.NewGuid();
        var game = new GameTemplate { Id = gameId, Categories = new List<GameTemplateCategory>() };
        _gameRepo.Setup(r => r.GetByIdWithCategoriesForUpdateAsync(gameId)).ReturnsAsync(game);
        _categoryRepo.Setup(r => r.CountByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), true)).ReturnsAsync(2);
        // After updating, service calls GetGameCategoriesAsync which uses GetByIdWithComponentsAsync; populate active categories
        var refreshedGame = new GameTemplate
        {
            Id = gameId,
            Categories = new List<GameTemplateCategory>
            {
                new() { GameTemplateId = gameId, Category = BuildCategory(cat1, "A", "a"), CategoryId = cat1 },
                new() { GameTemplateId = gameId, Category = BuildCategory(cat2, "B", "b"), CategoryId = cat2 }
            }
        };
        _gameRepo.Setup(r => r.GetByIdWithComponentsAsync(gameId)).ReturnsAsync(refreshedGame);

        var svc = CreateService();

        await svc.SetGameCategoriesAsync(gameId, new AdminSetGameCategoriesRequestDto
        {
            CategoryIds = new List<Guid> { cat1, cat2 }
        });

        Assert.Equal(2, game.Categories.Count);
        _gameRepo.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdateBoardGameAsync_WhenGameMissing_Throws()
    {
        var gameId = Guid.NewGuid();
        _gameRepo.Setup(r => r.GetByIdForUpdateAsync(gameId)).ReturnsAsync((GameTemplate?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.UpdateBoardGameAsync(gameId, new AdminUpdateBoardGameRequestDto()));
    }

    [Fact]
    public async Task UpdateBoardGameAsync_Valid_UpdatesFields()
    {
        var gameId = Guid.NewGuid();
        var game = new GameTemplate
        {
            Id = gameId,
            Name = "Old",
            MinPlayers = 3,
            MaxPlayers = 4,
            PlayTime = 60
        };
        _gameRepo.Setup(r => r.GetByIdForUpdateAsync(gameId)).ReturnsAsync(game);

        var svc = CreateService();

        var result = await svc.UpdateBoardGameAsync(gameId, new AdminUpdateBoardGameRequestDto
        {
            Name = "  New Name  ",
            MinPlayers = 2,
            MaxPlayers = 6,
            PlayTime = 90,
            IsActive = false
        });

        Assert.Equal("New Name", game.Name);
        Assert.Equal(2, game.MinPlayers);
        Assert.Equal(6, game.MaxPlayers);
        Assert.Equal(90, game.PlayTime);
        Assert.False(game.IsActive);
        _gameRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal("New Name", result.Name);
    }

    [Fact]
    public async Task UpdateThumbnailAsync_Valid_TrimsAndSaves()
    {
        var gameId = Guid.NewGuid();
        var game = new GameTemplate { Id = gameId, Name = "X" };
        _gameRepo.Setup(r => r.GetByIdForUpdateAsync(gameId)).ReturnsAsync(game);

        var svc = CreateService();

        var result = await svc.UpdateThumbnailAsync(gameId, new AdminUpdateThumbnailRequestDto
        {
            ThumbnailUrl = "  https://cdn.example/x.png  "
        });

        Assert.Equal("https://cdn.example/x.png", game.ThumbnailUrl);
        _gameRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal("https://cdn.example/x.png", result.ThumbnailUrl);
    }

    #endregion
}