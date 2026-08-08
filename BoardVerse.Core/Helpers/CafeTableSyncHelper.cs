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
            // SortOrder=null để ApplySync tự append vào cuối (MAX active + 1).
            // Không ép SortOrder=index vì sẽ gây conflict với bàn cũ chưa có trong payload
            // (vd: DB có [A=0, B=1, C=2], payload chỉ gồm [B, C] → SortOrder=0,1 sẽ trùng A cũ).
            var items = tableNames
                .Select(name => new CafeTableSyncItem
                {
                    Name = name,
                    SortOrder = null,   // ← để helper tự auto-append
                    SeatCount = null
                })
                .ToList();

            ApplySync(cafeId, items, existingTables);
        }

        /// <summary>
        /// Đồng bộ sơ đồ bàn đầy đủ (Name + SeatCount + SortOrder) theo 3-phase matching:
        ///
        /// <para><b>Auto-fill SortOrder=null</b>:</para>
        /// Khi FE không gửi <c>SortOrder</c> (vd: manager quán không biết/không quan tâm),
        /// backend tự động gán = MAX(SortOrder của bàn active) + 1 + thứ tự null trong payload.
        /// Nếu DB rỗng → bắt đầu từ 0. Cho phép manager thêm bàn mới mà không cần FE
        /// tính toán SortOrder, và idempotent (cùng payload → cùng kết quả).
        ///
        /// <para>Phase 1 — Match by Name (case-insensitive):</para>
        /// Tìm bàn active (hoặc inactive) trùng tên → giữ nguyên Id, cập nhật Name/SortOrder/SeatCount.
        /// Đây là "rename trực tiếp" khi user đổi tên nhưng giữ cùng vị trí, hoặc giữ nguyên không đổi.
        ///
        /// <para>Phase 2 — Match by SortOrder (rename ngầm khi đổi vị trí):</para>
        /// Với target chưa match ở Phase 1, nếu có bàn active ở cùng SortOrder index
        /// (chưa được matched) → coi như rename, gán target đó cho bàn đó.
        /// <para><b>Quan trọng</b>: GIỮ NGUYÊN Id để không làm đứt FK từ Booking/ActiveSession lịch sử.</para>
        /// <para><b>Điều kiện kích hoạt</b>: Phase 2 CHỈ chạy khi payload có ít nhất một tên trùng với
        /// bàn active đang tồn tại (Phase 1 matched ≥ 1). Nếu payload chỉ chứa tên mới hoàn toàn
        /// (không trùng bàn nào), tất cả target sẽ vào Phase 3 (tạo mới) — tránh "nuốt" bàn cũ.</para>
        ///
        /// <para>Phase 3 — Tạo mới:</para>
        /// Target còn lại chưa match → tạo bàn mới với SeatCount từ payload (hoặc default 4).
        /// Kể cả khi tên target trùng với bàn cũ đã soft-delete (Phase 1 đã reactivate bàn đó),
        /// Phase 3 vẫn có thể tạo thêm bàn mới nếu target chưa được match.
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
            // ========== Bước 1: Chuẩn bị danh sách target ==========
            // KHÔNG fill SortOrder=null ở đây. Fill null sẽ chạy SAU Phase 1 (Bước 4) để
            // dựa trên max SortOrder của các bàn CÒN LẠI sau Phase 1 (tránh gap thừa khi
            // Phase 1 match override SortOrder).
            var explicitTargets = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Select((x, index) => new NormalizedTarget(
                    Name: x.Name.Trim(),
                    SeatCount: x.SeatCount,
                    SortOrder: x.SortOrder ?? -1,    // -1 = null placeholder, sẽ fill sau Phase 1
                    ArrayIndex: index))
                .ToList();

            // ========== Bước 2: Validate SortOrder uniqueness trong PHẦN EXPLICIT ==========
            // Chỉ check các SortOrder != -1 (đã explicit). Null sẽ được fill sau và check lại.
            var explicitDuplicates = explicitTargets
                .Where(t => t.SortOrder != -1)
                .GroupBy(t => t.SortOrder)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (explicitDuplicates.Count > 0)
            {
                throw new ArgumentException(
                    $"SortOrder không được trùng lặp: {string.Join(", ", explicitDuplicates)}. Vui lòng đánh số thứ tự không trùng nhau.");
            }

            var now = DateTime.UtcNow;

            var activeTablesOrdered = existingTables
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ToList();

            var matchedIds = new HashSet<Guid>();

            // ========== Phase 1: Match by Name (case-insensitive) ==========
            foreach (var target in explicitTargets)
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
            //
            // ⚠️ BUGFIX: Phase 2 chỉ chạy khi Phase 1 matched ≥ 1 bàn (payload có ít nhất
            // một tên trùng với bàn active hiện có). Nếu không → tất cả target vào Phase 3
            // (tạo mới), tránh "nuốt" bàn cũ khi FE chỉ muốn thêm bàn mới.
            var sortOrderUsed = new HashSet<int>();
            if (matchedIds.Count > 0)
            {
                var phase2Targets = explicitTargets
                    .Where(t => !IsTargetMatched(t, matchedIds, existingTables))
                    .OrderBy(t => t.SortOrder == -1 ? int.MaxValue : t.SortOrder)   // null SortOrder (-1) xếp cuối
                    .ThenBy(t => t.ArrayIndex)
                    .ToList();

                foreach (var target in phase2Targets)
                {
                    // Null SortOrder (-1) không match Phase 2 — chờ Bước 4 fill.
                    if (target.SortOrder == -1) continue;

                    // Chỉ match khi SortOrder thật sự khớp — KHÔNG còn fallback "lấy bất kỳ
                    // bàn active chưa match nào". Fallback cũ đã từng âm thầm rename bàn cũ
                    // chỉ vì SortOrder chưa dùng hết.
                    var candidate = activeTablesOrdered.FirstOrDefault(t =>
                        !matchedIds.Contains(t.Id) &&
                        !sortOrderUsed.Contains(t.SortOrder) &&
                        t.SortOrder == target.SortOrder);

                    if (candidate != null)
                    {
                        ApplyTargetToTable(candidate, target, now);
                        matchedIds.Add(candidate.Id);
                        sortOrderUsed.Add(candidate.SortOrder);
                    }
                }
            }

            // ========== Bước 4: Fill SortOrder=-1 (null) cho target chưa matched ==========
            // Sau Phase 1+2, target chưa match sẽ đi vào Phase 3. Với các target có SortOrder=-1
            // (FE không gửi), tính max SortOrder của TẤT CẢ bàn active hiện tại + explicit payload,
            // rồi auto-append sau max đó.
            //
            // Lưu ý: maxSortOrder tính trên TOÀN BỘ bàn active (kể cả sẽ bị soft-delete) vì
            // append null phải sau cùng, không được "lấp vào chỗ trống" (gây shift SortOrder
            // của bàn khác không liên quan).
            //
            // VD: DB có [A=0, B=1, C=2]. Payload ["B", "Mới"] (cả 2 SortOrder=null).
            //   - Phase 1 match B → matchedIds.Add(B.Id).
            //   - maxSortOrder = max(A=0, B=1, C=2, explicit payload) = 2.
            //   - "Mới" SortOrder = 2 + 1 = 3. (A, C sẽ bị soft-delete sau.)
            //
            // ⚠️ Quan trọng: chỉ fill cho target CHƯA match (không có trong matchedIds). Nếu fill
            // cho cả target match Phase 1, SortOrder DB sẽ bị ghi đè thành giá trị fill sai.
            var maxSortOrder = activeTablesOrdered
                .Select(t => t.SortOrder)
                .Concat(explicitTargets
                    .Where(t => t.SortOrder != -1)
                    .Select(t => t.SortOrder))
                .DefaultIfEmpty(-1)
                .Max();
            var autoAppendCounter = 0;
            var targets = explicitTargets
                .Select(t =>
                {
                    if (t.SortOrder != -1) return t;
                    // Chỉ fill null cho target chưa match.
                    if (IsTargetMatched(t, matchedIds, existingTables)) return t;   // match Phase 1 → giữ -1, ApplyTargetToTable sẽ không apply SortOrder
                    return t with { SortOrder = maxSortOrder + 1 + autoAppendCounter++ };
                })
                .ToList();

            // ========== Bước 5: Validate SortOrder uniqueness sau fill null ==========
            // Re-check sau khi fill. Nếu fill null tạo duplicate (vd: 2 target null + DB max=0
            // → fill 1, 2 → OK; nhưng nếu payload có explicit SortOrder=1 + null → fill 2, 3 → OK).
            var allDuplicates = targets
                .GroupBy(t => t.SortOrder)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (allDuplicates.Count > 0)
            {
                throw new ArgumentException(
                    $"SortOrder không được trùng lặp: {string.Join(", ", allDuplicates)}. Vui lòng đánh số thứ tự không trùng nhau.");
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
            // SortOrder = -1 nghĩa là FE không gửi (null) → giữ nguyên DB SortOrder.
            // Chỉ ghi đè khi FE chủ động gửi SortOrder.
            if (target.SortOrder != -1)
            {
                table.SortOrder = target.SortOrder;
            }
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
