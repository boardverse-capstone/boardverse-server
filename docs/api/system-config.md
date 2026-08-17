# SystemConfigurationPublicController

**Base route:** `/api/v1/system-configs`
**Controller:** `SystemConfigurationPublicController.cs`
**Role:** Admin (`[Authorize(Roles = "Admin")]`)

API Admin-only để **xem** (read-only) một system config theo key. Hữu ích cho Admin/Dev/QA kiểm tra nhanh các flag runtime (`demo_loosen_lobby_constraints`, `bypass_time_window_validations`, `elo_k_factor`, v.v.) — yêu cầu JWT token với role `Admin`.

> �️ Endpoint chỉ **đọc**, không ghi. Để bật/tắt flag, dùng [AdminConfigurationController](./admin-configuration.md) (c�ng yêu cầu role Admin).
>
> ⚠️ **Chỉ expose non-sensitive config.** KHÔNG BAO GIỜ trả về password, API key, secret token. Endpoint hiện tại safe vì DB chỉ chứa giá trị công khai.

---

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/{key}` | GET | Lấy 1 config theo key, kèm parsed value (bool/int/double/string) |

**Auth:** Yêu cầu JWT token với role `Admin` (HTTP 401 nếu thiếu/invalid token, 403 nếu thiếu role).

---

## GET /api/v1/system-configs/{key}

Trả về raw string value + parsed value (best-effort detect type) + inferred type của config.

### Path parameters

| Tên | Type | Required | Mô tả |
|-----|------|----------|--------|
| `key` | string | Yes | Config key, ví dụ: `demo_loosen_lobby_constraints`, `elo_k_factor`, `bypass_time_window_validations`. Case-insensitive. |

### Quy tắc parse

Backend thử parse lần lượt theo thứ tự:

1. **bool**: `true` / `false` / `True` / `False` / `TRUE` / `FALSE` / `1` / `0` / `yes` / `no` / `on` / `off`.
2. **int**: số nguyên (`-5`, `32`, `200`).
3. **double**: số thực (`0.15`, `3.14`).
4. **string**: mọi giá trị khác (raw).

`inferredType` cho biết type nào được detect.

### Response 200

```json
{
  "configKey": "demo_loosen_lobby_constraints",
  "configValue": "true",
  "description": "Demo mode: relax BR-USER-LIMIT-01/04/05, BR-LOBBY-01a/b, BR-NEW-05, BR-CHECKIN-01. Chỉ bật trên Neon testing branch, không bật production.",
  "updatedAt": "2026-08-17T16:41:27.971Z",
  "inferredType": "bool",
  "parsedValue": true
}
```

### Response fields

| Field | Type | Mô tả |
|-------|------|--------|
| `configKey` | string | Key (đã trim) |
| `configValue` | string | Raw string value |
| `description` | string | Mô tả config |
| `updatedAt` | DateTime | Lần cuối update |
| `inferredType` | string | `"bool"` \| `"int"` \| `"double"` \| `"string"` |
| `parsedValue` | object? | Parsed value theo inferredType. Có thể là `true`/`false` (bool), số (int/double), hoặc string |

### Response codes

- `200` — Trả về config (raw + parsed).
- `400` — `key` rỗng / whitespace.
- `401` — Thiếu token, token hết hạn hoặc token không hợp lệ.
- `403` — Đã đăng nhập nhưng không có role Admin.
- `404` — Key không tồn tại trong DB.
- `500` — Lỗi hệ thống.

### Response 404 (key không tồn tại)

```json
{
  "error": "Config key 'foo_bar' không tồn tại.",
  "key": "foo_bar"
}
```

### Ví dụ

```bash
# Check demo mode
curl https://api.boardverse.dev/api/v1/system-configs/demo_loosen_lobby_constraints \
  -H "Authorization: Bearer <admin-token>"

# Check bypass time-window
curl https://api.boardverse.dev/api/v1/system-configs/bypass_time_window_validations \
  -H "Authorization: Bearer <admin-token>"

# Check elo K-factor
curl https://api.boardverse.dev/api/v1/system-configs/elo_k_factor \
  -H "Authorization: Bearer <admin-token>"
# → inferredType: "int", parsedValue: 32

# Check commission rate
curl https://api.boardverse.dev/api/v1/system-configs/platform_commission_rate \
  -H "Authorization: Bearer <admin-token>"
# → inferredType: "double", parsedValue: 0.15

# Unknown key → 404
curl https://api.boardverse.dev/api/v1/system-configs/does_not_exist
# → 404 { "error": "...", "key": "does_not_exist" }
```

---

## Use case

| Task | Endpoint |
|------|----------|
| Dev/QA xác nhận demo mode đang bật trên testing branch | `GET /api/v1/system-configs/demo_loosen_lobby_constraints` |
| Smoke test trước khi demo: kiểm tra mọi flag đúng | `GET /api/v1/system-configs/{key}` cho từng key |
| Tự động check trong script CI: parse `parsedValue` thay vì SELECT DB | `GET /api/v1/system-configs/{key}` + parse JSON |

---

## Khác biệt với AdminConfigurationController

| Aspect | Public (`/api/v1/system-configs/{key}`) | Admin (`/api/v1/admin/configs/...`) |
|--------|------------------------------------------|--------------------------------------|
| Auth | `[AllowAnonymous]` | `[Authorize(Roles = "Admin")]` |
| Read 1 key | ✅ (kèm parsed value) | ❌ (chỉ GET all) |
| Read all | ❌ | ✅ (`GET /`) |
| Write | ❌ | ✅ (`PUT /`) |
| Toggle bypass/demo | ❌ | ✅ (`POST/DELETE /bypass-time-window`, `/demo-loosen-lobby-constraints`) |
| Invalidate cache | ❌ | ✅ (`POST /invalidate-cache`) |

---

## Bảo mật

- Endpoint chỉ **đọc** — không có rủi ro lộ password / secret vì DB `SystemConfigurations` không lưu credentials.
- Nếu trong tương lai cần lưu sensitive config (API key, secret), **phải** thêm allowlist ở controller (chỉ cho phép một số key public, các key sensitive ẩn).
- Log không in raw value — chỉ log `configKey` để debug.
