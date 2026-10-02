# Operational Profile (Hồ sơ vận hành quán)

> **Phạm vi:** Tài liệu tổng hợp cho **Phase 2 — Operational Profile** của quán đối tác BoardVerse, bao gồm endpoint `PUT /api/manager/cafes/me/operational-profile`, state machine cho phép chỉnh sửa, validation rules, derived fields, và mối liên hệ với luồng Activate / Deactivate / Close.
>
> **Trạng thái Phase:** Phase 1 (đăng ký đối tác) → Phase 2 (hồ sơ vận hành) → Active.
> Tài liệu liên quan:
> - Phase 1 (đơn đăng ký): [cafe-partner.md](./cafe-partner.md)
> - Hồ sơ quán manager: [manager-cafe-profile.md](./manager-cafe-profile.md)
> - Cafe detail / pricing: [cafe.md](./cafe.md)

---

## Mục lục

- [1. Tổng quan](#1-tổng-quan)
- [2. State machine cho phép chỉnh sửa](#2-state-machine-cho-phép-chỉnh-sửa)
- [3. Endpoint](#3-endpoint)
  - [3.1 `PUT /api/manager/cafes/me/operational-profile`](#31-put-apimanagercafesmeoperational-profile)
- [4. Validation rules](#4-validation-rules)
- [5. PATCH semantics](#5-patch-semantics)
- [6. Derived fields (server-side)](#6-derived-fields-server-side)
- [7. Lỗi thường gặp](#7-lỗi-thường-gặp)
- [8. Mapping với Business Rules](#8-mapping-với-business-rules)
- [9. Ví dụ](#9-ví-dụ)
- [10. Tích hợp với luồng Activate](#10-tích-hợp-với-luồng-activate)
- [11. Audit log](#11-audit-log)
- [12. Liên kết](#12-liên-kết)

---

## 1. Tổng quan

**Hồ sơ vận hành (operational profile)** là tập thông tin Manager cấu hình **sau khi Admin duyệt đơn đăng ký đối tác (Phase 1)** và **trước khi quán được Activate (chuyển sang `ACTIVE`)**.

Dữ liệu lưu trên entity `Cafe`:

| Nhóm | Field `Cafe` | Mô tả |
|------|--------------|-------|
| Giờ mở cửa | `WeekdayOpen`, `WeekdayClose`, `WeekendOpen`, `WeekendClose` (`TimeSpan?`) | Khung giờ T2–T6 và T7–CN |
| Hạ tầng | `NumberOfPrivateRooms` (`int`), `SpaceImageUrlsJson` (`string`, JSON), `HasGameMaster` (`bool`) | Phòng riêng, ảnh không gian, Game Master |
| Biểu phí (BR-01) | `BillingModel`, `BasePrice`, `TieredBlockRate?`, `TieredBlockMinutes` | Mô hình tính tiền + giá base + block lũy tiến |
| Cọc (BR-02/03) | `DepositPercentage` (`decimal`) | % cọc so với giá base, tối đa 50% |
| Audit | `OperationalProfileUpdatedAt` (`DateTime?`) | Lần cuối cập nhật profile |

> **Lưu ý lịch sử:** Trước đây `NumberOfTables` / `NumberOfGamesOwned` / `PopularGamesList` cũng nằm trong payload `PUT /operational-profile`, nhưng đã được **derive server-side** từ `CafeTables` và `CafeGameInventory` để tránh trùng lặp dữ liệu (xem §6).

Hồ sơ được **khóa khi quán đang `ACTIVE`** (BR-04) — Manager phải `Deactivate` (chuyển sang `DATA_BLANK`) trước khi cập nhật lại.

---

## 2. State machine cho phép chỉnh sửa

Endpoint chỉ chấp nhận PUT khi `Cafe.PartnerOperationalStatus` thuộc một trong các trạng thái sau:

| Trạng thái hiện tại | Cho phép `PUT /operational-profile`? | Lý do |
|---------------------|:---:|-------|
| `DATA_BLANK` | ✅ | Sau admin duyệt, manager đang cấu hình quán mới |
| `ACTIVE` | ❌ | **BR-04 khóa biến động giá** khi quán đang hoạt động — phải `POST /deactivate` trước |
| `INACTIVE` | ✅ | Quán đã đóng vĩnh viễn, manager có thể chuẩn bị mở lại |
| `BANNED` | ❌ | Admin cấm — Manager phải liên hệ Admin |

State guard được thực thi ở `CafePartnerApplicationService.EnsureOperationalStateAllowsEdit` — nếu vi phạm sẽ throw exception tương ứng (`CafeBannedByAdmin` hoặc `PauseBeforeEditingProfile`).

```
   Admin Approve              Manager cập nhật          Manager Activate
PENDING_APPROVAL ────────►  DATA_BLANK  ◄──────► INACTIVE  ───────► ACTIVE
                                  │                ▲
                                  │   Manager      │
                                  │   Deactivate   │
                                  └──────────────► DATA_BLANK
                                       (chỉ từ ACTIVE)
```

---

## 3. Endpoint

### 3.1 `PUT /api/manager/cafes/me/operational-profile`

Cập nhật hồ sơ vận hành (Giai đoạn 2) cho quán của Manager đang đăng nhập. Hỗ trợ **partial update** cho một số field (xem §5).

#### Request

- Method: `PUT`
- Path: `/api/manager/cafes/me/operational-profile`
- Auth: `Bearer JWT`, role `Manager`
- Source: `ManagerCafeProfileController.UpdateOperationalProfile`
- Service: `ICafePartnerApplicationService.UpdateOperationalProfileAsync`

#### Request Body (`UpdateOperationalProfileRequestDto`)

```json
{
  "workingHours": {
    "weekdayStart": "09:00",
    "weekdayEnd": "22:00",
    "weekendStart": "10:00",
    "weekendEnd": "23:00"
  },
  "numberOfPrivateRooms": 2,
  "spaceImageUrls": [
    "https://cdn.example.com/facade.jpg",
    "https://cdn.example.com/play-area.jpg",
    "https://cdn.example.com/lighting.jpg"
  ],
  "hasGameMaster": true,
  "billingModel": "TIME_BASED",
  "basePrice": 50000,
  "tieredBlockRate": 3000,
  "tieredBlockMinutes": 15,
  "depositPercentage": 0.3
}
```

| Field | Type | Required | Validation | Mô tả |
|-------|------|:--------:|------------|-------|
| `workingHours` | `WorkingHoursDto` | ✅ | `weekdayStart < weekdayEnd`, `weekendStart < weekendEnd`, format `HH:mm` | Khung giờ mở cửa T2–T6 và T7–CN |
| `workingHours.weekdayStart` | `string` | ✅ | `HH:mm` (vd `09:00`) | Giờ mở cửa T2–T6 |
| `workingHours.weekdayEnd` | `string` | ✅ | `HH:mm`, phải sau `weekdayStart` | Giờ đóng cửa T2–T6 |
| `workingHours.weekendStart` | `string` | ✅ | `HH:mm` | Giờ mở cửa T7–CN |
| `workingHours.weekendEnd` | `string` | ✅ | `HH:mm`, phải sau `weekendStart` | Giờ đóng cửa T7–CN |
| `numberOfPrivateRooms` | `int` |  | `0 ≤ x ≤ 1000` | Số phòng riêng tư (VIP room) — `0` nếu không có |
| `spaceImageUrls` | `string[]` | ✅ | `Count ≥ 3` (khi tạo mới), mỗi URL phải có extension `.jpg`/`.jpeg`/`.png` | Ảnh không gian quán |
| `hasGameMaster` | `bool` |  | — | Có nhân viên hỗ trợ hướng dẫn luật game hay không |
| `billingModel` | `CafePartnerBillingModel` | ✅ | `TimeBased` hoặc `FlatEntry` | Mô hình tính tiền (BR-01) |
| `basePrice` | `decimal` | ✅ | `0 ≤ x ≤ 10,000,000` | Giá giờ đầu / giá vé vào cổng (VND) |
| `tieredBlockRate` | `decimal?` | (khi `TimeBased`) | `> 0` | Giá mỗi block lũy tiến (VND) — bắt buộc với `TimeBased`, **bỏ qua với `FlatEntry`** |
| `tieredBlockMinutes` | `int?` |  | `1 ≤ x ≤ 1440` | Phút mỗi block. **PATCH semantics:** nếu `null` thì giữ giá trị hiện tại |
| `depositPercentage` | `decimal?` |  | `0 ≤ x ≤ 0.5` (BR-03) | % cọc so với `basePrice`. **PATCH semantics:** nếu `null` thì giữ giá trị hiện tại |

#### Response 200

Trả về `ManagerCafeProfileResponseDto` đầy đủ (cùng shape với `GET /api/manager/cafes/me`):

```json
{
  "statusCode": 200,
  "message": "Cập nhật hồ sơ vận hành thành công.",
  "data": {
    "cafeId": "guid",
    "applicationId": "guid",
    "name": "Board Game Cafe",
    "address": "123 Đường ABC, Quận 1, TP.HCM",
    "latitude": 10.776889,
    "longitude": 106.700806,
    "phoneNumber": "0901234567",
    "workingHours": {
      "weekdayStart": "09:00",
      "weekdayEnd": "22:00",
      "weekendStart": "10:00",
      "weekendEnd": "23:00"
    },
    "numberOfTables": 5,
    "numberOfPrivateRooms": 2,
    "spaceImageUrls": [
      "https://cdn.example.com/facade.jpg",
      "https://cdn.example.com/play-area.jpg",
      "https://cdn.example.com/lighting.jpg"
    ],
    "numberOfGamesOwned": 25,
    "popularGamesList": "",
    "hasGameMaster": true,
    "billingModel": "TIME_BASED",
    "basePrice": 50000,
    "tieredBlockRate": 3000,
    "tieredBlockMinutes": 15,
    "depositPercentage": 0.3,
    "defaultHoldDurationMinutes": 30,
    "isPricingLocked": false,
    "applicationStatus": "APPROVED",
    "operationalStatus": "DATA_BLANK",
    "operationalStatusReason": null,
    "isTableLayoutConfigured": true,
    "canActivate": true,
    "canReopen": false,
    "activationBlockers": [],
    "approvedAt": "2026-07-30T10:00:00Z",
    "operationalProfileUpdatedAt": "2026-08-01T15:00:00Z"
  }
}
```

> **Lưu ý:** `canActivate` = `true` ⇔ quán đang `DATA_BLANK`/`INACTIVE` **và** đủ điều kiện kích hoạt (≥5 bàn, ≥20 hộp game, ≥3 ảnh không gian, giờ mở cửa hợp lệ, GPS).

#### Status codes

| Code | Khi nào |
|------|---------|
| `200 OK` | Cập nhật thành công |
| `400 Bad Request` | Validation lỗi (xem §7) |
| `401 Unauthorized` | Thiếu token, token hết hạn, hoặc token không hợp lệ |
| `403 Forbidden` | Tài khoản không có role `Manager` |
| `404 Not Found` | Manager chưa có quán đối tác đã được duyệt |
| `409 Conflict` | Quán đang ở trạng thái `ACTIVE` (cần `POST /deactivate` trước) hoặc `BANNED` |
| `500 Internal Server Error` | Lỗi hệ thống không mong đợi |

---

## 4. Validation rules

Được thực thi trong `CafePartnerApplicationService.ValidatePhase2Request` và DataAnnotation trên DTO.

| # | Rule | Nguồn | Thông báo lỗi (key trong `ApiErrorMessages.CafePartner` / `ApiErrorMessages.Validation`) |
|---|------|-------|---------------------------------------------------------------------------------------------------|
| V-01 | `workingHours` không được null | DTO | `Validation.WorkingHoursRequired` |
| V-02 | Mỗi field thời gian phải đúng `HH:mm` | Service | `CafePartner.TimeFormatInvalid(fieldName)` |
| V-03 | `weekdayStart < weekdayEnd` | Service | `CafePartner.WeekdayHoursInvalid` |
| V-04 | `weekendStart < weekendEnd` | Service | `CafePartner.WeekendHoursInvalid` |
| V-05 | `numberOfPrivateRooms ≥ 0` | DTO + Service | `Validation.PrivateRoomCountRange` / `CafePartner.PrivateRoomCountCannotBeNegative` |
| V-06 | `numberOfPrivateRooms ≤ 1000` | DTO | `Validation.PrivateRoomCountRange` |
| V-07 | `spaceImageUrls.Count ≥ MinSpaceImages` (mặc định **3**) | Service | `CafePartner.MinSpaceImagesRequired(min)` |
| V-08 | Mỗi URL trong `spaceImageUrls` phải có extension `.jpg` / `.jpeg` / `.png` | Service | `CafePartner.SpaceImagesFormatInvalid` |
| V-09 | `billingModel ∈ { TimeBased, FlatEntry }` | DTO (enum) | (auto) |
| V-10 | `basePrice ≥ 0` và `≤ 10,000,000` | DTO + Service | `Validation.BasePriceRange` |
| V-11 | Nếu `billingModel = TimeBased`: `tieredBlockRate` bắt buộc và `> 0` | Service | `Validation.TieredBlockRateRequired` |
| V-12 | Nếu `billingModel = FlatEntry`: `tieredBlockRate` bị ignore (set `null`) | Service | — |
| V-13 | `tieredBlockMinutes` ∈ `[1, 1440]` (nếu gửi) | DTO | `Validation.TieredBlockMinutesRange` |
| V-14 | `depositPercentage` ∈ `[0, 0.5]` — BR-03 trần cọc 50% (nếu gửi) | DTO + Service | `Validation.DepositPercentageRange` |
| V-15 | `depositPercentage` không vượt quá 50% `basePrice` (nếu áp dụng) | (kiểm tra ở controller khác khi lưu giá) | `CafePartner.DepositExceedsBasePriceLimit` |
| V-16 | `SpaceImageUrls` trống từng phần tử → bị filter bỏ trước khi lưu | Service | (silent) |

> **`MinSpaceImages`** định nghĩa trong `BoardVerse.Core/Constants/CafePartnerActivationRules.cs` — hiện tại `= 3`.

---

## 5. PATCH semantics

Một số field có **partial update** — nếu client gửi `null` thì **giữ giá trị hiện tại** trong DB:

| Field | `null` trong request | Hành vi |
|-------|:---:|---------|
| `tieredBlockMinutes` | ✅ giữ nguyên | `cafe.TieredBlockMinutes` không đổi (mặc định `15`) |
| `depositPercentage` | ✅ giữ nguyên | `cafe.DepositPercentage` không đổi (mặc định `0.5`) |
| `tieredBlockRate` | ✅ (khi `TimeBased`) | **KHÔNG** giữ — nếu `null` mà `billingModel = TimeBased` sẽ lỗi V-11 |
| `tieredBlockRate` | (khi `FlatEntry`) | Service set `cafe.TieredBlockRate = null` (vì `FlatEntry` không dùng) |

> Lý do: `tieredBlockMinutes` và `depositPercentage` thường được Manager chỉnh **một mình** (đổi block 15 phút → 30 phút, hoặc đổi cọc 30% → 20%) mà không muốn mất công gửi lại toàn bộ payload.

Các field **khác** trong `UpdateOperationalProfileRequestDto` là **required** — nếu thiếu sẽ dùng giá trị mặc định của DTO (`0` cho int, `false` cho bool, `new()` cho object, `[]` cho list).

**Mẹo cho Frontend:** muốn update chỉ 1 field, vẫn phải gửi đầy đủ các field khác. Hãy `GET /api/manager/cafes/me` trước → merge → `PUT`.

---

## 6. Derived fields (server-side)

Ba field sau **không có trong request body** nhưng luôn có trong response — chúng được **derive từ navigation collections** tại tầng service (`MapManagerCafeProfile`):

| Field response | Công thức | Endpoint quản lý |
|----------------|-----------|------------------|
| `numberOfTables` | `cafe.Tables.Count(t => t.IsActive)` | `PUT /api/cafes/{cafeId}/pos/tables` |
| `numberOfGamesOwned` | `cafe.Inventories.Where(i => i.IsActive).Sum(i => i.BoxQuantity)` | `POST /api/cafes/{cafeId}/inventory` |
| `isTableLayoutConfigured` | `numberOfTables > 0 && all active tables have non-empty name` | (tự tính) |

Lý do: Trước đây các field này là scalar trên `Cafe`, nhưng dữ liệu bị trùng với `CafeTables` và `CafeGameInventory`. Refactor 2026-08 chuyển sang **single source of truth = navigation collection**, mọi thay đổi phải đi qua endpoint POS tương ứng.

| ❌ Trước | ✅ Bây giờ |
|---------|------------|
| `PUT /operational-profile` cập nhật `numberOfTables` | `PUT /pos/tables` cập nhật `CafeTables` → response tự động derive lại |
| Tạo inventory riêng qua `PUT /operational-profile` | `POST /inventory` riêng, derive từ `BoxQuantity` |

---

## 7. Lỗi thường gặp

| Status | Key message | Nguyên nhân thường gặp |
|:------:|-------------|-----------------------|
| 400 | `WorkingHoursRequired` | `workingHours` null |
| 400 | `TimeFormatInvalid("weekdayStart")` | Sai format, vd `9:00` thay vì `09:00` |
| 400 | `WeekdayHoursInvalid` | `weekdayStart >= weekdayEnd` |
| 400 | `PrivateRoomCountCannotBeNegative` | `numberOfPrivateRooms < 0` |
| 400 | `MinSpaceImagesRequired(3)` | `spaceImageUrls` có < 3 URL |
| 400 | `SpaceImagesFormatInvalid` | URL không kết thúc bằng `.jpg`/`.jpeg`/`.png` |
| 400 | `BasePriceRange` | `basePrice` âm hoặc > 10 triệu |
| 400 | `TieredBlockRateRequired` | `billingModel=TimeBased` mà thiếu `tieredBlockRate` |
| 400 | `TieredBlockMinutesRange` | `tieredBlockMinutes` ngoài `[1, 1440]` |
| 400 | `DepositPercentageRange` | `depositPercentage` ngoài `[0, 0.5]` (BR-03) |
| 404 | `CafePartner.ApplicationNotFoundForManager` | Manager chưa có application đã duyệt |
| 409 | `CafePartner.PauseBeforeEditingProfile` | Quán đang `ACTIVE` — phải `POST /deactivate` trước |
| 409 | `CafePartner.CafeBannedByAdmin` | Quán `BANNED` — liên hệ Admin |

> Tham chiếu đầy đủ: `BoardVerse.Core/Messages/ApiErrorMessages.cs` — class lồng `CafePartner` và `Validation`.

---

## 8. Mapping với Business Rules

| BR | Nội dung | Áp dụng trong `PUT /operational-profile` |
|----|----------|------------------------------------------|
| **BR-01** | Quán chọn 1 trong 2 mô hình tính tiền (TimeBased / FlatEntry) | `billingModel` enum + validation V-11 (TieredBlockRate bắt buộc với TimeBased) |
| **BR-02** | Phí đặt cọc do Manager cấu hình | `depositPercentage` field |
| **BR-03** | Trần cọc ≤ 50% giá vé | Validation V-14: `depositPercentage ∈ [0, 0.5]` |
| **BR-04** | Khóa biến động giá khi quán hoạt động | State guard ở `EnsureOperationalStateAllowsEdit` — `ACTIVE → 409 PauseBeforeEditingProfile` |
| **BR-06** | Giữ chỗ mặc định tối đa 30 phút | `defaultHoldDurationMinutes` (đọc-only ở endpoint này, chỉnh qua `PUT /api/cafes/{id}/pricing-config` hoặc admin) |

> **Lưu ý:** BR-05, BR-07, BR-08 không liên quan trực tiếp đến operational profile — chúng thuộc luồng booking.

---

## 9. Ví dụ

### 9.1. Tạo mới profile sau khi Admin duyệt (quán `DATA_BLANK`)

**Request:**

```http
PUT /api/manager/cafes/me/operational-profile
Authorization: Bearer <manager-jwt>
Content-Type: application/json
```

```json
{
  "workingHours": {
    "weekdayStart": "09:00",
    "weekdayEnd": "22:00",
    "weekendStart": "10:00",
    "weekendEnd": "23:00"
  },
  "numberOfPrivateRooms": 1,
  "spaceImageUrls": [
    "https://cdn.example.com/cafe-1.jpg",
    "https://cdn.example.com/cafe-2.jpg",
    "https://cdn.example.com/cafe-3.jpg"
  ],
  "hasGameMaster": true,
  "billingModel": "TIME_BASED",
  "basePrice": 60000,
  "tieredBlockRate": 5000,
  "tieredBlockMinutes": 15,
  "depositPercentage": 0.3
}
```

**Response 200:**

```json
{
  "statusCode": 200,
  "message": "Cập nhật hồ sơ vận hành thành công.",
  "data": {
    "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "operationalStatus": "DATA_BLANK",
    "canActivate": false,
    "activationBlockers": [
      "Cần tối thiểu 5 bàn công cộng.",
      "Cần tối thiểu 20 bản game trong kho (tính theo tổng số lượng hộp, không phải số loại game)."
    ],
    "operationalProfileUpdatedAt": "2026-08-01T15:00:00Z"
  }
}
```

> `canActivate=false` vì mới có profile nhưng chưa có bàn (phải `PUT /pos/tables`) và chưa có game (phải `POST /inventory`).

### 9.2. Update một field (PATCH semantics)

Manager muốn đổi block từ 15 phút → 30 phút, giữ nguyên tất cả field khác:

```http
PUT /api/manager/cafes/me/operational-profile
Authorization: Bearer <manager-jwt>
Content-Type: application/json
```

```json
{
  "workingHours": {
    "weekdayStart": "09:00",
    "weekdayEnd": "22:00",
    "weekendStart": "10:00",
    "weekendEnd": "23:00"
  },
  "numberOfPrivateRooms": 1,
  "spaceImageUrls": [
    "https://cdn.example.com/cafe-1.jpg",
    "https://cdn.example.com/cafe-2.jpg",
    "https://cdn.example.com/cafe-3.jpg"
  ],
  "hasGameMaster": true,
  "billingModel": "TIME_BASED",
  "basePrice": 60000,
  "tieredBlockRate": 5000,
  "tieredBlockMinutes": 30,
  "depositPercentage": null
}
```

> `depositPercentage: null` → giữ giá trị cũ (0.3) trong DB.

### 9.3. Cập nhật khi quán đang `ACTIVE` (lỗi)

```http
PUT /api/manager/cafes/me/operational-profile
```

```json
{ ... }
```

**Response 409:**

```json
{
  "statusCode": 409,
  "isSuccess": false,
  "message": "Quán đang hoạt động. Vui lòng tạm dừng (deactivate) trước khi chỉnh sửa hồ sơ.",
  "data": null
}
```

**Fix:**

```http
POST /api/manager/cafes/me/deactivate
```

Sau đó thử lại `PUT /operational-profile`.

### 9.4. PowerShell

```powershell
$token = "eyJhbGciOi..." # Manager JWT

$body = @{
  workingHours = @{
    weekdayStart = "09:00"
    weekdayEnd   = "22:00"
    weekendStart = "10:00"
    weekendEnd   = "23:00"
  }
  numberOfPrivateRooms = 1
  spaceImageUrls = @(
    "https://cdn.example.com/cafe-1.jpg",
    "https://cdn.example.com/cafe-2.jpg",
    "https://cdn.example.com/cafe-3.jpg"
  )
  hasGameMaster = $true
  billingModel  = "TIME_BASED"
  basePrice = 60000
  tieredBlockRate = 5000
  tieredBlockMinutes = 15
  depositPercentage = 0.3
} | ConvertTo-Json -Depth 5

Invoke-RestMethod `
  -Uri "http://localhost:5022/api/manager/cafes/me/operational-profile" `
  -Method Put `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body $body
```

---

## 10. Tích hợp với luồng Activate

Sau khi cập nhật hồ sơ, Manager tiếp tục:

```
1. PUT  /api/manager/cafes/me/operational-profile    ← document này
2. PUT  /api/cafes/{cafeId}/pos/tables              ← tạo/cập nhật bàn (numberOfTables)
3. POST /api/cafes/{cafeId}/inventory               ← thêm game vào kho (numberOfGamesOwned)
4. POST /api/manager/cafes/me/activate               ← chuyển DATA_BLANK → ACTIVE
        │
        ├── ✅ Đủ điều kiện (≥5 bàn, ≥20 hộp, ≥3 ảnh, giờ mở cửa, GPS)
        │       → status = ACTIVE
        │       → emit email "Quán đã kích hoạt"
        │
        └── ❌ Thiếu điều kiện
                → 400 + danh sách `activationBlockers`
```

Điều kiện kích hoạt (validate tại `CafePartnerApplicationService.GetActivationBlockers`):

| Điều kiện | Mặc định | Nguồn cấu hình |
|-----------|:---:|----------------|
| Số bàn chơi công cộng active | ≥ 5 | `CafePartnerActivationRules.MinPublicTables` |
| Tổng số hộp game active | ≥ 20 | `CafePartnerActivationRules.MinGamesOwned` |
| Số ảnh không gian hợp lệ | ≥ 3 | `CafePartnerActivationRules.MinSpaceImages` |
| Đã cấu hình giờ mở cửa | ✅ | `Cafe.WeekdayOpen/Close` khác null |
| Đã cấu hình sơ đồ bàn (tất cả bàn active có tên) | ✅ | Tất cả `CafeTable.Name` non-empty |
| Có GPS (Latitude/Longitude) | ✅ | `Cafe.Latitude` và `Cafe.Longitude` khác null |

> Tham chiếu: [`POST /api/manager/cafes/me/activate`](./manager-cafe-profile.md#post-activate).

### Thay đổi 1 lệnh gộp (Phase 4+)

Từ phiên bản hỗ trợ `PATCH /operational-status` (xem [manager-cafe-profile.md §PATCH /operational-status](./manager-cafe-profile.md#patch-operational-status)), Manager có thể gộp 3 bước `/activate`, `/deactivate`, `/close`, `/reopen` thành 1 endpoint hợp nhất — nhưng `PUT /operational-profile` vẫn là bước chỉnh sửa nội dung hồ sơ, tách biệt với chuyển trạng thái.

---

## 11. Audit log

Mỗi lần `PUT /operational-profile` thành công sẽ cập nhật:

- `Cafe.OperationalProfileUpdatedAt` = `DateTime.UtcNow`
- `Cafe.UpdatedAt` = `DateTime.UtcNow`

Các thay đổi status (`ACTIVE` / `DATA_BLANK` / `INACTIVE`) — kể cả việc từ chối — đều ghi vào `PlayerActionHistory` với `ActionType = CafeOperationalStatusChanged (60)`, kèm metadata JSONB:

```json
{
  "cafeId": "guid",
  "previousStatus": "ACTIVE",
  "nextStatus": "DATA_BLANK",
  "isNoOp": false,
  "reason": null,
  "changedAt": "2026-08-17T15:00:00Z"
}
```

> Manager cố set `BANNED` (bị chặn) cũng được audit với `WriteBlockedAttemptAuditAsync` để admin theo dõi hành vi bất thường.

---

## 12. Liên kết

### Cùng controller (`ManagerCafeProfileController`)

- [`GET /api/manager/cafes/me`](./manager-cafe-profile.md#get-) — lấy hồ sơ hiện tại (cùng response shape).
- [`POST /api/manager/cafes/me/activate`](./manager-cafe-profile.md#post-activate) — bước tiếp theo sau khi cấu hình xong.
- [`POST /api/manager/cafes/me/deactivate`](./manager-cafe-profile.md#post-deactivate) — chuyển `ACTIVE → DATA_BLANK` trước khi sửa profile.
- [`POST /api/manager/cafes/me/close`](./manager-cafe-profile.md#post-close) — đóng vĩnh viễn.
- [`POST /api/manager/cafes/me/reopen`](./manager-cafe-profile.md#post-reopen) — mở lại từ `INACTIVE`.
- [`PATCH /api/manager/cafes/me/operational-status`](./manager-cafe-profile.md#patch-operational-status) — set status hợp nhất (khuyến nghị dùng thay 4 endpoint trên).

### Endpoint lân cận

- [`PUT /api/cafes/{cafeId}/pos/tables`](./cafe-pos.md) — quản lý bàn vật lý (ảnh hưởng `numberOfTables`).
- [`POST /api/cafes/{cafeId}/inventory`](./cafe-pos.md) — quản lý kho game (ảnh hưởng `numberOfGamesOwned`).
- [`PUT /api/cafes/{id}/pricing-config`](./cafe.md#put-apicafesidpricing-config--task-13) — cập nhật nhanh biểu phí sau khi `BR-04` mở khóa.
- [`PUT /api/cafes/{id}/sepay-config`](./cafe.md#put-apicafesidsepay-config) — cấu hình SePay cho session payment (POS).
- [`PATCH /api/cafes/{id}/deposit-refund-policy`](./cafe.md#patch-apicafesiddeposit-refund-policy--task-12) — chính sách hoàn cọc BR-18.

### Tài liệu liên quan

- [cafe-partner.md](./cafe-partner.md) — Phase 1 (đơn đăng ký) + admin approve/reject.
- [manager.md](./manager.md) — danh sách endpoint manager.
- [cafe.md](./cafe.md) — public cafe detail và admin/manager cafe endpoints.
- [cafe-pos.md](./cafe-pos.md) — POS endpoints (bàn, kho, session, check-in).
- [`BoardVerse.Core/Constants/CafePartnerActivationRules.cs`](../BoardVerse.Core/Constants/CafePartnerActivationRules.cs) — constants `MinPublicTables`, `MinGamesOwned`, `MinSpaceImages`.
- [`BoardVerse.Core/DTOs/CafePartner/UpdateOperationalProfileRequestDto.cs`](../BoardVerse.Core/DTOs/CafePartner/UpdateOperationalProfileRequestDto.cs) — DTO request.
- [`BoardVerse.Core/DTOs/CafePartner/ManagerCafeProfileResponseDto.cs`](../BoardVerse.Core/DTOs/CafePartner/ManagerCafeProfileResponseDto.cs) — DTO response.
- [`BoardVerse.Services/Services/CafePartnerApplicationService.cs`](../BoardVerse.Services/Services/CafePartnerApplicationService.cs) — `UpdateOperationalProfileAsync` + `ValidatePhase2Request` + `EnsureOperationalStateAllowsEdit`.

### Test

- `BoardVerse.Tests/Integration/CafePartnerAndManagerIntegrationTests.cs::ManagerOperationalProfile_Returns200Or400Or404` — happy path + validation.
- `BoardVerse.Tests/Integration/RemainingControllersIntegrationTests.cs` — các test cho Manager profile.
- `BoardVerse.Tests/Integration/ExceptionFlowIntegrationTests.cs` — verify gọi đúng endpoint.
