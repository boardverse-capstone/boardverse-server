# CafeController

**Base route:** `/api/cafes`  
**Controller:** `CafeController.cs`

| Endpoint | Method | Role |
|----------|--------|------|
| `/` | GET | Player (lấy tất cả quán ACTIVE) |
| `/nearby` | GET | Public (Player discovery — GPS query params) |
| `/nearby/me` | GET | Player (dùng vị trí đã lưu trên profile) |
| `/{id}` | GET | Public |
| `/{id}` | PUT | Manager (chủ quán) |
| `/{cafeId}/staff` | POST | Manager (chủ quán) |
| `/{cafeId}/staff/promote` | POST | Manager (chủ quán) |
| `/{cafeId}/staff` | GET | Manager (chủ quán) |
| `/{cafeId}/staff/{staffId}` | DELETE | Manager (chủ quán) |
| `/{id}/sepay-config` | PUT | Manager (chủ quán) |

> Lấy `cafeId` qua [GET /api/manager/my-cafes](./manager.md) thay vì hardcode.

---

## So sánh các GET cafe endpoint

### Tổng quan

| Endpoint | DTO trả về | Auth | Dùng cho |
|---------|-----------|------|----------|
| `GET /api/cafes/{id}` | `CafeDetailDto` | Public (AllowAnonymous) | Player xem chi tiết 1 quán trước khi đặt chỗ |
| `GET /api/cafes` | `PaginatedResponse<NearbyCafeDto>` | Player | List view (không GPS) |
| `GET /api/cafes/nearby` | `NearbyCafeSearchResultDto` (chứa `NearbyCafeDto[]`) | Public | Player discovery GPS |
| `GET /api/cafes/nearby/me` | `NearbyCafeSearchResultDto` | Player (đã đăng nhập) | Player discovery dùng vị trí đã lưu |
| `GET /api/cafes/search` | `PaginatedResponse<NearbyCafeDto>` | Public | Player search theo tên |
| `GET /api/manager/my-cafes` | `ManagerCafeDto[]` | Manager | Manager dashboard |
| `GET /api/staff/my-cafes` | `ManagerCafeDto[]` | CafeStaff | Staff dashboard |
| `GET /api/admin/cafes` | `AdminCafeListItemDto[]` | Admin | Admin list |
| `GET /api/admin/cafes/{id}` | `AdminCafeDetailDto` | Admin | Admin xem chi tiết |

### Chi tiết field theo từng endpoint

#### `GET /api/cafes/{id}` → `CafeDetailDto` (player/public)

Kế thừa `CafeDto` + thêm: `operationalStatus`, `operationalStatusReason` (ẩn cho player), `isCurrentlyOpen`, `refundPolicy`, `refundTiers`, `depositRatePerPerson`, `minDeposit`, `availableSeats`, `heldSeats`, `inUseSeats`, `availableSeatsByTimeSlot`, `cafeConfig`, `scheduleOverrides`, `numberOfTables`, `numberOfPrivateRooms`, `numberOfGamesOwned`, `hasGameMaster`, `distanceKm`.

**Không trả:** `ManagerId`, `SePayMerchantId/ApiKey/SecretKey`, `SePayBankCode`, `SePayAccountNumber`, `SePayReturnUrl`, `WeekdayOpen/Close`, `WeekendOpen/Close`, `StaffCount`, `UpcomingBookingsCount`, `ActiveLobbiesToday`, `PendingCafeApprovalLobbiesCount`, `HeldDepositTotal`, `DefaultHoldDurationMinutes`, `UpdatedAt`, `OperationalProfileUpdatedAt`, `OperationalStatusReason` (lý do nội bộ).

#### `GET /api/cafes`, `/nearby`, `/nearby/me`, `/search` → `NearbyCafeDto` (player)

Kế thừa `CafeDto` + thêm: `distanceMeters`, `availableGameCount`, `totalGameBoxCount`, `availableTableCount`, `totalTableCount`, `selectedGameAvailabilityStatus`, `estimatedWaitMinutes`.

**Không trả:** Mọi refund policy, deposit config, schedule overrides, operational status, staff/lobby/revenue metrics, distance chỉ có nếu truyền lat/lng (không tính trong list view).

