# CafePosController

**Base route:** `/api/cafes/{cafeId}/pos`
**Controller:** `CafePosController.cs`
**Role:** Manager hoặc CafeStaff

API vận hành quầy: bàn, kho hộp game, phiên chơi, kiểm kê, khách vô danh, thanh toán một phần và thanh toán toàn bộ.

> **Lưu ý:** Một số endpoint liên quan đến phiên chơi đang được tách sang `ActiveSessionController` với base route `/api/cafes/{cafeId}/sessions`. Xem bảng bên dưới để biết chính xác từng thao tác thuộc controller nào.

## Endpoints

| Endpoint | Method | Mô tả | Controller |
|----------|--------|--------|------------|
| `/tables` | GET | Sơ đồ bàn realtime | `CafePosController` |
| `/tables` | PUT | Đồng bộ sơ đồ bàn (Name + SeatCount + SortOrder) — hỗ trợ 2 shape | `CafePosController` |
| `/tables/{tableId}` | PATCH | Cập nhật một phần thông tin bàn (Name / SeatCount / SortOrder) | `CafePosController` |
| `/boxes` | GET | Danh sách hộp game | `CafePosController` |
| `/boxes/by-barcode/{barcode}` | GET | Tra cứu hộp sau khi quét POS | `CafePosController` |
| `/sessions/active` | GET | Phiên đang chơi | `CafePosController` |
| `/bookings/{bookingCode}` | GET | Preview booking trước check-in (AC 1.1) | `CafePosController` |
| `/sessions` | POST | Giao game cho bàn — bắt đầu phiên chơi | `CafePosController` |
| `/sessions/from-booking` | POST | Host-led check-in từ mã đặt chỗ (AC 1.2-1.4) | `CafePosController` |
| `/sessions/{sessionId}/end` | POST | Trả game / kết thúc phiên chơi | `CafePosController` |
| `/sessions/{sessionId}/checkout` | POST | Thanh toán toàn bộ sau kiểm kê linh kiện | `ActiveSessionController` |
| `/sessions/{sessionId}/guest-slots` | POST | Thêm khách vô danh | `ActiveSessionController` |
| `/sessions/{sessionId}/partial-checkout` | POST | Thanh toán một phần khi có người về sớm | `ActiveSessionController` |

---

## PUT /api/cafes/{cafeId}/pos/tables

Đồng bộ toàn bộ sơ đồ bàn — tạo mới, đổi tên, đổi SeatCount, hoặc xóa mềm bàn không còn trong layout.

**Role:** Manager — phải là chủ quán (ManagerId của cafe).

**Body — hỗ trợ 2 shape (chọn 1, không gửi cả 2):**

### Shape A (legacy) — chỉ tên

```json
{
  "tableNames": ["Bàn 1", "Bàn 2", "Bàn 3"]
}
```

- Bàn mới → SeatCount mặc định 4.
- Bàn đã tồn tại (match case-insensitive theo Name) → chỉ cập nhật Name/SortOrder, **giữ nguyên SeatCount**.
- Bàn active không có trong list → soft-delete (IsActive=false) nếu Status = Available.

### Shape B (mới) — Name + SeatCount + SortOrder

```json
{
  "tables": [
    { "name": "Bàn 1", "seatCount": 4, "sortOrder": 0 },
    { "name": "Bàn VIP", "seatCount": 8, "sortOrder": 1 },
    { "name": "Bàn 3", "seatCount": 6 }
  ]
}
```

- `seatCount` 1–50; `sortOrder` 0–9999; cả 2 đều optional.
- Bàn mới → dùng SeatCount từ payload, hoặc default 4 nếu null.
- Bàn đã tồn tại (match case-insensitive theo Name) → cập nhật Name/SortOrder, **SeatCount chỉ ghi đè khi payload có giá trị** (nếu null → giữ nguyên).
- Bàn active không có trong list → soft-delete nếu Status = Available.
- `sortOrder` null → dùng index trong mảng.

