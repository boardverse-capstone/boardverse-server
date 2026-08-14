# ManagerController + ManagerCafeProfileController

**Base routes:**
- `/api/manager` — `ManagerController.cs`
- `/api/manager/cafes/me` — `ManagerCafeProfileController.cs`

**Role:** Manager

## ManagerController

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/my-cafes` | GET | Quán manager đang sở hữu |

**Header:** `Authorization: Bearer <manager-token>`

---

## GET /api/manager/my-cafes

Trả danh sách cafe mà manager hiện tại là chủ (`Cafe.ManagerId`).
Response trả về `ManagerCafeDto[]` — kế thừa `CafeDetailDto` + thêm các field chỉ manager thấy
(SePay raw, hold duration, pricing model raw, schedule, audit timestamps, staff count).

**Response 200:**
```json
{
  "data": [
    {
      "id": "a1aae9db-4f1b-44af-ac86-6038d085df94",
      "name": "Boss cafe",
      "address": "22 Lê Tấn Bê, An Lạc, Hồ Chí Minh 00700, Vietnam",
      "latitude": 10.7249011,
      "longitude": 106.6046094,
      "phoneNumber": "0974993949",
      "description": null,
      "createdAt": "2026-08-01T05:23:53.639196Z",
      "totalSeats": 200,
      "billingModel": "TIME_BASED",
      "basePrice": 80000,
      "tieredBlockRate": null,
      "tieredBlockMinutes": 15,
      "depositPercentage": 0.5,
      "isPricingLocked": false,
      "hasSePayConfigured": false,

      // === CafeDetailDto fields ===
      "operationalStatus": "ACTIVE",
      "operationalStatusReason": null,
      "isCurrentlyOpen": true,
      "refundPolicy": "Partial",
      "refundTiers": [
        { "minHoursBeforeScheduled": 24, "refundPercent": 50 },
        { "minHoursBeforeScheduled": 12, "refundPercent": 25 },
        { "minHoursBeforeScheduled": 0,  "refundPercent": 0 }
      ],
      "depositRatePerPerson": 10,
      "minDeposit": null,
      "cafeConfig": null,
      "availableSeats": 200,
      "heldSeats": 0,
      "inUseSeats": 0,
      "availableSeatsByTimeSlot": null,
      "scheduleOverrides": [],
      "numberOfTables": 0,
      "numberOfPrivateRooms": 0,
      "numberOfGamesOwned": 0,
      "hasGameMaster": false,

      // === ManagerCafeDto (manager-only) ===
      "managerId": "79099361-...",
      "sePayMerchantId": "...",
      "sePayBankCode": "MBBank",
      "sePayAccountNumber": "...",
      "sePayReturnUrl": "...",
      "defaultHoldDurationMinutes": 30,
      "maxAdvanceBookingDays": 7,
      "staffCount": 3,
      "pricingModel": "TimeBased",
      "lockPricingWhileOpen": true,
      "weekdayOpen": "09:00",
      "weekdayClose": "22:00",
      "weekendOpen": "10:00",
      "weekendClose": "23:00",
      "strictSchedule": false,
      "updatedAt": "2026-08-10T05:00:00Z",
      "operationalProfileUpdatedAt": "2026-08-01T06:00:00Z"
    }
  ]
}
```

> **Lưu ý:**
> - Staff endpoint `GET /api/staff/my-cafes` trả về cùng shape `ManagerCafeDto[]` nhưng `managerId`, `sePayMerchantId`, `sePayBankCode`, `sePayAccountNumber`, `sePayReturnUrl` luôn là `null/empty` (chỉ manager mới thấy SePay raw).
> - Field `availableSeats`/`heldSeats`/`inUseSeats` cho biết tổng toàn quán; chi tiết theo `playDate + timeSlot` xem `availableSeatsByTimeSlot`.
> - `RefundTiers` chỉ populate khi `refundPolicy = "Partial"`.

Dùng `id` từ response cho các API `/api/cafes/{cafeId}/...`.

```powershell
$login = Invoke-RestMethod -Uri "http://localhost:5022/api/auth/login" `
  -Method POST -ContentType "application/json" `
  -Body (@{ usernameOrEmail = "manager@boardverse.dev"; password = "Manager@123" } | ConvertTo-Json)

Invoke-RestMethod -Uri "http://localhost:5022/api/manager/my-cafes" `
  -Headers @{ Authorization = "Bearer $($login.data.token)" }
```

---

## ManagerCafeProfileController

Quản lý **hồ sơ vận hành** của cafe mà manager sở hữu (Phase 2 sau khi đã được admin duyệt Phase 1). Tài liệu chi tiết: [cafe-partner.md](./cafe-partner.md).

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/api/manager/cafes/me` | GET | Hồ sơ quán đối tác |
| `/api/manager/cafes/me/operational-profile` | PUT | Cập nhật giờ mở cửa + hạ tầng + catalog |
| `/api/manager/cafes/me/activate` | POST | Kích hoạt quán (DATA_BLANK → ACTIVE) |
| `/api/manager/cafes/me/deactivate` | POST | Tạm dừng (ACTIVE → DATA_BLANK) |
| `/api/manager/cafes/me/close` | POST | Ngừng kinh doanh vĩnh viễn (→ INACTIVE) |
| `/api/manager/cafes/me/reopen` | POST | Mở lại sau khi close |

### Trạng thái vận hành

| Trạng thái | Ý nghĩa |
|------------|---------|
| `DATA_BLANK` | Đã cấp tài khoản, chưa kích hoạt |
| `ACTIVE` | Hiển thị trên Mobile App |
| `INACTIVE` | Ngừng kinh doanh (không thể reopen) |
| `BANNED` | Admin cấm |

> Admin có thêm quyền **`BANNED`** qua [admin-cafe.md](./admin-cafe.md).

### Ví dụ

```powershell
# Lấy hồ sơ
curl.exe http://localhost:5022/api/manager/cafes/me \
  -H "Authorization: Bearer $token"

# Cập nhật operational profile (giờ mở cửa + billing)
curl.exe -X PUT http://localhost:5022/api/manager/cafes/me/operational-profile \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{
    "workingHours":{"weekdayStart":"09:00","weekdayEnd":"22:00","weekendStart":"10:00","weekendEnd":"23:00"},
    "numberOfPrivateRooms":2,
    "spaceImageUrls":["https://..."],
    "billingModel":"TIME_BASED", "basePrice":50000,
    "tieredBlockRate":3000, "tieredBlockMinutes":15
  }'

# Activate
curl.exe -X POST http://localhost:5022/api/manager/cafes/me/activate \
  -H "Authorization: Bearer $token"

# Deactivate
curl.exe -X POST http://localhost:5022/api/manager/cafes/me/deactivate \
  -H "Authorization: Bearer $token"
```

> Đầy đủ chi tiết (validation, ràng buộc khi activate, billing model): xem [cafe-partner.md](./cafe-partner.md).