`NearbyCafeSearchResultDto` bọc thêm: `emptyResultMessage`, `alternativeSuggestions` (chỉ khi truyền `gameTemplateId`).

#### `GET /api/manager/my-cafes` & `/api/staff/my-cafes` → `ManagerCafeDto` (manager/staff)

Kế thừa `CafeDetailDto` + thêm **manager-only**: `ManagerId`, `SePayMerchantId`, `SePayBankCode`, `SePayAccountNumber`, `SePayReturnUrl`, `DefaultHoldDurationMinutes`, `StaffCount`, `UpcomingBookingsCount`, `ActiveLobbiesToday`, `PendingCafeApprovalLobbiesCount`, `HeldDepositTotal`, `WeekdayOpen/Close`, `WeekendOpen/Close`, `UpdatedAt`, `OperationalProfileUpdatedAt`.

**Quan trọng:** Manager/Staff thấy `operationalStatusReason` (lý do nội bộ) + `HeldDepositTotal` (revenue snapshot).

**Staff field filter:** Staff thấy `ManagerId = Guid.Empty` và ẩn SePay raw (`SePayMerchantId`, `SePayBankCode`, `SePayAccountNumber`, `SePayReturnUrl`).

#### `GET /api/admin/cafes` → `AdminCafeListItemDto` (admin)

Gọn cho list view: `Id`, `Name`, `Address`, `PhoneNumber`, `TotalSeats`, `IsActive`, `DepositPercentage`, `HasSePayConfigured`, `ManagerId`, `ManagerName`, `NumberOfTables`, `NumberOfGamesOwned`, `StaffCount`, `CreatedAt`, `Status`.

> Admin có thể click call trực tiếp từ list nhờ `PhoneNumber` (thêm 2026-08-15).

#### `GET /api/admin/cafes/{id}` → `AdminCafeDetailDto` (admin)

DTO riêng cho admin — **không kế thừa `CafeDetailDto`**: `Id`, `Name`, `Address`, `Latitude`, `Longitude`, `PhoneNumber`, `Description`, `ManagerId`, `ManagerName`, `ManagerEmail`, `PartnerOperationalStatus`, `PartnerOperationalStatusReason`, `PartnerOperationalStatusChangedAt`, `WeekdayOpen/Close`, `WeekendOpen/Close`, `NumberOfTables`, `NumberOfPrivateRooms`, `TotalSeats`, `NumberOfGamesOwned`, `PopularGamesList`, `HasGameMaster`, `BillingModel`, `BasePrice`, `TieredBlockRate`, `TieredBlockMinutes`, `IsPricingLocked`, `DepositPercentage`, `DefaultHoldDurationMinutes`, `RefundPolicy`, `HasSePayConfigured`, `ScheduleOverrides`, `CreatedAt`, `UpdatedAt`, `IsActive`.

> Admin xem/tạo override giờ mở cửa cho ngày lễ qua `ScheduleOverrides` (thêm 2026-08-15).

**Admin-specific:** `ManagerEmail`, `PopularGamesList`, `PartnerOperationalStatusChangedAt`, `TieredBlockRate`, `IsPricingLocked`, `DefaultHoldDurationMinutes`, `ScheduleOverrides`.

> Security note: Admin KHÔNG thấy `SePayApiKey`/`SePaySecretKey` (secret) — chỉ `HasSePayConfigured` boolean.

### Ma trận bảo mật — field nào endpoint nào trả