**Validation:**
- Phải có ít nhất 1 phần tử trong `tableNames` hoặc `tables`.
- Không được gửi cả 2 shape cùng lúc → 400.
- `name` 1–100 ký tự, trim khoảng trắng 2 đầu.
- `seatCount` 1–50; `sortOrder` 0–9999.

**Cơ chế matching (quan trọng cho rename):**

Để tránh làm đứt FK từ `Booking` / `ActiveSession` (link theo `CafeTableId`), hệ thống match theo 3 phase:

1. **Phase 1 — Match by Name** (case-insensitive): tìm bàn active hoặc inactive trùng tên → giữ nguyên `Id`, cập nhật `Name`/`SortOrder`/`SeatCount`. Đây là "rename trực tiếp" (giữ cùng vị trí hoặc đổi tên).
2. **Phase 2 — Match by SortOrder** (rename ngầm): với target chưa match ở Phase 1, nếu có bàn active ở cùng `SortOrder` index (chưa matched) → coi như rename, gán target đó cho bàn đó. **Giữ nguyên `Id`**.
3. **Phase 3 — Tạo mới**: target còn lại chưa match → tạo bàn mới với `Id` mới.

**Soft-delete:** bàn active không nằm trong matchedIds và không có trong payload (theo tên) → chỉ soft-delete (`IsActive=false`) nếu `Status = Available`. Bàn đang có session chạy (`Status ∈ {InUse, Reserved, EventInProgress}`) **không bị soft-delete** để bảo toàn lịch sử.

**SeatCount semantics:**

| Trường hợp | Kết quả |
|---|---|
| Tạo bàn mới, `seatCount = null` | Default 4 |
| Tạo bàn mới, `seatCount = 8` | 8 |
| Match bàn cũ, `seatCount = null` | Giữ nguyên seatCount hiện tại |
| Match bàn cũ, `seatCount = 8` | Ghi đè thành 8 |
| Bàn active không nằm trong list, `Status = Available` | Soft-delete (`IsActive=false`) |
| Bàn active không nằm trong list, `Status ≠ Available` | Giữ nguyên (an toàn) |

**Ví dụ rename:**

DB có:
- `Bàn 1` (Id=`A`, SeatCount=4, SortOrder=0)
- `Bàn 2` (Id=`B`, SeatCount=4, SortOrder=1)

Payload rename cả 2:
```json
{"tables": [
  {"name": "VIP", "seatCount": 8, "sortOrder": 0},
  {"name": "Standard", "seatCount": 4, "sortOrder": 1}
]}
```

Kết quả:
- Bàn có `Id=A` được rename thành "VIP", SeatCount=8 (giữ Id).
- Bàn có `Id=B` được rename thành "Standard", SeatCount=4 (giữ Id).
- Booking/ActiveSession cũ link tới `Id=A` và `Id=B` vẫn còn nguyên — **không mất lịch sử**.

**Response 200:** `CafeTableStatusDto[]` (danh sách bàn sau sync).

**Lỗi:**
- `400` payload rỗng / gửi cả 2 shape / seatCount ngoài 1–50 / sortOrder ngoài 0–9999 / name trống.
- `401` thiếu token.
- `403` không phải Manager chủ quán.
- `404` không tìm thấy quán.

**Ví dụ curl:**
```bash
# Shape A — legacy
curl -X PUT "https://api.boardverse.local/api/cafes/{cafeId}/pos/tables" \
  -H "Authorization: Bearer <manager-jwt>" \
  -H "Content-Type: application/json" \
  -d '{"tableNames": ["Bàn 1", "Bàn 2", "Bàn 3"]}'

# Shape B — mới (set SeatCount ngay khi tạo / đổi tên)
curl -X PUT "https://api.boardverse.local/api/cafes/{cafeId}/pos/tables" \
  -H "Authorization: Bearer <manager-jwt>" \
  -H "Content-Type: application/json" \
  -d '{"tables": [
        {"name": "Bàn 1", "seatCount": 4, "sortOrder": 0},
        {"name": "Bàn VIP", "seatCount": 8, "sortOrder": 1}
      ]}'
```

**Sau khi sync → `TableLayoutJson` (cache trên `Cafe`) được tự động refresh** với danh sách tên bàn active theo SortOrder.

---

