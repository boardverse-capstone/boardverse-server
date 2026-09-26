# ManagerCafeProfileController

**Base route:** `/api/manager/cafes/me`
**Controller:** `ManagerCafeProfileController.cs`
**Role:** Manager

API quản lý hồ sơ quán đối tác của Manager.

---

## Mục lục

- [GET /](#get-)
- [PUT /operational-profile](#put-operational-profile)
- [POST /activate](#post-activate)
- [POST /deactivate](#post-deactivate)
- [POST /close](#post-close)
- [POST /reopen](#post-reopen)
- [PATCH /operational-status](#patch-operational-status)

---

## GET /

Lấy hồ sơ quán đối tác của Manager (Web POS).

### Request

- Method: `GET`
- Path: `/api/manager/cafes/me`
- Auth: Manager

### Response 200

```json
{
  "status": 200,
  "message": "Lấy hồ sơ quán thành công.",
  "data": {
    "cafeId": "guid",
    "name": "Board Game Cafe",
    "address": "123 Đường ABC, Quận 1, TP.HCM",
    "phone": "0912345678",
    "status": "Active",
    "capacity": 30,
    "seatCount": 25,
    "gameCount": 50,
    "images": [
      "https://storage.boardverse.vn/cafes/uuid/img1.jpg"
    ],
    "createdAt": "2026-01-15T08:00:00Z",
    "updatedAt": "2026-08-10T14:30:00Z"
  }
}
```

### Response 401

Unauthorized — thiếu token, token hết hạn hoặc token không hợp lệ.

### Response 403

Forbidden — tài khoản không có quyền Manager.

### Response 404

Chưa có quán đối tác đã được duyệt.

---

## PUT /operational-profile

Cập nhật hồ sơ vận hành (Giai đoạn 2) trước khi kích hoạt.

### Request

- Method: `PUT`
- Path: `/api/manager/cafes/me/operational-profile`
- Auth: Manager

### Request Body

```json
{
  "openingTime": "09:00",
  "closingTime": "23:00",
  "infrastructure": {
    "hasWifi": true,
    "hasParking": false,
    "hasAirConditioning": true,
    "hasFoodService": true
  },
  "gameCatalog": [
    {
      "gameTemplateId": "guid",
      "quantity": 3,
      "availableQuantity": 3
    }
  ],
  "tableLayout": [
    {
      "name": "Bàn 1",
      "seatCount": 4,
      "sortOrder": 0
    }
  ]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `openingTime` | string (HH:mm) | Yes | Giờ mở cửa |
| `closingTime` | string (HH:mm) | Yes | Giờ đóng cửa |
| `infrastructure` | object | No | Thông tin cơ sở vật chất |
| `gameCatalog` | array | No | Danh sách game cho thuê |
| `tableLayout` | array | No | Sơ đồ bàn |

### Response 200

```json
{
  "status": 200,
  "message": "Cập nhật hồ sơ vận hành thành công.",
  "data": {
    "cafeId": "guid",
    "status": "DataBlank",
    "updatedFields": ["openingTime", "closingTime", "infrastructure"]
  }
}
```

### Response 400

Validation error — dữ liệu không hợp lệ hoặc quán đang ACTIVE (cần tạm dừng trước).

### Response 401

Unauthorized.

### Response 403

Forbidden.

### Response 404

Chưa có quán đối tác đã được duyệt.

---

## POST /activate

Kích hoạt quán (DATA_BLANK → ACTIVE) khi đủ điều kiện ràng buộc.

### Request

- Method: `POST`
- Path: `/api/manager/cafes/me/activate`
- Auth: Manager

### Điều kiện kích hoạt

| Yêu cầu | Mô tả |
|----------|--------|
| `seatCount >= 5` | Tối thiểu 5 ghế |
| `gameCount >= 20` | Tối thiểu 20 game |
| `images >= 3` | Tối thiểu 3 ảnh |
| `openingTime` | Đã cấu hình giờ mở cửa |
| `tableLayout` | Đã cấu hình sơ đồ bàn |

### Response 200

```json
{
  "status": 200,
  "message": "Kích hoạt quán thành công.",
  "data": {
    "cafeId": "guid",
    "status": "Active",
    "activatedAt": "2026-08-17T15:00:00Z"
  }
}
```

### Response 400

Chưa đủ điều kiện kích hoạt (thiếu bàn, game, ảnh, giờ mở cửa, sơ đồ bàn) hoặc trạng thái không hợp lệ.

### Response 401

Unauthorized.

### Response 403

Forbidden.

### Response 404

Chưa có quán đối tác đã được duyệt.

---

## POST /deactivate

Tạm dừng hoạt động (ACTIVE → DATA_BLANK).

### Request

- Method: `POST`
- Path: `/api/manager/cafes/me/deactivate`
- Auth: Manager

### Response 200

```json
{
  "status": 200,
  "message": "Tạm dừng quán thành công.",
  "data": {
    "cafeId": "guid",
    "status": "DataBlank",
    "deactivatedAt": "2026-08-17T15:00:00Z"
  }
}
```

### Response 400

Còn phiên đặt bàn đang chạy hoặc trạng thái không hợp lệ.

### Response 401

Unauthorized.

### Response 403

Forbidden.

### Response 404

Chưa có quán đối tác đã được duyệt.

---

## POST /close

Ngừng kinh doanh (ACTIVE/DATA_BLANK → INACTIVE).

### Request

- Method: `POST`
- Path: `/api/manager/cafes/me/close`
- Auth: Manager

### Response 200

```json
{
  "status": 200,
  "message": "Quán đã chuyển sang INACTIVE.",
  "data": {
    "cafeId": "guid",
    "status": "Inactive",
    "closedAt": "2026-08-17T15:00:00Z"
  }
}
```

### Response 400

Còn phiên bàn đang chạy, quán đã INACTIVE/BANNED, hoặc trạng thái không hợp lệ.

### Response 401

Unauthorized.

### Response 403

Forbidden.

### Response 404

Chưa có quán đối tác đã được duyệt.

---

## POST /reopen

Mở lại quán (INACTIVE → ACTIVE) khi đủ điều kiện ràng buộc.

### Request

- Method: `POST`
- Path: `/api/manager/cafes/me/reopen`
- Auth: Manager

### Điều kiện mở lại

Giống như điều kiện kích hoạt:
- `seatCount >= 5`
- `gameCount >= 20`
- `images >= 3`
- `openingTime` đã cấu hình
- `tableLayout` đã cấu hình

### Response 200

```json
{
  "status": 200,
  "message": "Mở lại quán thành công.",
  "data": {
    "cafeId": "guid",
    "status": "Active",
    "reopenedAt": "2026-08-17T15:00:00Z"
  }
}
```

### Response 400

Chưa đủ điều kiện kích hoạt, quán BANNED, hoặc trạng thái không phải INACTIVE.

### Response 401

Unauthorized.

### Response 403

Forbidden.

### Response 404

Chưa có quán đối tác đã được duyệt.

---

## PATCH /operational-status

Manager tự đặt trạng thái vận hành quán mình sở hữu — endpoint hợp nhất cho 3 trạng thái
(`DATA_BLANK`, `ACTIVE`, `INACTIVE`). Trạng thái `BANNED` chỉ Admin mới có quyền đặt — dùng
`PUT /api/v1/admin/cafes/{cafeId}/operational-status`.

Endpoint này thay thế việc gọi riêng `/activate`, `/deactivate`, `/close`, `/reopen` khi Manager
muốn đổi trực tiếp sang trạng thái cụ thể.

### Request

- Method: `PATCH`
- Path: `/api/manager/cafes/me/operational-status`
- Auth: Manager

### Request Body

```json
{
  "status": "ACTIVE",
  "reason": "Mở lại quán sau bảo trì"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `status` | string | Yes | Một trong: `DATA_BLANK`, `ACTIVE`, `INACTIVE`. Không chấp nhận `BANNED`. |
| `reason` | string | No | Lý do chuyển trạng thái. Khuyến nghị cung cấp khi sang `INACTIVE`. Tối đa 500 ký tự. |

### State transition rules

| Từ → Đến | Điều kiện áp dụng |
|---|---|
| `* → ACTIVE` | Áp dụng đầy đủ điều kiện kích hoạt (giống `/activate`, `/reopen`): ≥5 bàn, ≥20 game, ≥3 ảnh, giờ mở cửa, sơ đồ bàn, GPS. |
| `* → DATA_BLANK` hoặc `* → INACTIVE` | Yêu cầu **không còn phiên bàn đang chạy** (cùng rule với `/deactivate`, `/close`). |
| `BANNED → *` | Bị chặn — Manager phải liên hệ Admin. |
| `* → BANNED` | Bị chặn — chỉ Admin có quyền. |
| Status hiện tại = target | No-op idempotent, trả về hồ sơ hiện tại. |

### Response 200

```json
{
  "status": 200,
  "message": "Cập nhật trạng thái vận hành quán thành công.",
  "data": {
    "cafeId": "guid",
    "name": "Board Game Cafe",
    "operationalStatus": "ACTIVE",
    "operationalStatusReason": null,
    "canActivate": false,
    "canReopen": false,
    "activationBlockers": []
  }
}
```

### Response 400

| Lý do | Khi nào |
|---|---|
| `Trạng thái vận hành không hợp lệ. Dùng DATA_BLANK, ACTIVE, INACTIVE hoặc BANNED.` | `status` không parse được. |
| `Bạn không thể tự đặt trạng thái BANNED - chỉ quản trị viên mới có quyền này.` | Gửi `BANNED`. |
| `Chưa đủ điều kiện kích hoạt: ...` | Chuyển sang `ACTIVE` mà thiếu điều kiện. |
| `Không thể chuyển trạng thái vận hành khi quán còn phiên bàn đang chạy.` | Rời `ACTIVE` khi còn phiên active. |

### Response 401

Unauthorized — thiếu token, token hết hạn hoặc token không hợp lệ.

### Response 403

Forbidden — tài khoản không có quyền Manager.

### Response 404

Chưa có quán đối tác đã được duyệt.

### Response 500

Lỗi hệ thống không mong đợi.

### Ví dụ

```powershell
# Activate (tương đương POST /activate)
curl.exe -X PATCH http://localhost:5022/api/manager/cafes/me/operational-status `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{ "status": "ACTIVE" }'

# Pause (tương đương POST /deactivate)
curl.exe -X PATCH http://localhost:5022/api/manager/cafes/me/operational-status `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{ "status": "DATA_BLANK" }'

# Close permanently kèm lý do (tương đương POST /close)
curl.exe -X PATCH http://localhost:5022/api/manager/cafes/me/operational-status `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{ "status": "INACTIVE", "reason": "Đóng cửa vĩnh viễn do chuyển địa điểm" }'
```

> Lưu ý: Endpoint cũ (`/activate`, `/deactivate`, `/close`, `/reopen`) vẫn hoạt động
> để tương thích ngược. FE mới nên chuyển sang dùng `PATCH /operational-status`.

---

## Cafe Status Flow

```
┌─────────────┐
│  Pending    │ (Đang xét duyệt)
└──────┬──────┘
       │ Approve
       ▼
┌─────────────┐     Deactivate      ┌─────────────┐
│  DataBlank   │◄──────────────────►│   Active     │
│ (Chưa kích hoạt) │                  │  (Đang hoạt động) │
└──────┬──────┘     Activate        └──────┬──────┘
       │                                    │
       │              Close                 │
       └────────────►┌─────────────┐       │
                     │  Inactive   │◄─────┘
                     │ (Đã đóng)   │   Reopen
                     └─────────────┘
```

## Deprecation plan (chưa enable)

4 endpoint legacy bên dưới vẫn hoạt động để back-compat nhưng sẽ bị **deprecated** trong phase tiếp theo:

| Legacy endpoint | Thay bằng PATCH `/operational-status` | Timeline |
|---|---|---|
| `POST /activate` | `{ status: "ACTIVE" }` (gửi từ `DataBlank`/`Inactive`) | 4 tuần deprecation → 410 Gone |
| `POST /deactivate` | `{ status: "DATA_BLANK" }` (gửi từ `Active`) | 4 tuần deprecation → 410 Gone |
| `POST /close` | `{ status: "INACTIVE", reason: "..." }` | 4 tuần deprecation → 410 Gone |
| `POST /reopen` | `{ status: "ACTIVE" }` (gửi từ `Inactive`) | 4 tuần deprecation → 410 Gone |

**Hành động theo từng giai đoạn (chưa implement):**

1. **Tuần 0** — Thêm `[Obsolete]` attribute + trả `Sunset: <RFC1123-date>` response header. Log warning mỗi lần gọi legacy endpoint.
2. **Tuần 1** — Push notification cho tất cả client admin/web POS đang gọi legacy.
3. **Tuần 2–4** — Tiếp tục trả 200, log warning, dashboard "deprecated endpoints" cho team.
4. **Tuần 4+** — Trả `410 Gone` với body hướng dẫn dùng `PATCH /operational-status`.

**Audit log** (cả Manager và Admin) đều ghi `PlayerActionHistory.ActionType = CafeOperationalStatusChanged` (60) → query timeline chung để theo dõi ai (admin/manager) đổi status khi nào. Metadata JSONB có `cafeId`, `previousStatus`, `nextStatus`, `isNoOp`, `reason`, `changedAt`.

---

## Related Endpoints

- [Cafe POS](./cafe-pos.md) — POS endpoints cho vận hành quán hàng ngày.
- [Admin Cafe](./admin-cafe.md) — Admin endpoint để duyệt/quản lý quán đối tác.