| Field | `/{id}` (player) | `/nearby` etc. (player) | `my-cafes` (manager) | `my-cafes` (staff) | `/admin/cafes/{id}` |
|-------|:-:|:-:|:-:|:-:|:-:|
| `Id`, `Name`, `Address`, `PhoneNumber`, `Description`, `CreatedAt` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `Latitude`, `Longitude` | ✅ | ❌ | ✅ | ✅ | ✅ |
| `TotalSeats`, `BillingModel`, `BasePrice`, `TieredBlockRate`, `TieredBlockMinutes`, `DepositPercentage`, `IsPricingLocked` | ✅ | ❌ | ✅ | ✅ | ✅ |
| `HasSePayConfigured` (bool derived) | ✅ | ❌ | ✅ | ✅ | ✅ |
| `OperationalStatus` (string) | ✅ | ❌ | ✅ | ✅ | ✅ |
| **`OperationalStatusReason`** (lý do nội bộ) | ❌ **ẩn** | ❌ | ✅ | ✅ | ✅ |
| `IsCurrentlyOpen` | ✅ | ❌ | ✅ | ✅ | ❌ |
| `RefundPolicy`, `RefundTiers` | ✅ | ❌ | ✅ | ✅ | `RefundPolicy` only |
| `DepositRatePerPerson`, `MinDeposit`, `CafeConfig` (BR defaults) | ✅ | ❌ | ✅ | ✅ | ❌ |
| `AvailableSeats`, `HeldSeats`, `InUseSeats`, `AvailableSeatsByTimeSlot` | ✅ | ❌ | ✅ | ✅ | ❌ |
| `ScheduleOverrides` | ✅ | ❌ | ✅ | ✅ | ✅ |
| `NumberOfTables`, `NumberOfPrivateRooms`, `NumberOfGamesOwned`, `HasGameMaster` | ✅ | ❌ | ✅ | ✅ | ✅ |
| `PhoneNumber` (admin cần liên hệ cafe từ list/detail) | ❌ | ❌ | ❌ | ❌ | ✅ |
| `DistanceKm` | ✅ (nếu truyền lat/lng) | ❌ | ❌ | ❌ | ❌ |
| `DistanceMeters` | ❌ | ✅ | ❌ | ❌ | ❌ |
| `AvailableGameCount`, `TotalGameBoxCount`, `AvailableTableCount`, `TotalTableCount` | ❌ | ✅ | ❌ | ❌ | ❌ |
| `SelectedGameAvailabilityStatus`, `EstimatedWaitMinutes` | ❌ | ✅ | ❌ | ❌ | ❌ |
| `ManagerId` | ❌ | ❌ | ✅ | ✅ (set Empty) | ✅ |
| `ManagerName`, `ManagerEmail` | ❌ | ❌ | ❌ | ❌ | ✅ |
| `SePayMerchantId`, `SePayBankCode`, `SePayAccountNumber`, `SePayReturnUrl` | ❌ | ❌ | ✅ | ❌ (staff ẩn) | ❌ |
| `DefaultHoldDurationMinutes` | ❌ | ❌ | ✅ | ✅ | ✅ |
| `StaffCount`, `UpcomingBookingsCount`, `ActiveLobbiesToday`, `PendingCafeApprovalLobbiesCount`, `HeldDepositTotal` | ❌ | ❌ | ✅ | ✅ | ❌ |
| `WeekdayOpen/Close`, `WeekendOpen/Close` | ❌ | ❌ | ✅ | ✅ | ✅ |
| `UpdatedAt`, `OperationalProfileUpdatedAt` | ❌ | ❌ | ✅ | ✅ | `UpdatedAt` only |
| `PopularGamesList`, `PartnerOperationalStatusChangedAt` | ❌ | ❌ | ❌ | ❌ | ✅ |

### Security filter cho player endpoint `GET /api/cafes/{id}` (public)

> **Cập nhật 2026-08-15:** Field `operationalStatusReason` (lý do nội bộ khi quán bị Inactive/Banned) **luôn null** cho player endpoint. Manager/Admin/Staff thấy field này qua endpoint riêng.
>
> Implementation: `ICafeService.GetCafeDetailAsync(includeSensitiveInfo = false)` — `OperationalStatusReason = includeSensitiveInfo ? cafe.PartnerOperationalStatusReason : null`.

---

## GET /api/cafes

Lấy danh sách tất cả quán cafe đang hoạt động cho player (`IsActive=true` AND `PartnerOperationalStatus=Active`).
**Yêu cầu đăng nhập** (`[Authorize]`). Không filter theo vị trí, không yêu cầu `gameTemplateId`.
Sắp xếp theo `Name` A→Z. Trả về shape `NearbyCafeDto` (giống `/nearby`) để player thấy được
`AvailableGameCount`, `TotalGameBoxCount`, `AvailableTableCount`, `TotalTableCount`.
`distanceMeters` luôn là `0` vì không tính khoảng cách.

**Query:**

| Param | Mô tả | Mặc định |
|-------|--------|----------|
| `pageNumber` | Trang | `1` |
| `pageSize` | Kích thước trang | `20` |

