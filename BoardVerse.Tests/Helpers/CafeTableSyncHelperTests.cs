using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class CafeTableSyncHelperTests
{
    [Fact]
    public void ApplySync_AddsNewTablesForNewNames()
    {
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        CafeTableSyncHelper.ApplySync(cafeId, ["Table A", "Table B"], tables);

        Assert.Equal(2, tables.Count);
        Assert.Equal(0, tables[0].SortOrder);
        Assert.Equal(1, tables[1].SortOrder);
        Assert.All(tables, t => Assert.Equal(CafeTableStatus.Available, t.Status));
    }

    [Fact]
    public void ApplySync_ReactivatesInactiveTableWithSameName()
    {
        var cafeId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = existingId,
                CafeId = cafeId,
                Name = "Table A",
                SortOrder = 5,
                IsActive = false
            }
        };

        CafeTableSyncHelper.ApplySync(cafeId, ["Table A"], tables);

        Assert.Single(tables);
        Assert.True(tables[0].IsActive);
        Assert.Equal(0, tables[0].SortOrder);
        Assert.Equal(existingId, tables[0].Id);
    }

    [Fact]
    public void ApplySync_DeactivatesAvailableTableRemovedFromLayout()
    {
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                Name = "Old Table",
                Status = CafeTableStatus.Available,
                IsActive = true
            }
        };

        CafeTableSyncHelper.ApplySync(cafeId, ["New Table"], tables);

        Assert.Equal(2, tables.Count);
        var oldTable = tables.First(t => t.Name == "Old Table");
        Assert.False(oldTable.IsActive);
    }
}
