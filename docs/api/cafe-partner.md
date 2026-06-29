# Cafe Partner Applications (Self-onboarding)

**Controllers:** `CafePartnerApplicationController.cs`, `AdminCafePartnerApplicationController.cs`, `ManagerCafeProfileController.cs`

## Luồng nghiệp vụ

**Mô hình dữ liệu:** Phase 1 (đăng ký) → `CafePartnerApplication`. Sau admin duyệt, Phase 2+ (hồ sơ vận hành, bàn, kích hoạt) → `Cafe` (+ `CafeTable`).

```
[Landing Page: Gửi Form] → PENDING_APPROVAL
  → [Admin: Approve] → Tạo Manager + Cafe (DATA_BLANK) + Email credentials
  → [Web POS: Cập nhật hồ sơ vận hành]
  → [Manager: Activate] → ACTIVE (hiển thị Mobile App)

PENDING_APPROVAL → [Admin: Reject] → REJECTED (+ email lý do)
ACTIVE → [Manager: Deactivate] → DATA_BLANK (tạm dừng, có thể kích hoạt lại)
ACTIVE/DATA_BLANK → [Manager: Close] → INACTIVE (ngừng kinh doanh vĩnh viễn)
Any → [Admin: BANNED] → BANNED (vi phạm chính sách)
```

---

## Mô hình entity (Cafe · Application · CafeTable)

```
CafePartnerApplication          Cafe (1:1 qua CreatedCafeId)
├── Phase 1 only                ├── Phase 2+ runtime
│   cafeName, address           │   name, address, phoneNumber
│   lat/lng, phoneNumber        │   workingHours (Weekday*/Weekend*)
│   email, license              │   numberOfTables, tableLayoutJson
│   status, audit               │   space images, games, billing…
│                               │   partnerOperationalStatus
│                               └── Tables[] → CafeTable (1:N)
│                                       name, sortOrder, status
│                                       (POS / đặt bàn / InUse…)
```

| Entity | Vai trò | Ghi chú |
|--------|---------|---------|
| **CafePartnerApplication** | Đơn đăng ký + audit | Snapshot Phase 1; sau APPROVED gần như bất biến |
| **Cafe** | Quán thật sau duyệt | Nguồn sự thật cho vận hành, giờ mở cửa, số bàn |
| **CafeTable** | Từng bàn chơi | Tạo/cập nhật khi PUT operational-profile hoặc activate |

**Liên kết:** `Application.CreatedCafeId` → `Cafe.Id` (1:1). `CafeTable.CafeId` → `Cafe.Id` (N:1, cascade delete).

**Hai lớp “bàn” trên Cafe:**

| Field | Mục đích |
|-------|----------|
| `NumberOfTables` + `TableLayoutJson` | Cấu hình hồ sơ (tên bàn dạng JSON) |
| `CafeTables` (bảng riêng) | Bàn runtime — trạng thái `Available` / `InUse` / `Reserved` |

PUT operational-profile đồng bộ JSON → `CafeTables` qua `SyncCafeTablesAsync`. `numberOfPrivateRooms` chỉ lưu trên `Cafe`, **không** tạo `CafeTable`.

---

## Public endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/api/cafe-partner-applications` | POST | Gửi đơn đăng ký (Giai đoạn 1) |
| `/api/cafe-partner-applications/{id}` | GET | Tra cứu trạng thái đơn |

## Admin endpoints (Bearer JWT, role `Admin`)

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/api/admin/cafe-partner-applications` | GET | Danh sách + lọc |
| `/api/admin/cafe-partner-applications/{id}` | GET | Chi tiết |
| `/api/admin/cafe-partner-applications/{id}/approve` | POST | Duyệt đơn — tạo Manager + Cafe |
| `/api/admin/cafe-partner-applications/{id}/reject` | POST | Từ chối đơn (bắt buộc `reason`) |

## Manager endpoints (Bearer JWT, role `Manager`)

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/api/manager/cafes/me` | GET | Hồ sơ quán đối tác (`ManagerCafeProfileResponseDto`) |
| `/api/manager/cafes/me/operational-profile` | PUT | Cập nhật Giai đoạn 2 (giờ mở cửa + hạ tầng + catalog) |
| `/api/manager/cafes/me/activate` | POST | Kích hoạt quán (DATA_BLANK → ACTIVE) |
| `/api/manager/cafes/me/deactivate` | POST | Tạm dừng (ACTIVE → DATA_BLANK) |
| `/api/manager/cafes/me/close` | POST | Ngừng kinh doanh vĩnh viễn (→ INACTIVE) |