**Response 200:** `PaginatedResponse<NearbyCafeDto>` — `data` chứa danh sách quán, `meta` chứa `currentPage`/`pageSize`/`totalItems`/`totalPages`.

**Lỗi:** `401` thiếu token; `500` lỗi hệ thống.

---

## GET /api/cafes/nearby

Tìm quán đối tác **ACTIVE** gần vị trí player (PostGIS `geography` + GiST index). **Không cần token.**  
`gameTemplateId` là **tùy chọn** — khi truyền sẽ lọc theo tựa game (luồng **Khám phá game**: chỉ quán có ít nhất một hộp game `Available` hoặc `InUse` thuộc tựa đó, AC 2.1, 3.1). Bỏ trống → trả tất cả quán ACTIVE trong bán kính.

**Query:**

| Param | Mô tả | Mặc định |
|-------|--------|----------|
| `latitude` | Vĩ độ player (WGS84) | bắt buộc |
| `longitude` | Kinh độ player | bắt buộc |
| `gameTemplateId` | Tựa game player đã chọn (tùy chọn) | — |
| `radiusKm` | Bán kính tìm kiếm (km) | `15` (0.1–50) |
| `pageNumber` | Trang | `1` |
| `pageSize` | Kích thước trang | `20` |

**Response 200:** wrapper `NearbyCafeSearchResultDto`:

| Field | Mô tả |
|-------|--------|
| `cafes` | Phân trang `NearbyCafeDto` (shape cũ nằm trong `cafes.data` + `cafes.meta`) |
| `emptyResultMessage` | Thông điệp UI khi **không có quán nào** (AC 5.1); `null` khi có kết quả |
| `alternativeSuggestions` | Game cùng thể loại còn hàng `Available` gần player (AC 5.2); `[]` khi không truyền `gameTemplateId` hoặc có kết quả |

Mỗi phần tử `alternativeSuggestions`:

| Field | Mô tả |
|-------|--------|
| `gameTemplateId`, `gameName`, `thumbnailUrl` | Tựa game thay thế |
| `minPlayers`, `maxPlayers` | Giới hạn người chơi |
| `nearbyCafeCount` | Số quán trong bán kính có hộp `Available` |
| `nearestCafeDistanceMeters` | Khoảng cách quán gần nhất có hàng |
| `availableBoxCount` | Tổng hộp `Available` trong bán kính |
| `sharedCategories` | Thể loại trùng với game gốc |

Mỗi quán trong `cafes.data` (`NearbyCafeDto`):
| `distanceMeters` | Khoảng cách địa lý từ GPS player (PostGIS, sắp xếp tăng dần) |
| `availableGameCount` | Số hộp game `Available` (theo `gameTemplateId` nếu có, ngược lại tổng kho) |
| `availableTableCount` | Số bàn vật lý trạng thái `Available` (AC 2.3) |
| `totalTableCount` | Tổng số bàn active của quán (AC 2.3) |
| `totalGameBoxCount` | Tổng hộp game playable (`Available` + `InUse`) của tựa đã chọn |
| `selectedGameAvailabilityStatus` | `GameAvailable` hoặc `WaitingForGame` (AC 3.2 — UI: **Chờ game trống**) |
| `estimatedWaitMinutes` | Phút chờ ước tính khi `WaitingForGame` (AC 3.3); `null` khi còn hộp trống |

UI card ví dụ: **Còn trống {availableTableCount}/{totalTableCount} bàn** · **Chờ game ~{estimatedWaitMinutes} phút** khi `selectedGameAvailabilityStatus = WaitingForGame`.

**Response 200 — không có quán (AC 5.1, 5.2):**

```json
{
  "statusCode": 200,
  "message": "Nearby cafes retrieved successfully",
  "data": {
    "cafes": {
      "data": [],
      "meta": { "currentPage": 1, "pageSize": 20, "totalItems": 0, "totalPages": 0, "hasPrevious": false, "hasNext": false }
    },
    "emptyResultMessage": "Không tìm thấy địa điểm phù hợp có sẵn tựa game này xung quanh bạn.",
    "alternativeSuggestions": [
      {
        "gameTemplateId": "55555555-5555-5555-5555-555555555555",
        "gameName": "Werewolf Ultimate",
        "minPlayers": 5,
        "maxPlayers": 20,
        "nearbyCafeCount": 1,
        "nearestCafeDistanceMeters": 120.5,
        "availableBoxCount": 2,
        "sharedCategories": [{ "id": "c1111111-1111-1111-1111-111111111111", "name": "Ẩn vai", "slug": "an-vai" }]
      }
    ]
  }
}
```