## PATCH /api/cafes/{cafeId}/pos/tables/{tableId}

Cập nhật một phần thông tin bàn. Dùng để **đổi `SeatCount` cho từng bàn** (ảnh hưởng đến `AvailableSeats` cho booking — BR-05).

**Role:** Manager — phải là chủ quán (ManagerId của cafe). CafeStaff không được phép.

**Path params:**
- `cafeId` (guid): mã định danh quán.
- `tableId` (guid): mã định danh bàn cần cập nhật.

**Body mẫu** (tất cả field optional — chỉ field gửi mới được cập nhật):
```json
{
  "name": "Bàn 1 (VIP)",
  "seatCount": 8,
  "sortOrder": 0
}
```

**Validation:**
- `name`: 1–100 ký tự, trim khoảng trắng 2 đầu; không được trùng với bàn active khác trong quán (case-insensitive).
- `seatCount`: 1–50 (range cố định cho boardgame cafe).
- `sortOrder`: 0–9999.
- Ít nhất một trong 3 field phải được gửi — nếu không sẽ trả `400`.
- Bàn **không được có `ActiveSession` đang chạy** (`Status ∈ {Active, Checking, Unpaid}`). Khi có session phải kết thúc phiên trước khi đổi `SeatCount` (vì lý do BR-05: `AvailableSeats = sum(SeatCount)` runtime).

**Response 200:** `CafeTableStatusDto`
```json
{
  "id": "guid",
  "name": "Bàn 1 (VIP)",
  "sortOrder": 0,
  "seatCount": 8,
  "status": "Available"
}
```

**Lỗi:**
- `400` Validation — không có trường nào / name dài quá / seatCount ngoài 1–50 / sortOrder ngoài 0–9999 / name trống.
- `401` thiếu token.
- `403` không phải Manager chủ quán.
- `404` không tìm thấy bàn trong quán.
- `409` bàn đang có phiên chơi hoạt động, hoặc tên bàn đã trùng với bàn khác.

**Differ vs `PUT /tables`:**
| | `PUT /tables` (sync all) | `PATCH /tables/{tableId}` (partial) |
|--|---|---|
| Use case | Manager thiết kế lại toàn bộ sơ đồ | Tinh chỉnh 1 bàn (SeatCount, đổi tên) |
| Shape | `tableNames[]` (legacy) hoặc `tables[]` (Name + SeatCount + SortOrder) | `name`, `seatCount`, `sortOrder` từng bàn |
| SeatCount? | ✅ (shape mới) / ❌ (shape cũ giữ nguyên) | ✅ cập nhật được |
| Xóa mềm? | ✅ tự động (bàn không có trong list) | ❌ giữ nguyên |
| Khi có session? | ✅ cập nhật được | ❌ bị chặn |

**Ví dụ curl:**
```bash
curl -X PATCH "https://api.boardverse.local/api/cafes/{cafeId}/pos/tables/{tableId}" \
  -H "Authorization: Bearer <manager-jwt>" \
  -H "Content-Type: application/json" \
  -d '{"seatCount": 8}'
```

---

## GET /api/cafes/{cafeId}/pos/bookings/{bookingCode}

Preview thông tin booking trước khi check-in.

**AC 1.1:** Hiển thị danh sách thành viên + game info TRƯỚC khi check-in.

**Query params:**
- `bookingCode` (path): Mã đặt chỗ (OrderId)

**Response 200:**
```json
{
  "bookingCode": "BV12345678",
  "depositStatus": "Paid",
  "depositAmount": 50000,
  "scheduledStartTime": "2026-08-01T19:00:00Z",
  "registeredMemberCount": 4,
  "canCheckIn": true,
  "host": {
    "userId": "guid",
    "displayName": "Nguyễn Văn A",
    "avatarUrl": "https://...",
    "karmaScore": 85
  },
  "lobby": {
    "lobbyId": "guid",
    "gameName": "Catan",
    "minPlayers": 3,
    "maxPlayers": 4,
    "currentMemberCount": 4
  }
}
```

**Lỗi:**
- `401` thiếu token
- `403` không đủ quyền
- `404` không tìm thấy booking

