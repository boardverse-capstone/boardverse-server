# AdminConfigurationController

**Base route:** `/api/v1/admin/configs`  
**Controller:** `AdminConfigurationController.cs`  
**Role:** Admin

API đọc và cập nhật **System Configuration** — các tham số runtime ảnh hưởng toàn hệ thống (K-factor Elo, deposit hold minutes, v.v.). Cấu hình lưu dạng key-value JSON trong DB, có cache layer (Redis/in-memory).

> **Public read-only endpoint**: Xem [SystemConfigurationPublicController](./system-config.md) (`GET /api/v1/system-configs/{key}`) — không cần Admin, dev/QA dùng để check nhanh demo/bypass flag.

---

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Lấy toàn bộ cấu hình (dict `key → value`) |
| `/` | PUT | Cập nhật đồng loạt nhiều config, invalidate cache |
| `/bypass-time-window` | POST | Bật `bypass_time_window_validations` (toàn cục, áp dụng sau ≤ 10s) |
| `/bypass-time-window` | DELETE | Tắt `bypass_time_window_validations` (toàn cục, áp dụng sau ≤ 10s) |
| `/bypass-time-window` | GET | Xem trạng thái bypass hiện tại |
| `/invalidate-cache` | POST | Invalidate cache cấu hình ngay lập tức (bỏ qua TTL 10s) |
| `/demo-loosen-lobby-constraints` | POST | Bật `demo_loosen_lobby_constraints` (demo mode, áp dụng sau ≤ 10s) |
| `/demo-loosen-lobby-constraints` | DELETE | Tắt `demo_loosen_lobby_constraints` (về production-safe, áp dụng sau ≤ 10s) |
| `/demo-loosen-lobby-constraints` | GET | Xem trạng thái demo mode hiện tại |

**Header:** `Authorization: Bearer <admin-token>`

---

## GET /api/v1/admin/configs

Trả về object `{ configKey: configValue }` của toàn bộ system config.

**Response 200:**
```json
{
  "statusCode": 200,
  "message": "Configs retrieved successfully",
  "data": {
    "elo.kfactor": "32",
    "tournament.defaultRoundDurationMinutes": "45",
    "payment.sepay.retryMaxAttempts": "3",
    "cache.ttlSeconds": "300"
  }
}
```

**Response codes:**
- `200` — Trả về dict configs
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `500` — Lỗi hệ thống

---

## PUT /api/v1/admin/configs

Cập nhật đồng loạt nhiều config. Tự động invalidate cache liên quan sau khi lưu.

**Body:**
```json
{
  "configs": [
    { "configKey": "elo.kfactor", "configValue": "32" },
    { "configKey": "tournament.defaultRoundDurationMinutes", "configValue": "60" }
  ]
}
```

| Field | Ràng buộc |
|-------|-----------|
| `configs` | Mảng 1+ entry |
| `configs[].configKey` | Bắt buộc, đã có sẵn trong hệ thống |
| `configs[].configValue` | Bắt buộc, kiểu tùy theo `configKey` |

**Response 200:** dict configs sau khi cập nhật.

**Response codes:**
- `200` — Đã cập nhật + invalidate cache
- `400` — Dữ liệu request không hợp lệ
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `500` — Lỗi hệ thống

---

## Use case phổ biến

| Task | Endpoint |
|------|----------|
| Tăng K-factor Elo giảm tốc độ tăng Elo | `PUT` `elo.kfactor` |
| Thay đổi thời lượng round mặc định tournament | `PUT` `tournament.defaultRoundDurationMinutes` |
| Điều chỉnh retry policy của SePay | `PUT` `payment.sepay.retryMaxAttempts` |
| Tune cache TTL | `PUT` `cache.ttlSeconds` |
| Bật bypass time-window để test check-in / lobby / cancel / no-show không bị chặn bởi deadline | `POST /bypass-time-window` |
| Tắt bypass sau khi test xong | `DELETE /bypass-time-window` |

---

## Bypass Time-Window (Dev/QA convenience)

Cờ `bypass_time_window_validations` cho phép bỏ qua các ràng buộc thời gian sau (chỉ dùng dev/test):