Logic gợi ý: lấy `category_id` của game gốc → tìm game **khác** cùng ít nhất một thể loại → có `CafeInventoryBoxes` trạng thái **`Available`** tại quán ACTIVE trong bán kính → sắp xếp theo quán gần nhất.

**Công thức chờ (AC 3.3):** `GameTemplates.PlayTime` − thời gian đã chơi (từ `ActiveSessions.StartedAt` khi POS giao game). Lấy **min** trên các hộp `InUse` của tựa tại quán. Quán vẫn hiển thị khi tất cả hộp đang `InUse` (AC 3.1).

POS tạo/kết thúc session qua [CafePosController](./cafe-pos.md).

**Lỗi:** `400` tọa độ hoặc bán kính không hợp lệ.

---

## GET /api/cafes/nearby/me

Cùng logic và response như `GET /nearby`, nhưng dùng **tọa độ đã lưu** trên profile (`LastKnownLatitude` / `LastKnownLongitude`) thay vì query `latitude`/`longitude`. **Yêu cầu đăng nhập.** `gameTemplateId` tùy chọn — bỏ trống trả tất cả quán ACTIVE trong bán kính.

**Luồng gợi ý (mobile):**

```
1. Lấy GPS thiết bị
2. PUT /api/userprofile/me/location   → lưu server
3. GET /api/cafes/nearby/me?gameTemplateId=...   → không cần gửi lại lat/lng
```

Hoặc gọi thẳng `GET /nearby?latitude=...&longitude=...` (public, không cần token).

**Query:**

| Param | Mô tả | Mặc định |
|-------|--------|----------|
| `gameTemplateId` | Tựa game đã chọn (tùy chọn) | — |
| `radiusKm` | Bán kính (km) | `15` |
| `pageNumber` | Trang | `1` |
| `pageSize` | Kích thước trang | `20` |

**Lỗi:** `400` chưa lưu vị trí (`PUT me/location` trước); `401` thiếu token.

---

## GET /api/cafes/{id}

Xem thông tin **chi tiết** quán cafe — **không cần token**. Bao gồm pricing, refund policy, seat availability, schedule overrides.

**Query (optional):**

| Param | Mô tả |
|-------|--------|
| `latitude` | Vĩ độ player (để tính khoảng cách) |
| `longitude` | Kinh độ player (để tính khoảng cách) |

**Response 200:** `CafeDetailDto`

```json
{
  "statusCode": 200,
  "message": "Lấy thông tin quán thành công.",
  "data": {
    "id": "uuid",
    "name": "Boss cafe",
    "address": "22 Lê Tấn Bê, An Lạc, Hồ Chí Minh",
    "latitude": 10.7249011,
    "longitude": 106.6046094,
    "phoneNumber": "0974993949",
    "description": null,
    "createdAt": "2026-08-01T05:23:53Z",
    "totalSeats": 30,
    "billingModel": "TIME_BASED",
    "basePrice": 80000,
    "tieredBlockRate": 25000,
    "tieredBlockMinutes": 15,
    "depositPercentage": 0.5,
    "isPricingLocked": false,
    "hasSePayConfigured": false,

    "operationalStatus": "ACTIVE",
    "operationalStatusReason": null,
    "isCurrentlyOpen": true,

    "refundPolicy": "Partial",
    "refundTiers": [
      { "minHoursBeforeScheduled": 24, "refundPercent": 50 },
      { "minHoursBeforeScheduled": 12, "refundPercent": 25 },
      { "minHoursBeforeScheduled": 0, "refundPercent": 0 }
    ],

    "depositRatePerPerson": 10,

    "cafeConfig": {
      "capacity": 30,
      "maxLobbiesPerUserPerDay": 1,
      "maxPlayersPerLobbySameDay": 30,
      "maxPlayersPerLobby1Day": 20,
      "maxPlayersPerLobby2Days": 15,
      "maxPlayersPerLobby3To4Days": 10,
      "maxPlayersPerLobby5To7Days": 6,
      "requireApprovalForDistant": true,
      "distantThresholdDays": 2,
      "approvalTimeoutHours": 24,
      "maxTotalDepositPerUser": 500000,
      "recruitmentDeadlineBufferMinutes": 120,
      "cancellationGraceMinutes": 15
    },

    "availableSeats": 25,
    "heldSeats": 3,
    "inUseSeats": 2,
    "availableSeatsByTimeSlot": {
      "Morning": 30,
      "Afternoon": 28,
      "Evening": 25,
      "LateNight": 30
    },

    "scheduleOverrides": [],

    "numberOfTables": 10,
    "numberOfPrivateRooms": 0,
    "numberOfGamesOwned": 25,
    "hasGameMaster": false,
    "distanceKm": 1.5
  }
}
```

