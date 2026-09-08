# Plan — Flow "Về sớm + Tiếp tục chơi"

> **Ngày lập:** 2026-09-07
> **Trạng thái:** Draft — chờ review
> **Phạm vi:** EndGame → PartialCheckout → ComponentCheck → SplitSession → tiếp tục chơi → (tuỳ chọn) MergeSession → kết thúc

---

## I. TÓM TẮT

Khi trong nhóm có người về sớm (A1, A2) và người muốn ở lại chơi tiếp (A3, A4), flow nghiệp vụ mục tiêu:

1. **EndGame** toàn nhóm (trả hộp game về quầy, kiểm kê linh kiện)
2. **PartialCheckout(A1,A2)** đánh dấu A1,A2 về sớm
3. **ComponentCheck** kiểm kê linh kiện (penalty gán cho A1,A2 nếu thiếu)
4. **SplitSession(A3,A4)** tách A3,A4 ra session mới (giữ JoinedAt gốc để bill liên tục)
5. **Checkout + Pay** session gốc (A1,A2 thanh toán)
6. Session mới (A3,A4) **tiếp tục chơi** → có thể **MergeSessionAsync** vào nhóm khác hoặc EndGame riêng
7. (MỚI) **Live Merge** — A3, A4 ở session Active có thể merge sang target Active mà không cần qua EndGame

---

## II. TRẠNG THÁI HIỆN TẠI CỦA CODE

### 2.1. Code đã có

- `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/end` — EndGameSessionAsync (`CafePosService.cs:1351`)
- `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/partial-checkout` — PartialCheckoutAsync (`ActiveSessionService.cs:269`)
- `POST /api/cafes/{cafeId}/pos/sessions/{sessionGameId}/component-check` — SubmitComponentCheckAsync
- `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/checkout` — CheckoutAsync (`ActiveSessionService.cs:186`)
- `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/pay` — PaySessionAsync
- `POST /api/cafes/{cafeId}/pos/sessions/{sourceSessionId}/split` — SplitSessionAsync (`ActiveSessionService.cs:484`)
- `POST /api/cafes/{cafeId}/pos/sessions/{sourceSessionId}/merge` — MergeSessionAsync (`ActiveSessionService.cs:399`)

### 2.2. Gap phát hiện qua đọc code

#### Gap 1 — CHẶN FLOW

**File:** `BoardVerse.Services/Services/ActiveSessionService.cs:269-345`

`PartialCheckoutAsync` chỉ set status cho members trong `request.MemberIds` (người về sớm). Members KHÔNG thuộc `MemberIds` (A3,A4) giữ nguyên `SuspendedMutation` từ EndGame → SplitSession từ chối (validate `member.Status == Playing` ở line 528).

#### Gap 2 — KHUYẾN NGHỊ

**File:** `BoardVerse.Services/Services/ActiveSessionService.cs:591-680` (SplitSessionAsync slow path)

Session mới có `CafeInventoryBoxId = sourceSession.CafeInventoryBoxId` nhưng box vẫn ở `Available` (do EndGame set ở `CafePosService.cs:1406-1408`). Staff phải AttachGame thủ công sau Split, nếu quên → box bị nhân viên khác attach nhầm.

#### Gap 4 — CHẶN LIVE MERGE (phát hiện sau khi phân tích scenario "A3, A4 merge sau 10-20 phút")

**File:** `BoardVerse.Services/Services/ActiveSessionService.cs:399-462` (`MergeSessionAsync`)

**Vấn đề:** Validate ở line 426 yêu cầu `member.Status == SuspendedMutation`. Nếu A3, A4 đang ở session Active (`Playing`) muốn merge sang target Active → fail 409.

**Workaround hiện tại (có bug tài chính):**
- EndGame(X) → CHECKING, members → SuspendedMutation
- Merge → A3, A4 set ActiveSessionId = B
- Session X rỗng → bill 80 phút của A3, A4 **mất** vì họ đã chuyển session

**Fix cần làm (Gap 4):**

Cho phép member `Playing` ở session `Active` merge sang target `Active`. Logic bổ sung:

```csharp
// Sau block validate hiện tại, thêm:
if (member.Status == IndividualSessionStatus.Playing
    && sourceSession.Status == GroupSessionStatus.Active)
{
    // Live merge path
    // Validate member vẫn đang trong source
    // Validate target cùng cafeId + GameTemplateId
    // Transfer box (nếu source rỗng sau merge)
    // Set member.ActiveSessionId = target, Status = Playing, OriginalSessionId giữ
}
```

**Tác động:** Box chuyển từ source sang target. Source nếu còn members → tiếp tục; nếu rỗng → bill của X cho members đã rời được tính trong bill của target (BR-09 continuous time, JoinedAt = 12:00, TotalMinutesPlayed tính đến khi target kết thúc).