- **Check-in window** — không chặn khi player check-in ngoài khung `scheduledStart ± grace`.
- **Lobby deadline** — không chặn khi member join lobby sau `recruitmentDeadline`.
- **Lobby time-slot change buffer** — không chặn khi đổi time-slot với buffer < 60 phút.
- **Reservation cancel refund milestones** — luôn tính 100% refund (override theo mốc 24h/6h).
- **No-show detection** — background job `ReservationNoShowDetectionJob` skip khi bypass bật.
- **Tournament scheduled time** — không chặn khi `StartTime` không ở tương lai hoặc `RegistrationDeadline` đã qua.

**Ba cách bật bypass (ưu tiên từ cao xuống thấp):**

| # | Cách | Phạm vi | Dùng khi |
|---|------|----------|----------|
| 1 | HTTP header `X-Bypass-Time-Window: true` | 1 request | Test 1 endpoint cụ thể |
| 2 | Query string `?bypassTimeWindow=true` | 1 request | Test từ browser/postman |
| 3 | DB config `bypass_time_window_validations=true` | Toàn cục (mọi instance, áp dụng sau ≤ 10s) | Test full flow / nhiều request |

**Ví dụ:**

```bash
# Bật bypass toàn cục (cách 3)
curl -X POST https://api.boardverse.dev/api/v1/admin/configs/bypass-time-window \
  -H "Authorization: Bearer <token>"

# Bật bypass cho 1 request (cách 1)
curl https://api.boardverse.dev/api/v1/lobbies/{id}/join \
  -H "Authorization: Bearer <token>" \
  -H "X-Bypass-Time-Window: true"

# Tắt bypass toàn cục (cách 3)
curl -X DELETE https://api.boardverse.dev/api/v1/admin/configs/bypass-time-window \
  -H "Authorization: Bearer <token>"
```

**Response 200** (cả POST/DELETE/GET):

```json
{
  "statusCode": 200,
  "message": "Bypass time-window đã bật. Áp dụng trong vòng 10 giây.",
  "data": {
    "bypassEnabled": true,
    "configKey": "bypass_time_window_validations",
    "appliedWithinSeconds": 10
  }
}
```

---

## POST /api/v1/admin/configs/bypass-time-window

Bật `bypass_time_window_validations=true`. Áp dụng cho mọi instance trong vòng 10 giây (cache TTL).

**Response codes:**
- `200` — Bypass đã bật
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `500` — Lỗi hệ thống

---

## DELETE /api/v1/admin/configs/bypass-time-window

Tắt `bypass_time_window_validations=false`. Áp dụng cho mọi instance trong vòng 10 giây.

**Response codes:**
- `200` — Bypass đã tắt
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `500` — Lỗi hệ thống

---

## GET /api/v1/admin/configs/bypass-time-window

Trả về trạng thái bypass hiện tại.

**Response 200:**
```json
{
  "statusCode": 200,
  "message": "OK",
  "data": {
    "bypassEnabled": false,
    "configKey": "bypass_time_window_validations"
  }
}
```

**Response codes:**
- `200` — Trả về trạng thái
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `500` — Lỗi hệ thống

---

## POST /api/v1/admin/configs/invalidate-cache

Force invalidate toàn bộ cache config (bỏ qua TTL 10s). Dùng khi cần áp dụng thay đổi tức thì cho mọi instance.

**Response codes:**
- `200` — Cache đã invalidate
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `500` — Lỗi hệ thống

---

## Demo Mode (`demo_loosen_lobby_constraints`)

Toggle riêng cho demo happy case. Khi bật, hệ thống **bypass** các ràng buộc sau (mặc định `false`):

- **BR-USER-LIMIT-01** — cho phép user host + join nhiều lobby cùng lúc (mặc định giới hạn 2 lobby active)
- **BR-USER-LIMIT-04** — member không bị chặn tạo lobby mới làm host
- **BR-USER-LIMIT-05** — host không bị chặn join lobby khác làm member
- **BR-LOBBY-01a** — không chặn khi buffer ≥ 60 phút
- **BR-LOBBY-01b** — không từ chối khi buffer < 60 phút
- **BR-LOBBY-01c** — không cảnh báo khi buffer 60-120 phút
- **BR-NEW-05** — không giới hạn max 5 lần tạo/hủy / playDate
- **BR-CHECKIN-01** — không chặn early grace 15 phút

**Không bị override:** BR-RISK-04 (suspended/banned), BR-RESERVATION-01/02 (atomic hold seat + game copy), BR-REVENUE-01 (100% deposit về quán).

