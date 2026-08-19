using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class CafeTableUpdateHelperTests
{
    private static readonly Guid CafeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TableId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CafeTable BuildTable(string name = "Bàn 1", int seatCount = 4, int sortOrder = 0) =>
        new()
        {
            Id = TableId,
            CafeId = CafeId,
            Name = name,
            SeatCount = seatCount,
            SortOrder = sortOrder,
            Status = CafeTableStatus.Available,
            IsActive = true
        };

    [Fact]
    public void ApplyUpdate_AllNullRequest_ThrowsBadRequest()
    {
        var table = BuildTable();
        var request = new UpdateCafeTableRequestDto();

        Assert.Throws<BadRequestException>(() =>
            CafeTableUpdateHelper.ApplyUpdate(table, request, Array.Empty<CafeTable>()));
    }

    [Fact]
    public void ApplyUpdate_SeatCountOnly_UpdatesSeatCount()
    {
        var table = BuildTable(seatCount: 4);
        var request = new UpdateCafeTableRequestDto { SeatCount = 8 };
        var before = DateTime.UtcNow;

        CafeTableUpdateHelper.ApplyUpdate(table, request, Array.Empty<CafeTable>());

        Assert.Equal(8, table.SeatCount);
        Assert.NotNull(table.UpdatedAt);
        Assert.True(table.UpdatedAt >= before);
    }

    [Fact]
    public void ApplyUpdate_SeatCountTooLow_ThrowsBadRequest()
    {
        var table = BuildTable();
        var request = new UpdateCafeTableRequestDto { SeatCount = 0 };

        Assert.Throws<BadRequestException>(() =>
            CafeTableUpdateHelper.ApplyUpdate(table, request, Array.Empty<CafeTable>()));
    }

    [Fact]
    public void ApplyUpdate_SeatCountTooHigh_ThrowsBadRequest()
    {
        var table = BuildTable();
        var request = new UpdateCafeTableRequestDto { SeatCount = 51 };

        var ex = Assert.Throws<BadRequestException>(() =>
            CafeTableUpdateHelper.ApplyUpdate(table, request, Array.Empty<CafeTable>()));
        Assert.Contains("Số ghế mỗi bàn phải từ 1 đến 50", ex.Message);
    }

    [Fact]
    public void ApplyUpdate_NameOnly_TrimsAndSetsName()
    {
        var table = BuildTable(name: "Bàn 1");
        var request = new UpdateCafeTableRequestDto { Name = "  Bàn VIP  " };

        CafeTableUpdateHelper.ApplyUpdate(table, request, Array.Empty<CafeTable>());

        Assert.Equal("Bàn VIP", table.Name);
    }

    [Fact]
    public void ApplyUpdate_BlankName_ThrowsBadRequest()
    {
        var table = BuildTable();
        var request = new UpdateCafeTableRequestDto { Name = "   " };

        Assert.Throws<BadRequestException>(() =>
            CafeTableUpdateHelper.ApplyUpdate(table, request, Array.Empty<CafeTable>()));
    }

    [Fact]
    public void ApplyUpdate_NameConflictsWithOtherTable_ThrowsConflict()
    {
        var table = BuildTable(name: "Bàn 1");
        var otherTable = new CafeTable
        {
            Id = Guid.NewGuid(),
            CafeId = CafeId,
            Name = "Bàn 2",
            IsActive = true
        };
        var request = new UpdateCafeTableRequestDto { Name = "Bàn 2" };

        var ex = Assert.Throws<ConflictException>(() =>
            CafeTableUpdateHelper.ApplyUpdate(table, request, new[] { table, otherTable }));
        Assert.Contains("Bàn 2", ex.Message);
    }

    [Fact]
    public void ApplyUpdate_NameConflictsIsCaseInsensitive()
    {
        var table = BuildTable(name: "Bàn 1");
        var otherTable = new CafeTable
        {
            Id = Guid.NewGuid(),
            CafeId = CafeId,
            Name = "BÀN 2",
            IsActive = true
        };
        var request = new UpdateCafeTableRequestDto { Name = "bàn 2" };

        Assert.Throws<ConflictException>(() =>
            CafeTableUpdateHelper.ApplyUpdate(table, request, new[] { table, otherTable }));
    }

    [Fact]
    public void ApplyUpdate_NameSetToSameOwnName_DoesNotThrow()
    {
        var table = BuildTable(name: "Bàn 1");
        var request = new UpdateCafeTableRequestDto { Name = "Bàn 1" };

        CafeTableUpdateHelper.ApplyUpdate(table, request, new[] { table });

        Assert.Equal("Bàn 1", table.Name);
    }

    [Fact]
    public void ApplyUpdate_SortOrderOnly_UpdatesSortOrder()
    {
        var table = BuildTable(sortOrder: 0);
        var request = new UpdateCafeTableRequestDto { SortOrder = 5 };

        CafeTableUpdateHelper.ApplyUpdate(table, request, Array.Empty<CafeTable>());

        Assert.Equal(5, table.SortOrder);
    }

    [Fact]
    public void ApplyUpdate_AllThreeFields_UpdatesAllAndSetsUpdatedAt()
    {
        var table = BuildTable(name: "Bàn 1", seatCount: 4, sortOrder: 0);
        var request = new UpdateCafeTableRequestDto
        {
            Name = "Bàn VIP",
            SeatCount = 10,
            SortOrder = 3
        };
        var before = DateTime.UtcNow;

        CafeTableUpdateHelper.ApplyUpdate(table, request, Array.Empty<CafeTable>());

        Assert.Equal("Bàn VIP", table.Name);
        Assert.Equal(10, table.SeatCount);
        Assert.Equal(3, table.SortOrder);
        Assert.NotNull(table.UpdatedAt);
        Assert.True(table.UpdatedAt >= before);
    }
}