---

## POST /api/cafes/{cafeId}/pos/sessions/from-booking

Host-led check-in: Quét mã đặt chỗ (BookingCode/OrderId) để kích hoạt phiên chơi cho cả nhóm.

**AC 1.2:** Nhân viên bấm xác nhận check-in → Trạng thái bàn chuyển sang Occupied.

**AC 1.3:** `DepositAppliedAmount = 0` — Thanh toán session KHÔNG trừ tiền cọc (BR-09 mới).

**AC 1.4:** Gửi SignalR notification để mobile app cập nhật "Đang chơi tại quán".

**Bug Fixes:**
- **Double check-in prevention:** Kiểm tra `Booking.Status == CheckedIn` trước khi check-in. Nếu đã check-in rồi → throw `ConflictException`.
- **Booking.Status update:** Sau khi check-in thành công, `Booking.Status` được cập nhật từ `Confirmed` → `CheckedIn`.

**Body mẫu:**
```json
{
  "bookingCode": "BV12345678",
  "cafeTableId": "guid",
  "barcode": "BV-bbbbbbbb-xxxxxxxx-001"
}
```

**Response 201:** `ActiveSessionDto`

**Lỗi:**
- `400` mã đặt chỗ không hợp lệ
- `401` thiếu token
- `403` không đủ quyền
- `404` quán, bàn hoặc game không tồn tại
- `409` đơn đặt chỗ chưa thanh toán, đã check-in rồi, hoặc bàn/game không khả dụng

---

## POST /api/cafes/{cafeId}/pos/sessions

POS quét barcode và chọn bàn → tạo `ActiveSession`, đặt hộp `InUse`, bàn `InUse` nếu đang trống.

**Body mẫu:**

```json
{
  "cafeTableId": "table-id",
  "barcode": "BV-bbbbbbbb-xxxxxxxx-002",
  "bookingId": "booking-id",
  "lobbyId": "lobby-id",
  "initialMemberUserIds": ["user-id-1"]
}
```

**Response 201:** `ActiveSessionDto` — `startedAt`, `elapsedMinutes`, `estimatedRemainingMinutes`, `defaultPlayTimeMinutes`.

**Lỗi:** `404` bàn/barcode; `409` hộp không Available, đã có session, hoặc bàn Reserved/Event.

---

## POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/end

Kết thúc phiên chơi — trả hộp game và giải phóng bàn nếu không còn session khác.

**Bug Fix:** Sau khi trả game (ReturnGameAsync), trạng thái hộp game được cập nhật:
- Nếu **không có linh kiện hỏng**: Box → `Available` (sẵn sàng cho phiên tiếp theo)
- Nếu **có linh kiện hỏng**: Box → `Maintenance` (cần bảo trì trước khi dùng lại)

**Response 200:** Phiên đã đóng; hộp về Available/Maintenance; bàn về Available khi không còn session trên bàn đó.

**Lỗi:** `404` không tìm thấy phiên; `500` lỗi hệ thống.

---

## SignalR Notifications

**Hub:** `/hubs/pos`

Khi check-in thành công, hệ thống gửi event `SessionActivated` đến mobile app.

**Event Payload:**
```json
{
  "eventType": "SessionActivated",
  "sessionId": "guid",
  "cafeId": "guid",
  "cafeName": "Board Game Cafe",
  "hostId": "guid",
  "timestamp": "2026-08-01T19:00:00Z"
}
```

**Mobile app cần:**
1. Connect đến `/hubs/pos`
2. Gọi `JoinUserNotifications(userId)` để subscribe notifications cá nhân
3. Listen event `SessionActivated` để cập nhật UI → "Đang chơi tại quán"

---

## Luồng test

```powershell
# 1. Login staff/manager
# 2. GET .../pos/bookings/{bookingCode}  (preview booking)
#    → Xem thông tin: host, game, số người, trạng thái cọc
# 3. POST .../pos/sessions/from-booking  (check-in)
#    → Tạo session, bàn → InUse, gửi SignalR notification
# 4. POST .../pos/sessions/{id}/end
#    → Kết thúc phiên
```
