using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
    public static class CafeTableSyncHelper
    {
        /// <summary>
        /// Default seat count cho bàn tạo mới khi payload không chỉ định.
        /// Phải khớp với `CafeTable.SeatCount = 4` (entity default).
        /// </summary>
        public const int DefaultSeatCount = 4;

        /// <summary>
        /// Internal record cho normalized target item sau khi trim/resolve SortOrder.
        /// </summary>
        private sealed record NormalizedTarget(
            string Name,
            int? SeatCount,
            int SortOrder,
            int ArrayIndex);

        /// <summary>
        /// Legacy overload — đồng bộ chỉ tên.
        /// Tự gọi overload đầy đủ với SeatCount=null (giữ nguyên seatCount cũ cho bàn match, default 4 cho bàn mới).
        /// </summary>
        public static void ApplySync(Guid cafeId, IReadOnlyList<string> tableNames, IList<CafeTable> existingTables)
        {
            var items = tableNames
                .Select((name, index) => new CafeTableSyncItem
                {
                    Name = name,
                    SortOrder = index,
                    SeatCount = null
                })
                .ToList();

            ApplySync(cafeId, items, existingTables);
        }

        /// <summary>
        /// Đồng bộ sơ đồ bàn đầy đủ (Name + SeatCount + SortOrder) theo 3-phase matching:
        ///
        /// <para>Phase 1 — Match by Name (case-insensitive):</para>
        /// Tìm bàn active (hoặc inactive) trùng tên → giữ nguyên Id, cập nhật Name/SortOrder/SeatCount.
        /// Đây là "rename trực tiếp" khi user đổi tên nhưng giữ cùng vị trí, hoặc giữ nguyên không đổi.
        ///
        /// <para>Phase 2 — Match by SortOrder (rename ngầm khi đổi vị trí):</para>
        /// Với target chưa match ở Phase 1, nếu có bàn active ở cùng SortOrder index
        /// (chưa được matched) → coi như rename, gán target đó cho bàn đó.
        /// <para><b>Quan trọng</b>: GIỮ NGUYÊN Id để không làm đứt FK từ Booking/ActiveSession lịch sử.</para>
        ///
        /// <para>Phase 3 — Tạo mới:</para>
        /// Target còn lại chưa match → tạo bàn mới với SeatCount từ payload (hoặc default 4).
        ///
        /// <para>Soft-delete:</para>
        /// Bàn active KHÔNG nằm trong matchedIds và KHÔNG có trong targetItems (theo tên)
        /// → chỉ soft-delete nếu Status = Available (giữ an toàn cho bàn đang có session).
        ///
        /// <para>SeatCount semantics:</para>
        /// <list type="bullet">
        /// <item>Bàn mới: lấy từ payload, fallback <see cref="DefaultSeatCount"/> nếu null.</item>
        /// <item>Bàn match: chỉ ghi đè khi payload có giá trị (null → giữ nguyên).</item>
        /// </list>
        /// </summary>
        public static void ApplySync(
            Guid cafeId,
            IReadOnlyList<CafeTableSyncItem> items,
            IList<CafeTable> existingTables)
        {
            // GAP-23 Fix: Validate SortOrder uniqueness before processing
            var sortOrders = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Select((x, index) => x.SortOrder ?? index)
                .ToList();

            var duplicates = sortOrders
                .GroupBy(so => so)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                throw new ArgumentException(
                    $"SortOrder không được trùng lặp: {string.Join(", ", duplicates)}. Vui lòng đánh số thứ tự không trùng nhau.");
            }

            var now = DateTime.UtcNow;
            var targets = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Select((x, index) => new NormalizedTarget(
                    Name: x.Name.Trim(),
                    SeatCount: x.SeatCount,
                    SortOrder: x.SortOrder ?? index,
                    ArrayIndex: index))
                .ToList();

            var activeTablesOrdered = existingTables
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ToList();

            var matchedIds = new HashSet<Guid>();

            // ========== Phase 1: Match by Name (case-insensitive) ==========
            foreach (var target in targets)
            {
                var byName = existingTables.FirstOrDefault(t =>
                    t.IsActive &&
                    string.Equals(t.Name, target.Name, StringComparison.OrdinalIgnoreCase));

                if (byName == null)
                {
                    byName = existingTables.FirstOrDefault(t =>
                        !t.IsActive &&
                        string.Equals(t.Name, target.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (byName != null)
                {
                    ApplyTargetToTable(byName, target, now);
                    matchedIds.Add(byName.Id);
                }
            }

            // ========== Phase 2: Match by SortOrder (rename ngầm) ==========
            // Bàn active CHƯA matched ở Phase 1 sẽ là rename candidate.
            // Duyệt target theo SortOrder; với target chưa match tìm bàn cùng SortOrder.
            // Nếu không có bàn đúng SortOrder, fallback lấy bàn active chưa match tiếp theo
            // (cho phép payload shrink/reorder mà vẫn match được).
            var sortOrderUsed = new HashSet<int>();
            var phase2Targets = targets
                .Where(t => !IsTargetMatched(t, matchedIds, existingTables))
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.ArrayIndex)
                .ToList();

            foreach (var target in phase2Targets)
            {
                var candidate = activeTablesOrdered.FirstOrDefault(t =>
                    !matchedIds.Contains(t.Id) &&
                    !sortOrderUsed.Contains(t.SortOrder) &&
                    t.SortOrder == target.SortOrder);

                // Fallback: ghép theo thứ tự xuất hiện nếu SortOrder hoàn toàn khác.
                if (candidate == null)
                {
                    candidate = activeTablesOrdered.FirstOrDefault(t =>
                        !matchedIds.Contains(t.Id) &&
                        !sortOrderUsed.Contains(t.SortOrder));
                }

                if (candidate != null)
                {
                    ApplyTargetToTable(candidate, target, now);
                    matchedIds.Add(candidate.Id);
                    sortOrderUsed.Add(candidate.SortOrder);
                }
            }

            // ========== Phase 3: Tạo mới cho target chưa matched ==========
            foreach (var target in targets)
            {
                if (IsTargetMatched(target, matchedIds, existingTables))
                {
                    continue;
                }

                existingTables.Add(new CafeTable
                {
                    Id = Guid.NewGuid(),
                    CafeId = cafeId,
                    Name = target.Name,
                    SortOrder = target.SortOrder,
                    SeatCount = target.SeatCount ?? DefaultSeatCount,
                    Status = CafeTableStatus.Available,
                    CreatedAt = now,
                    IsActive = true
                });
            }

            // ========== Soft-delete: bàn active thừa ==========
            foreach (var table in existingTables.Where(t => t.IsActive && !matchedIds.Contains(t.Id)))
            {
                var stillListed = targets.Any(t =>
                    string.Equals(t.Name, table.Name, StringComparison.OrdinalIgnoreCase));

                if (stillListed)
                {
                    continue;
                }

                // An toàn: chỉ soft-delete khi không có session đang chạy.
                if (table.Status == CafeTableStatus.Available)
                {
                    table.IsActive = false;
                    table.UpdatedAt = now;
                }
            }
        }

        private static void ApplyTargetToTable(CafeTable table, NormalizedTarget target, DateTime now)
        {
            table.Name = target.Name;
            table.SortOrder = target.SortOrder;
            table.IsActive = true;
            if (target.SeatCount.HasValue)
            {
                table.SeatCount = target.SeatCount.Value;
            }
            table.UpdatedAt = now;
        }

        /// <summary>
        /// Target đã được match vào bàn nào chưa?
        /// </summary>
        private static bool IsTargetMatched(
            NormalizedTarget target,
            HashSet<Guid> matchedIds,
            IList<CafeTable> existingTables)
        {
            return existingTables.Any(t =>
                matchedIds.Contains(t.Id) &&
                string.Equals(t.Name, target.Name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
