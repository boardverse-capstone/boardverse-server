using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class CafePosServiceTests
{
    private static readonly Guid CafeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ManagerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TableId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");
    private static readonly Guid BoxId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid GameTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task StartGameSessionAsync_Success_StartsSessionAndUpdatesStatuses()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var table = new CafeTable { Id = TableId, CafeId = CafeId, Name = "T1", Status = CafeTableStatus.Available };
        var gameTemplate = new GameTemplate { Id = GameTemplateId, Name = "Catan", PlayTime = 90 };
        var inventory = new CafeGameInventory { Id = Guid.NewGuid(), CafeId = CafeId, GameTemplateId = GameTemplateId, GameTemplate = gameTemplate };
        var box = new CafeInventoryBox
        {
            Id = BoxId,
            Barcode = "BV-test-001",
            Status = CafeGameInventoryStatus.Available,
            CafeGameInventory = inventory
        };

        posRepo.Setup(r => r.GetTableAsync(CafeId, TableId)).ReturnsAsync(table);
        posRepo.Setup(r => r.GetBoxByBarcodeAsync(CafeId, "BV-test-001")).ReturnsAsync(box);
        posRepo.Setup(r => r.GetActiveSessionByBoxIdAsync(BoxId)).ReturnsAsync((ActiveSession?)null);

        var depositRepo = new Mock<IBookingDepositRepository>();
        var service = new CafePosService(posRepo.Object, cafeRepo.Object, depositRepo.Object);
        var result = await service.StartGameSessionAsync(CafeId, ManagerId, "Manager", new StartGameSessionRequestDto
        {
            CafeTableId = TableId,
            Barcode = "BV-test-001"
        });

        Assert.Equal(TableId, result.CafeTableId);
        Assert.Equal(ManagerId, result.HostId);
        Assert.Equal(CafeGameInventoryStatus.InUse, box.Status);
        Assert.Equal(CafeTableStatus.InUse, table.Status);
        posRepo.Verify(r => r.AddSessionAsync(It.IsAny<ActiveSession>()), Times.Once);
        posRepo.Verify(r => r.AddSessionMemberAsync(It.IsAny<ActiveSessionMember>()), Times.Once);
        posRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task StartGameSessionAsync_BoxNotAvailable_ThrowsConflict()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        posRepo.Setup(r => r.GetTableAsync(CafeId, TableId))
            .ReturnsAsync(new CafeTable { Id = TableId, Status = CafeTableStatus.Available });
        posRepo.Setup(r => r.GetBoxByBarcodeAsync(CafeId, "BV-test-001"))
            .ReturnsAsync(new CafeInventoryBox
            {
                Id = BoxId,
                Barcode = "BV-test-001",
                Status = CafeGameInventoryStatus.InUse,
                CafeGameInventory = new CafeGameInventory { GameTemplateId = GameTemplateId }
            });

        var depositRepo = new Mock<IBookingDepositRepository>();
        var service = new CafePosService(posRepo.Object, cafeRepo.Object, depositRepo.Object);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.StartGameSessionAsync(CafeId, ManagerId, "Manager", new StartGameSessionRequestDto
            {
                CafeTableId = TableId,
                Barcode = "BV-test-001"
            }));
    }

    [Fact]
    public async Task StartGameSessionAsync_ReservedTable_ThrowsConflict()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        posRepo.Setup(r => r.GetTableAsync(CafeId, TableId))
            .ReturnsAsync(new CafeTable { Id = TableId, Status = CafeTableStatus.Reserved });

        var depositRepo = new Mock<IBookingDepositRepository>();
        var service = new CafePosService(posRepo.Object, cafeRepo.Object, depositRepo.Object);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.StartGameSessionAsync(CafeId, ManagerId, "Manager", new StartGameSessionRequestDto
            {
                CafeTableId = TableId,
                Barcode = "BV-test-001"
            }));
    }

    [Fact]
    public async Task EndGameSessionAsync_ReleasesBoxAndTable()
    {
        var sessionId = Guid.NewGuid();
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var table = new CafeTable { Id = TableId, Status = CafeTableStatus.InUse, Name = "T1" };
        var box = new CafeInventoryBox { Id = BoxId, Barcode = "BV-test-001", Status = CafeGameInventoryStatus.InUse };
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = CafeId,
            CafeTableId = TableId,
            CafeTable = table,
            CafeInventoryBoxId = BoxId,
            CafeInventoryBox = box,
            GameTemplateId = GameTemplateId,
            GameTemplate = new GameTemplate { Name = "Catan", PlayTime = 90 },
            StartedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        posRepo.Setup(r => r.GetActiveSessionByIdAsync(CafeId, sessionId)).ReturnsAsync(session);
        posRepo.Setup(r => r.GetActiveSessionsAsync(CafeId, null)).ReturnsAsync([]);

        var depositRepo = new Mock<IBookingDepositRepository>();
        var service = new CafePosService(posRepo.Object, cafeRepo.Object, depositRepo.Object);
        var result = await service.EndGameSessionAsync(CafeId, ManagerId, "Manager", sessionId);

        Assert.Equal(CafeGameInventoryStatus.Available, box.Status);
        Assert.Equal(CafeTableStatus.Available, table.Status);
        Assert.Equal(sessionId, result.Id);
    }

    [Fact]
    public async Task GetBoxByBarcodeAsync_EmptyBarcode_ThrowsBadRequest()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var depositRepo = new Mock<IBookingDepositRepository>();
        var service = new CafePosService(posRepo.Object, cafeRepo.Object, depositRepo.Object);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetBoxByBarcodeAsync(CafeId, ManagerId, "Manager", "   "));
    }

    [Fact]
    public async Task GetTablesAsync_NoAccess_ThrowsForbidden()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Player")).ReturnsAsync(false);

        var depositRepo = new Mock<IBookingDepositRepository>();
        var service = new CafePosService(posRepo.Object, cafeRepo.Object, depositRepo.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetTablesAsync(CafeId, ManagerId, "Player"));
    }

    private static void SetupActiveCafe(Mock<ICafeRepository> cafeRepo) =>
        cafeRepo.Setup(r => r.GetActiveByIdAsync(CafeId))
            .ReturnsAsync(new Cafe { Id = CafeId, Name = "Demo", Address = "Addr", IsActive = true });
}