**Điểm bổ sung trong plan triển khai Gap 4:**
- Update `MergeSessionAsync` validate (cho phép Playing + Active)
- Thêm logic box transfer
- Thêm test cho scenario "merge từ Active 2 session cùng Active"
- Cập nhật doc MergeSessionAsync

#### Gap 3 — DEAD CODE

**File:** `BoardVerse.Services/Services/ActiveSessionService.cs:343-344`

```csharp
session.IsCheckingInventory = true;
session.Status = GroupSessionStatus.Checking;
```

Hai dòng này không có tác dụng vì session đã ở `Checking` (validate ở line 285-289). Xoá để code rõ ràng.

---

## III. THIẾT KẾ FIX

### 3.1. Fix Gap 1 — PartialCheckout restore members về Playing

**File:** `ActiveSessionService.cs:332-345`

```csharp
// Code hiện tại (chỉ set status cho members về sớm)
foreach (var member in session.Members.Where(m => request.MemberIds.Contains(m.Id)))
{
    member.Status = IndividualSessionStatus.SuspendedMutation;
    member.LeftAt = DateTime.UtcNow;
    await _activeSessionRepository.UpdateMemberAsync(member);
}

// Code thêm vào SAU đó (GAP 1 fix)
foreach (var member in session.Members.Where(m =>
    !request.MemberIds.Contains(m.Id)
    && m.Status == IndividualSessionStatus.SuspendedMutation
    && m.LeftAt.HasValue
    && !m.IsGuestSlot))  // Guest slot không bao giờ "tiếp tục chơi" (BR-13)
{
    member.Status = IndividualSessionStatus.Playing;
    member.LeftAt = null;
    await _activeSessionRepository.UpdateMemberAsync(member);
}

// Xoá 2 dòng dead code (GAP 3)
// session.IsCheckingInventory = true;  ← không cần, đã true
// session.Status = GroupSessionStatus.Checking;  ← không cần, đã Checking
```

**Lý do cần `!m.IsGuestSlot`:** Guest slot (BR-13) không có tư cách tài sản độc lập, không thể "tiếp tục chơi" — chỉ host mới chịu trách nhiệm. Nếu guest được restore về `Playing`, sẽ gây nhầm lẫn về trách nhiệm tài sản.

### 3.2. Fix Gap 2 — SplitSession auto-attach box

**File:** `ActiveSessionService.cs` slow path của SplitSessionAsync

Thêm ngay sau khi tạo `newSession` và trước `await _activeSessionRepository.AddAsync(newSession, ct)`:

```csharp
// GAP 2 fix: tự attach box vào session mới để staff không phải gọi AttachGame thủ công
if (newSession.CafeInventoryBoxId.HasValue)
{
    var box = await _posRepository.GetBoxByIdAsync(newSession.CafeInventoryBoxId.Value, ct);
    if (box == null)
    {
        throw new NotFoundException(ApiErrorMessages.Pos.BoxNotFound);
    }

    if (box.Status == CafeGameInventoryStatus.Available)
    {
        box.Status = CafeGameInventoryStatus.InUse;
        box.UpdatedAt = DateTime.UtcNow;
        await _posRepository.UpdateBoxAsync(box, ct);
    }
    else if (box.Status != CafeGameInventoryStatus.InUse)
    {
        // Box đang Maintenance/Damaged/Retired → không thể split
        throw new ConflictException(
            ApiErrorMessages.Pos.BoxNotAvailableForSplit(box.Status.ToString()));
    }
}
```

**Lưu ý cho fast path (target session):** Không cần fix vì target session đã `Active` → box của target đã `InUse` từ trước.

### 3.3. Doc update (Gap 3)

**File:** `BoardVerse.Core/DTOs/Pos/SplitSessionResponseDto.cs`

Bổ sung XML doc:

```csharp
/// <summary>
/// Session gốc SAU khi split:
/// - Vẫn tồn tại trong DB (giữ audit trail).
/// - Chỉ còn lại members KHÔNG thuộc MovedMemberIds (thường là người về sớm).
/// - Vẫn ở trạng thái Checking (hoặc Paid) — staff phải tiếp tục Checkout/Pay như bình thường.
/// </summary>
public ActiveSessionResponseDto? SourceSession { get; set; }

/// <summary>
/// Session MỚI sau khi split:
/// - Status = Active, JoinedAt = inherited từ session gốc (BR-09 continuous time).
/// - Box game đã được auto-attach (Status = InUse) — staff KHÔNG cần gọi AttachGame thủ công.
/// - Members trong MovedMemberIds đã ở Status = Playing, LeftAt = null.
/// </summary>
public ActiveSessionResponseDto? NewSession { get; set; }
```

---

## IV. FILE CẦN SỬA

