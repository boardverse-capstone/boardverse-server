using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
    public static class CafeTableSyncHelper
    {
        public static void ApplySync(Guid cafeId, IReadOnlyList<string> tableNames, IList<CafeTable> existingTables)
        {
            var now = DateTime.UtcNow;
            var targetNames = tableNames
                .Select((name, index) => (Name: name.Trim(), SortOrder: index))
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .ToList();

            var matchedIds = new HashSet<Guid>();

            foreach (var (name, sortOrder) in targetNames)
            {
                var table = existingTables.FirstOrDefault(t =>
                    t.IsActive &&
                    string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

                if (table == null)
                {
                    table = existingTables.FirstOrDefault(t =>
                        !t.IsActive &&
                        string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                }

                if (table != null)
                {
                    table.Name = name;
                    table.SortOrder = sortOrder;
                    table.IsActive = true;
                    table.UpdatedAt = now;
                    matchedIds.Add(table.Id);
                    continue;
                }

                existingTables.Add(new CafeTable
                {
                    Id = Guid.NewGuid(),
                    CafeId = cafeId,
                    Name = name,
                    SortOrder = sortOrder,
                    Status = CafeTableStatus.Available,
                    CreatedAt = now,
                    IsActive = true
                });
            }

            foreach (var table in existingTables.Where(t => t.IsActive && !matchedIds.Contains(t.Id)))
            {
                var stillListed = targetNames.Any(t =>
                    string.Equals(t.Name, table.Name, StringComparison.OrdinalIgnoreCase));

                if (stillListed)
                {
                    continue;
                }

                if (table.Status == CafeTableStatus.Available)
                {
                    table.IsActive = false;
                    table.UpdatedAt = now;
                }
            }
        }
    }
}
