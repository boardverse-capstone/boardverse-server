# Cafe Partner Applications (Self-onboarding)

**Controllers:** `CafePartnerApplicationController.cs`, `AdminCafePartnerApplicationController.cs`, `CafePartnerManagerController.cs`

## Luồng nghiệp vụ

```
[Landing Page: Gửi Form] → PENDING_APPROVAL
  → [Admin: Approve] → Tạo Manager + Cafe (DATA_BLANK) + Email credentials
  → [Web POS: Cập nhật hồ sơ vận hành]
  → [Manager: Activate] → ACTIVE (hiển thị Mobile App)

PENDING_APPROVAL → [Admin: Reject] → REJECTED (+ email lý do)
ACTIVE → [Manager: Deactivate] → DATA_BLANK (khi không còn booking đang chạy)
```

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
| `/api/cafe-partner/me` | GET | Hồ sơ đối tác của Manager |
| `/api/cafe-partner/me/operational-profile` | PUT | Cập nhật Giai đoạn 2 (hạ tầng + catalog) |
| `/api/cafe-partner/me/activate` | POST | Kích hoạt quán (DATA_BLANK → ACTIVE) |
| `/api/cafe-partner/me/deactivate` | POST | Tạm dừng (ACTIVE → DATA_BLANK) |

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
| `DATA_BLANK` | Đã cấp tài khoản, chưa kích hoạt / đang tạm dừng |
| `ACTIVE` | Hiển thị trên Player Mobile App |

---

## POST /api/cafe-partner-applications (Giai đoạn 1)

**Body mẫu:**
```json
{
  "cafeName": "Board Game Hub Saigon",
  "address": "123 Nguyen Hue, Phuong Ben Nghe, Quan 1, TP.HCM",
  "hotline": "0901234567",
  "representativeEmail": "cafe.owner@example.com",
  "workingHours": {
    "weekdayStart": "09:00",
    "weekdayEnd": "22:00",
    "weekendStart": "10:00",
    "weekendEnd": "23:00"
  },
  "businessLicense": "0312345678",
  "businessLicenseImageUrl": "https://cdn.example.com/license.pdf"
}
```

| Field | Bắt buộc | Ràng buộc |
|-------|----------|-----------|
| `cafeName` | Có | 5–100 ký tự |
| `address` | Có | Địa chỉ đầy đủ |
| `hotline` | Có | SĐT VN 10–11 số, đầu 03/05/07/08/09 |
| `representativeEmail` | Có | Email RFC — dùng làm username Manager |
| `workingHours` | Có | Khung T2–T6 và T7–CN (`HH:mm`) |
| `businessLicense` | Có | Alphanumeric, khóa pháp lý duy nhất |
| `businessLicenseImageUrl` | Có | JPEG/PNG/PDF |

**Lỗi:**
- `409` — Trùng MST hoặc địa chỉ 100%: *"Mã số thuế hoặc Địa chỉ này đã được đăng ký trên hệ thống. Vui lòng kiểm tra lại."*
- `409` — Email đã có đơn `PENDING_APPROVAL` hoặc thuộc Admin/Manager/CafeStaff

---

## PUT /api/cafe-partner/me/operational-profile (Giai đoạn 2)

Chỉ khi quán ở `DATA_BLANK`. Không chỉnh sửa khi đang `ACTIVE` (phải deactivate trước).

```json
{
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
  "billingModel": 0,
  "tableNames": ["Bàn 1", "Bàn 2", "Bàn 3", "Bàn 4", "Bàn 5", "Bàn 6", "Bàn 7", "Bàn 8"]
}
```

| `billingModel` | API string |
|----------------|------------|
| `0` | `BY_HOUR` |
| `1` | `PER_DRINK` |

---

## POST /api/cafe-partner/me/activate

Kiểm tra ràng buộc tối thiểu (mục 4.2 spec):

- ≥ 5 bàn chơi công cộng
- ≥ 20 bộ game vật lý
- ≥ 3 ảnh không gian hợp lệ
- Sơ đồ bàn: `tableNames.length >= numberOfTables`

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

Migration **không** tự chạy khi API start. Chạy thủ công:

```powershell
dotnet run --project tools/ExecSql -- BoardVerse.Data/update-all-entities.sql
```

### Status migration

| Cũ | Mới |
|----|-----|
| `Pending`, `PendingReview`, `PendingInfo` | `PendingApproval` |
| `Active`, `ContractSigned`, `PendingNegotiation` | `Approved` |
| `Cancelled`, `Rejected` | `Rejected` |

---

## Email (Brevo)

Cấu hình `Brevo` trong `appsettings.json` — xem [auth.md](./auth.md).