## Admin — trạng thái quán

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/api/v1/admin/cafes/{cafeId}/operational-status` | PUT | Đặt `DATA_BLANK`, `ACTIVE`, `INACTIVE`, `BANNED` (`reason` bắt buộc khi `BANNED`) |

---

## Trạng thái

### Đơn đăng ký (`applicationStatus`)

| Giá trị API | Ý nghĩa |
|------------|---------|
| `PENDING_APPROVAL` | Chờ Admin duyệt |
| `APPROVED` | Đã duyệt — đã tạo tài khoản |
| `REJECTED` | Bị từ chối (terminal) |

### Quán đối tác (`operationalStatus` — sau khi APPROVED)

| Giá trị API | Ý nghĩa |
|------------|---------|
| `DATA_BLANK` | Đã cấp tài khoản, chưa kích hoạt / manager tạm dừng |
| `ACTIVE` | Hiển thị trên Player Mobile App |
| `INACTIVE` | Quán ngừng kinh doanh — không còn hoạt động (manager đóng cửa vĩnh viễn hoặc admin đặt) |
| `BANNED` | Admin cấm hoạt động do vi phạm chính sách |

Response partner profile (`ManagerCafeProfileResponseDto`) có thêm `operationalStatusReason` khi `INACTIVE` hoặc `BANNED`.

---

## Response shapes

### `CafePartnerApplicationResponseDto` (Phase 1)

Dùng cho: POST/GET đơn công khai, admin list/detail, reject, trường `application` trong approve.

Chỉ chứa thông tin đăng ký + audit. **Không** có `workingHours`, số bàn, ảnh không gian, v.v.

Sau khi duyệt, có thêm `createdCafeId` và `operationalStatus` (tóm tắt từ `Cafe`).

### `ManagerCafeProfileResponseDto` (Phase 2+)

Dùng cho: mọi endpoint `/api/manager/cafes/me/*`.

Nguồn dữ liệu là `Cafe`: `name`, `workingHours`, hạ tầng, `canActivate`, `activationBlockers`, v.v.

---

## POST /api/cafe-partner-applications (Giai đoạn 1)

**Body mẫu:**
```json
{
  "cafeName": "Board Game Hub Saigon",
  "address": "123 Nguyen Hue, Phuong Ben Nghe, Quan 1, TP.HCM",
  "latitude": 10.776889,
  "longitude": 106.700806,
  "phoneNumber": "0901234567",
  "representativeEmail": "cafe.owner@example.com",
  "businessLicense": "0312345678",
  "businessLicenseImageUrl": "https://cdn.example.com/license.pdf"
}
```

| Field | Bắt buộc | Ràng buộc |
|-------|----------|-----------|
| `cafeName` | Có | 5–100 ký tự |
| `address` | Có | Địa chỉ đầy đủ |
| `latitude` | Có | -90 … 90 (WGS84, từ bản đồ khi đăng ký) |
| `longitude` | Có | -180 … 180 |
| `phoneNumber` | Có | SĐT VN 10–11 số, đầu 03/05/07/08/09 |
| `representativeEmail` | Có | Email RFC — dùng làm username Manager |
| `businessLicense` | Có | Alphanumeric, khóa pháp lý duy nhất |
| `businessLicenseImageUrl` | Có | JPEG/PNG/PDF |

**Lỗi:**
- `409` — Trùng MST hoặc địa chỉ 100%: *"Mã số thuế hoặc Địa chỉ này đã được đăng ký trên hệ thống. Vui lòng kiểm tra lại."*
- `409` — Email đã có đơn `PENDING_APPROVAL` hoặc thuộc Admin/Manager/CafeStaff

---

## PUT /api/manager/cafes/me/operational-profile (Giai đoạn 2)

Chỉ khi quán ở `DATA_BLANK`. Không chỉnh sửa khi đang `ACTIVE` (phải deactivate trước).

```json
{
  "workingHours": {
    "weekdayStart": "09:00",
    "weekdayEnd": "22:00",
    "weekendStart": "10:00",
    "weekendEnd": "23:00"
  },
  "numberOfTables": 8,
  "numberOfPrivateRooms": 2,
  "spaceImageUrls": [
    "https://cdn.example.com/facade.jpg",
    "https://cdn.example.com/play-area.jpg",
    "https://cdn.example.com/lighting.jpg"
  ],
  "numberOfGamesOwned": 45,
  "popularGamesList": "Catan, Ticket to Ride, Azul, Wingspan",
  "hasGameMaster": true,
  "billingModel": 0
}
```

### `tableNames` (tùy chọn)

Không gửi `tableNames` (hoặc gửi `null` / `[]`) — backend tự quản lý sơ đồ bàn theo `numberOfTables`:

| Tình huống | Hành vi backend |
|------------|-----------------|
| Lần đầu, `numberOfTables: 8` | Sinh `["Bàn 1", …, "Bàn 8"]` |
| Tăng lên `numberOfTables: 10` (không gửi `tableNames`) | Cập nhật thành `["Bàn 1", …, "Bàn 10"]` |
| Giảm xuống `numberOfTables: 5` (không gửi `tableNames`) | Cập nhật thành `["Bàn 1", …, "Bàn 5"]` |
| Gửi `tableNames` tùy chỉnh | Dùng tên tùy chỉnh; thiếu thì thêm `"Bàn N"`, thừa thì cắt theo `numberOfTables` |
| Đã đặt tên tùy chỉnh, chỉ đổi `numberOfTables` | Giữ tên cũ (theo thứ tự), thêm/bớt slot cho khớp số bàn |

Response luôn trả `tableNames` đầy đủ sau khi lưu.

| `billingModel` | API string |
|----------------|------------|
| `0` | `BY_HOUR` |
| `1` | `PER_DRINK` |

| `workingHours` | Có | Khung T2–T6 và T7–CN (`HH:mm`) — lưu trên `Cafe` |

---

## POST /api/manager/cafes/me/activate

Kiểm tra ràng buộc tối thiểu (mục 4.2 spec):

- ≥ 5 bàn chơi công cộng
- ≥ 20 bộ game vật lý
- ≥ 3 ảnh không gian hợp lệ
- Giờ mở cửa (T2–T6, T7–CN) đã cấu hình qua Phase 2
- Sơ đồ bàn: backend đảm bảo đủ tên cho mọi bàn (tự sinh hoặc đồng bộ khi `numberOfTables` thay đổi)

Response có `canActivate`, `activationBlockers`, `isTableLayoutConfigured`.

---

## POST /api/admin/cafe-partner-applications/{id}/approve

Tạo tài khoản `Manager` (username = `representativeEmail`), Cafe (`IsActive=false`, `DATA_BLANK`), gửi email credentials.

Response (`OnboardPartnerResultDto`):
```json
{
  "application": { "...": "..." },
  "managerUserId": "uuid",
  "managerEmail": "cafe.owner@example.com",
  "cafeId": "uuid",
  "temporaryPassword": "Abc12!@#xyz"
}
```

---

## POST /api/admin/cafe-partner-applications/{id}/reject

```json
{ "reason": "Giấy phép kinh doanh không hợp lệ." }
```

---

## GET /api/admin/cafe-partner-applications

**Query:** `search`, `status` (enum: `PendingApproval`, `Rejected`, `Approved`), `page`, `pageSize`

---

## Hiển thị trên Mobile App

Chỉ quán có `IsActive=true` **và** `operationalStatus=ACTIVE` mới hiển thị cho người chơi.

---

## Database

Schema quản lý qua **EF Core** (`BoardVerseDbContext`, entity + configuration). Database Neon phải được tạo/cập nhật schema trước khi chạy API.

---

## Email (Brevo)

Cấu hình `Brevo` trong `appsettings.json` — xem [auth.md](./auth.md).
