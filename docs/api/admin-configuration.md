# AdminConfigurationController

**Base route:** `/api/v1/admin/configs`  
**Controller:** `AdminConfigurationController.cs`  
**Role:** Admin

API đọc và cập nhật **System Configuration** — các tham số runtime ảnh hưởng toàn hệ thống (K-factor Elo, deposit hold minutes, v.v.). Cấu hình lưu dạng key-value JSON trong DB, có cache layer (Redis/in-memory).

---

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Lấy toàn bộ cấu hình (dict `key → value`) |
| `/` | PUT | Cập nhật đồng loạt nhiều config, invalidate cache |

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

---

## Cache invalidation

Sau khi `PUT` thành công, hệ thống tự động clear cache key tương ứng. Service đọc config (`ISystemConfigurationProvider`) sẽ tự reload giá trị mới ở request kế tiếp.