| File | Thay đổi | Dòng tham chiếu |
|---|---|---|
| `BoardVerse.Services/Services/ActiveSessionService.cs` | Thêm 8 dòng restore Playing (Gap 1) | Sau line 345 |
| `BoardVerse.Services/Services/ActiveSessionService.cs` | Xoá 2 dòng dead code (Gap 3) | Line 343-344 |
| `BoardVerse.Services/Services/ActiveSessionService.cs` | Thêm 14 dòng auto-attach box (Gap 2) | Trước `await _activeSessionRepository.AddAsync(newSession, ct)` (~line 654) |
| `BoardVerse.Core/DTOs/Pos/SplitSessionResponseDto.cs` | Bổ sung XML doc | Toàn file |

---

## V. FILE TEST CẦN TẠO

| File | Test ID | Mô tả |
|---|---|---|
| `BoardVerse.Tests/Services/ActiveSessionServicePartialCheckoutTests.cs` (MỚI) | TC-PARTIAL-04 | Sau PartialCheckout, members không thuộc MemberIds phải `Playing` + `LeftAt = null` |
| `BoardVerse.Tests/Services/ActiveSessionServiceSplitSessionTests.cs` (MỚI) | TC-SPLIT-04 | Box = Maintenance → Split → 409; Box = Available → Split → box = InUse |
| `BoardVerse.Tests/Integration/CafePos/SplitSessionAfterEarlyLeaveTests.cs` (MỚI) | TC-SPLIT-01 | Happy path: 4 người ACTIVE → EndGame → Partial(A1,A2) → ComponentCheck → Split(A3,A4) → 2 session song song |
| `BoardVerse.Tests/Integration/CafePos/SplitSessionAfterEarlyLeaveTests.cs` (MỚI) | TC-SPLIT-02 | Penalty: A1,A2 về sớm + thiếu linh kiện → penalty gán đúng cho A1,A2 |
| `BoardVerse.Tests/Integration/CafePos/SplitSessionAfterEarlyLeaveTests.cs` (MỚI) | TC-SPLIT-MERGE | A3 merge sang nhóm B đang Active — bill A3 continuous từ 12:00 |

---

## VI. DOC CẬP NHẬT

| File | Nội dung |
|---|---|
| `docs/api/cafe-pos.md` | Endpoint SplitSession: bổ sung "after split, source vẫn chờ Checkout/Pay", "new session auto-attach box" |
| `docs/api/cafe-pos.md` | Endpoint PartialCheckout: bổ sung "members không thuộc MemberIds sẽ được restore về Playing sau fix Gap 1" |

---

## VII. STATE MACHINE TỔNG HỢP

```
                     ┌─────────────────────────────────────────────┐
                     │              ActiveSession state            │
                     └─────────────────────────────────────────────┘

[ACTIVE]──EndGame──►[CHECKING]──PartialCheckout──►[CHECKING]
   │                     │   (A1,A2 = SuspendedMutation)
   │                     │   (A3,A4 = Playing ← GAP 1 restore)
   │                     │
   │                     ├──ComponentCheck──────────┐
   │                     │   (penalty cho A1,A2)    │
   │                     │                          │
   │                     ├──Checkout────────────────►[UNPAID]
   │                     │   (tính bill per-member) │
   │                     │                          │
   │                     ├──Pay─────────────────────►[PAID]
   │                     │   (A1,A2 = Finished)     │
   │                     │   (bàn Released)         │
   │                     │
   │                     └──SplitSession(A3,A4)──┐
   │                                              │
   │                                              ▼
   │                                       ┌──────────────┐
   │                                       │ Session MỚI  │
   │                                       │ Status=ACTIVE│
   │                                       │ A3,A4=Playing│
   │                                       │ JoinedAt=12:00│
   │                                       │ Box=InUse    │
   │                                       └──────────────┘
   │                                              │
   │                                              ├──EndGame──►[CHECKING]
   │                                              │             (kiểm kê lại)
   │                                              │
   │                                              ├──Checkout──►[UNPAID]
   │                                              │
   │                                              ├──Pay──────►[PAID]
   │                                              │
   │                                              └──MergeSessionAsync
   │                                                  (A3 sang nhóm B
   │                                                   giữ JoinedAt)
```

---

## VIII. MIGRATION DB

Không cần. Schema đã đủ:
- `ActiveSession.CafeInventoryBoxId` ✓
- `ActiveSessionMember.OriginalSessionId` ✓ (GAP-12 đã track)
- `ActiveSessionMember.JoinedAt` ✓
- `ActiveSessionMember.LeftAt` ✓
- `CafeInventoryBox.Status` ✓

---

## IX. RISK & MITIGATION