> ⚠️ **CHỈ bật trên Neon testing branch** (`br-sparkling-salad-aota3n5d`). **KHÔNG BAO GIỜ bật trên production** (`br-hidden-shadow-aoqtn6su`).

**Ba cách bật (ưu tiên từ cao xuống thấm):**

| # | Cách | Phạm vi | Dùng khi |
|---|------|----------|----------|
| 1 | HTTP header `X-Bypass-Demo-Locks: true` | 1 request | Test 1 endpoint cụ thể |
| 2 | Query string `?bypassDemoLocks=true` | 1 request | Test từ browser/postman |
| 3 | DB config `demo_loosen_lobby_constraints=true` (qua API dưới) | Toàn cục (mọi instance, áp dụng sau ≤ 10s) | Demo full flow / smoke test nhiều request |

**Ví dụ:**

```bash
# Bật demo mode toàn cục (cách 3)
curl -X POST https://api.boardverse.dev/api/v1/admin/configs/demo-loosen-lobby-constraints \
  -H "Authorization: Bearer <admin-token>"

# Tắt demo mode
curl -X DELETE https://api.boardverse.dev/api/v1/admin/configs/demo-loosen-lobby-constraints \
  -H "Authorization: Bearer <admin-token>"

# Xem trạng thái hiện tại
curl https://api.boardverse.dev/api/v1/admin/configs/demo-loosen-lobby-constraints \
  -H "Authorization: Bearer <admin-token>"
```

**Response 200** (POST — bật):

```json
{
  "statusCode": 200,
  "message": "Demo mode đã bật. BR-USER-LIMIT-01/04/05, BR-LOBBY-01a/b, BR-NEW-05, BR-CHECKIN-01 sẽ bị bypass. Áp dụng trong vòng 10 giây.",
  "data": {
    "demoEnabled": true,
    "configKey": "demo_loosen_lobby_constraints",
    "appliedWithinSeconds": 10,
    "affectedRules": [
      "BR-USER-LIMIT-01 (max 2 lobby active)",
      "BR-USER-LIMIT-04 (member cannot host)",
      "BR-USER-LIMIT-05 (host cannot join)",
      "BR-LOBBY-01a (buffer >= 60 phút)",
      "BR-LOBBY-01b (buffer < 60 phút reject)",
      "BR-LOBBY-01c (buffer 60-120 phút warning)",
      "BR-NEW-05 (max 5 tạo/hủy / playDate)",
      "BR-CHECKIN-01 (early grace 15 phút)"
    ]
  }
}
```

---

## POST /api/v1/admin/configs/demo-loosen-lobby-constraints

Bật `demo_loosen_lobby_constraints=true`. Áp dụng cho mọi instance trong vòng 10 giây (cache TTL).

**Response codes:**
- `200` — Demo mode đã bật (kèm danh sách affected rules)
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `500` — Lỗi hệ thống

---

## DELETE /api/v1/admin/configs/demo-loosen-lobby-constraints

Tắt `demo_loosen_lobby_constraints=false`. Hệ thống trở về hành vi production (áp dụng đầy đủ BR-USER-LIMIT / BR-LOBBY-01 / BR-NEW-05 / BR-CHECKIN-01).

**Response codes:**
- `200` — Demo mode đã tắt
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `500` — Lỗi hệ thống

---

## GET /api/v1/admin/configs/demo-loosen-lobby-constraints

Trả về trạng thái demo mode hiện tại.

**Response 200:**
```json
{
  "statusCode": 200,
  "message": "OK",
  "data": {
    "demoEnabled": false,
    "configKey": "demo_loosen_lobby_constraints"
  }
}
```

**Response codes:**
- `200` — Trả về trạng thái
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `500` — Lỗi hệ thống

---

## Cache invalidation

Sau khi `PUT` thành công, hệ thống tự động clear cache key tương ứng. Service đọc config (`ISystemConfigurationProvider`) sẽ tự reload giá trị mới ở request kế tiếp.

**TTL cache:** 10 giây (mặc định). Khi toggle bypass qua `POST/DELETE /bypass-time-window`, thay đổi áp dụng cho mọi instance sau tối đa 10 giây. Dùng `POST /invalidate-cache` để force ngay lập tức.