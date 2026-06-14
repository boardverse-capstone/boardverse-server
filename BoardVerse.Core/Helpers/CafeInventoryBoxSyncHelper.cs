using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
    public static class CafeInventoryBoxSyncHelper
    {
        public static string FormatBarcode(Guid cafeId, Guid inventoryId, int sequence) =>
            $"BV-{cafeId.ToString("N")[..8]}-{inventoryId.ToString("N")[..8]}-{sequence:D3}";

        public static void ApplySync(CafeGameInventory inventory, IList<CafeInventoryBox> existingBoxes)
        {
            var now = DateTime.UtcNow;
            var targetCount = inventory.BoxQuantity;
            var activeBoxes = existingBoxes.Where(b => b.IsActive).OrderBy(b => b.Barcode).ToList();

            while (activeBoxes.Count < targetCount)
            {
                var sequence = existingBoxes.Count + 1;
                var box = new CafeInventoryBox
                {
                    Id = Guid.NewGuid(),
                    CafeGameInventoryId = inventory.Id,
                    Barcode = FormatBarcode(inventory.CafeId, inventory.Id, sequence),
                    Status = CafeGameInventoryStatus.Available,
                    CreatedAt = now,
                    IsActive = true
                };
                existingBoxes.Add(box);
                activeBoxes.Add(box);
            }

            if (activeBoxes.Count > targetCount)
            {
                var removable = activeBoxes
                    .Where(b => b.Status == CafeGameInventoryStatus.Available)
                    .OrderByDescending(b => b.Barcode)
                    .Take(activeBoxes.Count - targetCount)
                    .ToList();

                foreach (var box in removable)
                {
                    box.IsActive = false;
                    box.UpdatedAt = now;
                    activeBoxes.Remove(box);
                }
            }
        }
    }
}
