# HealthController

**Base route:** `/api/health`  
**Controller:** `HealthController.cs`  
**Role:** Public — không cần token

Dùng để kiểm tra API và database trước khi test các endpoint khác.

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/status` | GET | API có hoạt động không |
| `/db-info` | GET | Kiểm tra DB + đếm users |
| `/ping` | GET | Ping đơn giản |

---

## Cách dùng

```powershell
# Kiểm tra nhanh nhất — không cần token
curl.exe http://localhost:5022/api/health/ping

# Kiểm tra API + message chi tiết
curl.exe http://localhost:5022/api/health/status

# Kiểm tra kết nối database (sau khi seed)
curl.exe http://localhost:5022/api/health/db-info
```

**Khi nào dùng:**
- Trước khi chạy test script — đảm bảo API đã start
- CI/CD health check
- Debug lỗi `500` — `db-info` cho biết DB có kết nối được không

---

## GET /api/health/status

**Response 200:**
```json
{
  "statusCode": 200,
  "message": "API is operational",
  "data": { "status": "healthy" }
}
```

---

## GET /api/health/db-info

**Response 200:**
```json
{
  "data": {
    "status": "connected",
    "userCount": 12
  }
}
```

| Field | Ý nghĩa |
|-------|---------|
| `status` | `"connected"` nếu DB OK |
| `userCount` | Số user trong DB — sau seed dev thường ≥ 1 |

**Lỗi:** `500` nếu DB không kết nối được (kiểm tra connection string trong `appsettings.json`).

---

## GET /api/health/ping

**Response 200:**
```json
{
  "message": "pong",
  "data": { "message": "pong" }
}
```