**CafeDetailDto fields:**

| Field | Mô tả |
|-------|--------|
| **Basic Info** | `id`, `name`, `address`, `latitude`, `longitude`, `phoneNumber`, `description`, `createdAt` |
| **Pricing** | `totalSeats`, `billingModel`, `basePrice`, `tieredBlockRate`, `tieredBlockMinutes`, `depositPercentage`, `isPricingLocked`, `hasSePayConfigured` |
| **Operational** | `operationalStatus` (DataBlank/Active/Inactive/Banned), `operationalStatusReason`, `isCurrentlyOpen` |
| **Refund Policy (BR-18)** | `refundPolicy` (Full/Partial/None), `refundTiers` (khi Partial) |
| **Deposit Config** | `depositRatePerPerson` (BVC/người), `cafeConfig` (hạn mức riêng của cafe) |
| **Seat Availability** | `availableSeats`, `heldSeats`, `inUseSeats`, `availableSeatsByTimeSlot` |
| **Cafe Config (BR-NEW-12)** | `cafeConfig` (maxPlayers, minDeposit, approval settings) |
| **Schedule** | `scheduleOverrides` (ngày lễ, giờ mở đặc biệt) |
| **Additional** | `numberOfTables`, `numberOfPrivateRooms`, `numberOfGamesOwned`, `hasGameMaster`, `distanceKm` (nếu truyền lat/lng) |

**Lỗi:** `404` cafe không tồn tại hoặc inactive.

---

## PUT /api/cafes/{id}

Cập nhật thông tin quán — chỉ **chủ quán**.

**Body (tất cả optional):**
```json
{
  "name": "BoardVerse Demo Cafe",
  "address": "456 New Street, HCMC",
  "latitude": 10.776889,
  "longitude": 106.700806,
  "phoneNumber": "0909999999",
  "description": "Updated description"
}
```

---

## Hai luồng thêm nhân viên

### Luồng A — Tài khoản mới
```
POST /api/cafes/{cafeId}/staff
{ "email", "username", "password"? }
```

### Luồng B — User đã đăng ký
```
POST /api/cafes/{cafeId}/staff/promote
{ "email", "username"?, "password"? }
```

### Luồng C — CafeStaff đã có, gắn thêm quán
```
POST /api/cafes/{cafeId}/staff
{ "email" }
```

| Tình huống | API |
|------------|-----|
| Email chưa có | `POST .../staff` (+ username bắt buộc) |
| Email là `Player` | `POST .../staff/promote` trước |
| Email là `CafeStaff` | `POST .../staff` (chỉ email) |
| Gọi `POST staff` khi vẫn là `Player` | `400` — message hướng dẫn gọi `/promote` |

---

## POST /api/cafes/{cafeId}/staff

