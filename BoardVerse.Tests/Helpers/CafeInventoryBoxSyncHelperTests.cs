using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class CafeInventoryBoxSyncHelperTests
{
    [Fact]
    public void FormatBarcode_UsesExpectedPattern()
    {
        var cafeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var inventoryId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var barcode = CafeInventoryBoxSyncHelper.FormatBarcode(cafeId, inventoryId, 1);

        Assert.Equal("BV-bbbbbbbb-cccccccc-001", barcode);
    }

    [Fact]
    public void ApplySync_IncreasesBoxesToTargetQuantity()
    {
        var cafeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var inventory = new CafeGameInventory
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            BoxQuantity = 3
        };
        var boxes = new List<CafeInventoryBox>();

        CafeInventoryBoxSyncHelper.ApplySync(inventory, boxes);

        Assert.Equal(3, boxes.Count);
        Assert.All(boxes, b =>
        {
            Assert.True(b.IsActive);
            Assert.Equal(CafeGameInventoryStatus.Available, b.Status);
        });
    }

    [Fact]
    public void ApplySync_RemovesOnlyAvailableBoxesWhenOverTarget()
    {
        var cafeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var inventory = new CafeGameInventory
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            BoxQuantity = 1
        };
        var boxes = new List<CafeInventoryBox>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CafeGameInventoryId = inventory.Id,
                Barcode = "BV-bbbbbbbb-aaaaaaaa-001",
                Status = CafeGameInventoryStatus.InUse,
                IsActive = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                CafeGameInventoryId = inventory.Id,
                Barcode = "BV-bbbbbbbb-aaaaaaaa-002",
                Status = CafeGameInventoryStatus.Available,
                IsActive = true
            }
        };

        CafeInventoryBoxSyncHelper.ApplySync(inventory, boxes);

        Assert.Equal(2, boxes.Count);
        Assert.Single(boxes, b => b.IsActive);
        Assert.Equal(CafeGameInventoryStatus.InUse, boxes[0].Status);
        Assert.False(boxes[1].IsActive);
    }
}
