# CancellationToken Propagation Audit (2026-08-27)

> **Status:** Audit completed. Fix strategy below prioritizes high-impact paths.
>
> **Context:** Toàn bộ service method nên propagate `CancellationToken` xuống EF Core và HTTP client calls. Khi controller bị cancel (client disconnect) hoặc timeout, hiện tại nhiều service method vẫn tiếp tục chạy đến hết → lãng phí CPU/IO và có thể ghi state trên DB sau khi client đã ngắt.

---

## I. Phạm vi audit

Toàn bộ code trong:
- `BoardVerse.Services/Services/**/*.cs` (production service layer)
- `BoardVerse.Services/Repositories/**/*.cs` (data access)
- `BoardVerse.API/Controllers/**/*.cs` (HTTP layer entry)

Method được audit phải là `public async Task*` có ≥ 1 tham số `CancellationToken cancellationToken = default` **và** có body call `SaveChangesAsync()`, `ToListAsync()`, `FirstOrDefaultAsync()`, `AnyAsync()`, `CountAsync()`, `FindAsync(...)`, `ExecuteUpdateAsync()`, hoặc `HttpClient.*Async` mà KHÔNG truyền `cancellationToken` xuống.

---

## II. Tổng kết tìm được

| Pattern | File | Count | Mức độ |
|---|---|---|---|
| `_db.SaveChangesAsync()` không có `ct` | `BoardVerse.Services/Services/ReservationService.cs` | **5** | High |
| `.ToListAsync()` không có `ct` | `AdminReportService`, `PlayerAlertService`, `SettlementService`, `BggGameService`, `ReceiptService`, `CafePosService`, `GameSeedService` | **8** | Medium |
| `.FirstOrDefaultAsync()` không có `ct` | `AdminReportService` (4), `PlayerCheckInService` (2) | **6** | Medium |
| `.CountAsync()` không có `ct` | `AdminReportService` (2) | 2 | Low |
| `await HttpClient.*Async(...)` không có `ct` | (ngoài scope — chỉ kiểm tra EF) | — | — |

**Tổng production occurrences:** ~21 sites.

---

## III. Top-priority fixes (HOT PATH)

### ReservationService.cs — 5 sites

Tại `BoardVerse.Services/Services/ReservationService.cs`:

| Line | Pattern | Note |
|---|---|---|
| 330 | `await _db.SaveChangesAsync();` (in `ConfirmAsync`) | Critical — lobby publish + BVC hold |
| 705 | `await _db.SaveChangesAsync();` (in lobby activation) | Critical — booking atomic transaction |
| 816 | `await _db.SaveChangesAsync();` (in confirm + capture) | Critical — payment capture |
| 2169 | `await _db.SaveChangesAsync();` (in session completion) | Critical — session → completed |
| 3285 | `await _db.SaveChangesAsync();` (in admin moderation) | Medium — admin actions |

Mỗi site phải đổi `await _db.SaveChangesAsync()` → `await _db.SaveChangesAsync(ct)`.

**Tại sao HOT:** ReservationService là entry point cho flow chính (lobby tạo + activate + capture). Nếu controller bị cancel giữa SaveChanges, request vẫn chạy → có thể tạo `Lobby` "ma" (đã publish lên SignalR nhưng HTTP response trả về cho client = 499 Client Closed Request).

### AdminReportService.cs — 8 sites (background job)

Tại `BoardVerse.Services/Services/AdminReportService.cs`:
- Lines 57, 86, 207, 225, 310, 317, 397, 404

Method này được gọi bởi admin dashboard polling — không critical nhưng vẫn lý tưởng để có `ct` để có thể cancel long-running aggregation.

### PlayerCheckInService.cs — 2 sites

Lines 241, 255 — `FirstOrDefaultAsync()` trong check-in validation. Should respect CT.

---

## IV. Cách fix

### Pattern tiêu chuẩn

```csharp
// ❌ Trước
public async Task<bool> DoSomethingAsync(int id)
{
    var entity = await _db.Entities.FindAsync(id);
    await _db.SaveChangesAsync();
    return true;
}

// ✅ Sau
public async Task<bool> DoSomethingAsync(int id, CancellationToken cancellationToken = default)
{
    var entity = await _db.Entities.FindAsync(new object[] { id }, cancellationToken);
    await _db.SaveChangesAsync(cancellationToken);
    return true;
}
```

### Lưu ý cho controller layer

```csharp
// Controller — pass HttpContext.RequestAborted
[HttpGet("{id}")]
public async Task<IActionResult> Get(int id)
{
    var result = await _service.GetAsync(id, HttpContext.RequestAborted);
    return Ok(result);
}
```

### Background job

```csharp
// Hangfire / BackgroundService — pre-create linked CT với timeout
using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
cts.CancelAfter(TimeSpan.FromMinutes(5));
await _service.RunReportAsync(cts.Token);
```

---

## V. Kế hoạch triển khai (chia theo PR)

### PR 1 — ReservationService critical paths (2026-08-27, ưu tiên P0)

Thêm `CancellationToken cancellationToken = default` parameter (nếu chưa có) và truyền xuống 5 sites `SaveChangesAsync`.

Affected callers:
- `LobbyService.ActivateLobbyAsync` (gọi `ReservationService.ConfirmAsync`)
- `ActiveSessionService.PaySessionCoreAsync` (gọi `CompleteAndCaptureAsync`)
- `BookingService` (gọi nhiều Reservation method)
- Admin controller (gọi admin moderation methods)

→ Cần update signature các caller, hoặc default `CancellationToken.None` nếu caller không có sẵn `ct`.

### PR 2 — AdminReportService + PlayerCheckInService (P1)

8 + 2 sites. Caller layer (admin dashboard + check-in endpoint) đều đã có `HttpContext.RequestAborted` → chỉ cần propagate xuống.

### PR 3 — Audit remaining sites (P2)

`PlayerAlertService`, `SettlementService`, `BggGameService`, `ReceiptService`, `CafePosService`, `GameSeedService`.

---

## VI. Test strategy

Mỗi PR phải có:
1. **Unit test** xác nhận `CancellationToken` được propagate — dùng `CancellationTokenSource` với `Cancel()` trước khi gọi method, assert method throw `OperationCanceledException`.
2. **Integration test** cho controller bị cancel (HttpClient với `cts.CancelAfter(100ms)`) → assert không có side-effect DB (hoặc rollback).

---

## VII. Out of scope (đề xuất deferred)

- HTTP client calls (`HttpClient.SendAsync`, `GetAsync`, ...) — đã được wrap qua `SePayClient` / `BrevoEmailService` với CT riêng. Audit chi tiết ở file riêng.
- EF Core raw SQL (`FromSqlRaw`, `Database.ExecuteSqlRawAsync`) — check riêng nếu cần.
- Async delegates (`Task.Run`, `ContinueWith`) — không nên có trong production code.

---

## VIII. Tham chiếu

- Source: scope liệt kê ở §II.
- Existing convention: xem `ActiveSessionService.cs` và `ReservationService.ExecuteCompleteAndCaptureTransactionAsync` — đã có sẵn pattern `ct` propagation.
- Workspace rule: `api-controller-xml-docs.mdc` (XML doc) — không liên quan.
- Workspace rule: `api-doc-test-standards.mdc` (test + doc khi thay đổi signature).