```json
{
  "email": "staff@example.com",
  "username": "johndoe",
  "password": "Staff@1234"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `email` | Bắt buộc |
| `username` | Bắt buộc khi tạo mới; bỏ qua khi gắn CafeStaff đã có |
| `password` | Tuỳ chọn, 8–100 ký tự |

---

## POST /api/cafes/{cafeId}/staff/promote

```json
{
  "email": "player@example.com",
  "username": "johndoe",
  "password": "Staff@1234"
}
```

Nâng `Player` → `CafeStaff` và gắn quán.

---

## DELETE /api/cafes/{cafeId}/staff/{staffId}

Gỡ nhân viên khỏi quán — **xóa hàng** trong `CafeStaffs` (không còn cột `IsActive` trên staff).

Nếu staff **không còn quán nào** → role tự hạ về `Player`.

Danh sách staff (`GET .../staff`) chỉ gồm user có `isActive = true`.

---

## GET /api/cafes/{cafeId}/staff

**Query:** `pageNumber`, `pageSize` (thống nhất với inventory — không dùng `page`).

**Response:** `userId`, `email`, `username`, `joinedAt`.

---

## PUT /api/cafes/{id}/sepay-config

Cập nhật cấu hình SePay cho quán — dùng cho session payment (POS). Endpoint rút gọn so với `SePayAccountController.my-cafe`, manager không cần biết `accountId`, chỉ cần `cafeId`.

**Body** (`UpdateSePayConfigRequestDto`):
```json
{
  "merchantId": "...",
  "secretKey": "...",
  "bankCode": "VCB",
  "accountNumber": "...",
  "environment": "Test"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `merchantId` | Bắt buộc |
| `secretKey` | Bắt buộc |
| `bankCode` | Mã ngân hàng |
| `accountNumber` | Số tài khoản nhận tiền |
| `environment` | `Test` / `Production` |

**Response 200:** message "SePay config updated successfully".

**Lỗi:** `400` dữ liệu không hợp lệ; `403` không phải manager của cafe; `404` không tìm thấy cafe.

> **Liên quan:** [sepay-account.md](./sepay-account.md), [sepay-webhook.md](./sepay-webhook.md).

---

## PATCH /api/cafes/{id}/deposit-refund-policy — Task #12

Cập nhật chính sách hoàn cọc khi booking bị hủy (BR-18). Manager cấu hình 1 trong 3 policy: `Full` / `Partial` / `None`.

**Body** (`UpdateRefundPolicyRequestDto`):
```json
{
  "policy": "Partial",
  "partialTiers": [
    { "minHoursBeforeScheduled": 24, "refundPercent": 50 },
    { "minHoursBeforeScheduled": 12, "refundPercent": 25 },
    { "minHoursBeforeScheduled": 0, "refundPercent": 0 }
  ]
}
```

| Field | Ràng buộc |
|-------|-----------|
| `policy` | `Full` (0) / `Partial` (1) / `None` (2) — bắt buộc |
| `partialTiers` | Bắt buộc khi `policy=Partial`. 1-5 bậc, không trùng `minHoursBeforeScheduled`, `refundPercent` ∈ [0, 100] |

**Response 200:**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "data": {
    "cafeId": "uuid",
    "policy": "Partial",
    "partialTiers": [
      { "minHoursBeforeScheduled": 24, "refundPercent": 50 },
      { "minHoursBeforeScheduled": 12, "refundPercent": 25 },
      { "minHoursBeforeScheduled": 0, "refundPercent": 0 }
    ],
    "updatedAt": "2026-08-01T15:00:00Z"
  }
}
```

**Lỗi:** `400` tiers không hợp lệ; `403`; `404`.

> **Liên quan:** [payment.md](./payment.md) §Refund BR-18.

---

## PUT /api/cafes/{id}/pricing-config — Task #13

Cập nhật biểu phí của quán (BasePrice, BillingModel, TieredBlockRate, TieredBlockMinutes). **BR-04:** chỉ cho phép khi quán đóng cửa (`IsPricingLocked=false`). Khi update thành công → broadcast SignalR event `CafePricingChanged` cho member có booking trong tuần.

**Body** (`UpdatePricingConfigRequestDto`):
```json
{
  "billingModel": "TimeBased",
  "basePrice": 90000,
  "tieredBlockRate": 25000,
  "tieredBlockMinutes": 15
}
```

**Response 200:**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "data": {
    "cafeId": "uuid",
    "billingModel": "TimeBased",
    "basePrice": 90000,
    "tieredBlockRate": 25000,
    "tieredBlockMinutes": 15,
    "isPricingLocked": false,
    "operationalProfileUpdatedAt": "2026-08-01T15:00:00Z",
    "affectedBookingsCount": 12
  }
}
```

**Lỗi:** `400`; `403`; `404`; `409` quán đang hoạt động; `500`.