| Risk | Mức | Mitigation |
|---|---|---|
| Gap 1 fix gây regression: staff từng dùng pattern "PartialCheckout toàn bộ group" thay Checkout | Thấp | Test TC-PARTIAL-02 (chọn cả nhóm → 400) đã đảm bảo |
| Gap 2 fix conflict với staff manual attach | Trung bình | Skip nếu box đã `InUse` (staff attach trước) → không double-set |
| End-to-end test chậm | Thấp | Dùng `IntegrationTestFixtures` có sẵn, test 1 path chính |
| Merge session với JoinedAt = 12:00 làm bill sai khi cafe charge per-slot | Trung bình | Đã có logic ở `ActiveSessionBillingCalculator`, cần verify trong test TC-SPLIT-MERGE |
| Source session sau Pay vẫn còn `EndedAt` nhưng members rỗng → GetActiveSessionsAsync trả gì? | Thấp | `GetActiveSessionsAsync` filter `Status ∈ {Active, Checking}` → source đã `Paid` → không trả. Nhưng `GetSessionByIdAsync` vẫn trả → staff thấy state rỗng, gây confusion |

---

## X. THỨ TỰ TRIỂN KHAI

1. **Bước 1** — Fix Gap 1 + Gap 3 (cùng block code) + unit test TC-PARTIAL-04
2. **Bước 2** — Fix Gap 2 + unit test TC-SPLIT-04
3. **Bước 3** — Doc update XML docs của `SplitSessionResponseDto`
4. **Bước 4** — Doc update `docs/api/cafe-pos.md`
5. **Bước 5** — Integration test TC-SPLIT-01, TC-SPLIT-02, TC-SPLIT-MERGE

---

## XI. CÂU HỎI MỞ (đã chốt)

- [x] Có cho phép A3 merge vào nhóm B đang Active không? → **Có, dùng MergeSessionAsync hiện có**
- [x] Phạm vi fix Gap 2? → **Auto attach box**
- [x] Dead code xoá? → **Xoá**
- [x] A3, A4 merge sau 10-20 phút chơi riêng (live merge)? → **Cần Gap 4: thêm logic merge từ Active session**

## XII. KỊCH BẢN MỚI: A3, A4 MERGE SAU 10-20 PHÚT

### XII.1. Flow mong muốn

```
[Source A: CHECKING, A1,A2 SuspendedMutation, A3,A4 Playing]
   │
   ▼ SplitSession(A, [A3,A4]) slow path
[New X: Active, A3,A4 Playing, box InUse, JoinedAt = 12:00]
   │
   ▼ ... A3, A4 chơi 20 phút ...
   │     (12:00 → 12:20)
   │
[X: Active, A3,A4 Playing, TotalMinutesPlayed ≈ 0 (chưa checkout)]
   │
   ▼ A3,A4 muốn qua B (đang Active)
   │
[X: tạm thời gì?]                [B: Active, B1-B4 Playing]
   │
   ▼ Live merge (Gap 4):
   ├─ member ActiveSessionId: X → B
   ├─ member Status: giữ Playing
   ├─ member JoinedAt: giữ 12:00 (BR-09)
   ├─ member OriginalSessionId: giữ A (lần đầu split)
   │
   ▼ X: rỗng members → bill tự động = 0, có thể set Paid luôn
   │       (không có penalty, không có ai ở lại)
   │
   ▼ B: B1-B4 + A3,A4 (6 người)
       Box đã transfer từ X → B
       A3,A4 chơi tiếp từ 12:20 đến khi B kết thúc
       Bill cuối: A3,A4 tính tổng = (12:00 → B.EndedAt) theo BR-09
```

### XII.2. Trước Gap 4 — workaround hiện tại có bug

```
[X: Active, A3,A4 Playing]
   │
   ▼ EndGame(X) → CHECKING
   │       members → SuspendedMutation, box → Available
   ▼ ComponentCheck(X) → kiểm kê
   ▼ MergeSessionAsync(X, [A3,A4], target=B) ← OK ở đây
   │       A3, A4 sang B, status Playing
   ▼ X: rỗng → bill X (80 phút) MẤT vì không còn members ở X
   │       Bug: A3, A4 đã merge sang B nhưng bill 80 phút đầu không ai trả
```

### XII.3. Cách giải quyết tạm thời nếu chưa làm Gap 4

**Option A:** Dùng SplitSession fast path với target = B ngay từ đầu (yêu cầu B đã tồn tại khi A3, A4 tách).

**Option B:** Wait cho source A → Paid, sau đó A3, A4 tạo session mới cho riêng họ (walk-in mới hoặc walk-in override) rồi merge B — nhưng phức tạp và vẫn cần Gap 4 hoặc workaround tương tự.

**Option C (khuyến nghị):** Implement Gap 4 cùng các fix khác trong turn này. Đây là edge case quan trọng sẽ phát sinh trong thực tế.
