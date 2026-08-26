using BoardVerse.Core.DTOs.Inventory;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

using System.Threading;
namespace BoardVerse.Tests.Services;

public class CafeInventoryServiceTests
{
    private static readonly Guid CafeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ManagerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GameTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AddToInventoryAsync_GameAlreadyInInventory_ThrowsConflict()
    {
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetByIdAsync(CafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = CafeId, ManagerId = ManagerId, Name = "Demo", Address = "Addr" });

        var inventoryRepo = new Mock<ICafeInventoryRepository>();
        inventoryRepo.Setup(r => r.GetByCafeAndGameTemplateAsync(CafeId, GameTemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CafeGameInventory { Id = Guid.NewGuid(), CafeId = CafeId, GameTemplateId = GameTemplateId });

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameTemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameTemplate { Id = GameTemplateId, Name = "Catan", MinPlayers = 2, MaxPlayers = 4, PlayTime = 60 });

        var service = new CafeInventoryService(cafeRepo.Object, inventoryRepo.Object, gameRepo.Object);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.AddToInventoryAsync(CafeId, ManagerId, new AddCafeInventoryRequestDto
            {
                GameTemplateId = GameTemplateId,
                BoxQuantity = 2,
                Status = CafeGameInventoryStatus.Available
            }));
    }

    [Fact]
    public async Task AddToInventoryAsync_UnknownGame_ThrowsNotFound()
    {
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetByIdAsync(CafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = CafeId, ManagerId = ManagerId, Name = "Demo", Address = "Addr" });

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameTemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameTemplate?)null);

        var service = new CafeInventoryService(cafeRepo.Object, new Mock<ICafeInventoryRepository>().Object, gameRepo.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AddToInventoryAsync(CafeId, ManagerId, new AddCafeInventoryRequestDto
            {
                GameTemplateId = GameTemplateId,
                BoxQuantity = 1,
                Status = CafeGameInventoryStatus.Available
            }));
    }

    [Fact]
    public async Task AddToInventoryAsync_Success_CreatesInventoryAndSyncsBoxes()
    {
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetByIdAsync(CafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = CafeId, ManagerId = ManagerId, Name = "Demo", Address = "Addr" });

        var inventoryRepo = new Mock<ICafeInventoryRepository>();
        inventoryRepo.Setup(r => r.GetByCafeAndGameTemplateAsync(CafeId, GameTemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeGameInventory?)null);
        inventoryRepo.Setup(r => r.GetByCafeAndGameTemplateIncludingInactiveAsync(CafeId, GameTemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeGameInventory?)null);
        inventoryRepo.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new CafeGameInventory
            {
                Id = id,
                CafeId = CafeId,
                GameTemplateId = GameTemplateId,
                BoxQuantity = 2,
                Status = CafeGameInventoryStatus.Available,
                GameTemplate = new GameTemplate { Id = GameTemplateId, Name = "Catan" },
                Boxes = []
            });

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameTemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameTemplate
            {
                Id = GameTemplateId,
                Name = "Catan",
                MinPlayers = 2,
                MaxPlayers = 4,
                PlayTime = 60,
                Components = []
            });

        var service = new CafeInventoryService(cafeRepo.Object, inventoryRepo.Object, gameRepo.Object);
        var result = await service.AddToInventoryAsync(CafeId, ManagerId, new AddCafeInventoryRequestDto
        {
            GameTemplateId = GameTemplateId,
            BoxQuantity = 2,
            Status = CafeGameInventoryStatus.Available
        });

        Assert.Equal(GameTemplateId, result.GameTemplateId);
        inventoryRepo.Verify(r => r.AddAsync(It.IsAny<CafeGameInventory>(), It.IsAny<CancellationToken>()), Times.Once);
        inventoryRepo.Verify(r => r.SyncInventoryBoxesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        inventoryRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
