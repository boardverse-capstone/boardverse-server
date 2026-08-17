# DeviceTokenController

**Base route:** `/api/notifications/device-tokens`
**Controller:** `DeviceTokenController.cs`
**Role:** Authenticated User (JWT bearer)

Quản lý FCM device tokens cho push notifications trên mobile app.

---

## Mục lục

- [POST /](#post-)
- [DELETE /{id}](#delete-id)

---

## POST /

Đăng ký hoặc cập nhật FCM device token cho user hiện tại.

### Request

- Method: `POST`
- Path: `/api/notifications/device-tokens`
- Auth: Authenticated User

### Request Body

```json
{
  "fcmToken": "dGVzdC10b2tlbi0xMjM0NTY3ODkwMTIzNDU2Nzg5MDEyMzQ1Njc4OTA",
  "platform": "android",
  "deviceModel": "Samsung Galaxy S23",
  "osVersion": "14",
  "appVersion": "1.0.0"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `fcmToken` | string | Yes | FCM token từ Firebase SDK |
| `platform` | string | Yes | `android`, `ios`, `web` |
| `deviceModel` | string | No | Model thiết bị |
| `osVersion` | string | No | Phiên bản OS |
| `appVersion` | string | No | Phiên bản app |

### Response 200

```json
{
  "status": 200,
  "message": "Đăng ký device token thành công.",
  "data": {
    "id": "guid",
    "userId": "guid",
    "fcmToken": "dGVzdC10b2tlbi0xMjM0NTY3ODkwMTIzNDU2Nzg5MDEyMzQ1Njc4OTA",
    "platform": "android",
    "deviceModel": "Samsung Galaxy S23",
    "osVersion": "14",
    "appVersion": "1.0.0",
    "isActive": true,
    "createdAt": "2026-08-17T10:00:00Z",
    "updatedAt": "2026-08-17T10:00:00Z"
  }
}
```

### Response 400

Validation error — platform không hợp lệ hoặc token rỗng.

### Response 401

Unauthorized — thiếu hoặc token không hợp lệ.

---

## DELETE /{id}

Xóa FCM device token (khi user logout hoặc gỡ app).

### Request

- Method: `DELETE`
- Path: `/api/notifications/device-tokens/{id}`
- Auth: Authenticated User

### Path Parameters

| Param | Type | Description |
|-------|------|-------------|
| `id` | Guid | Device token ID |

### Response 204

Xóa thành công — không có body.

### Response 401

Unauthorized — thiếu token.

### Response 404

Token không tìm thấy hoặc không thuộc user hiện tại.

---

## Mục đích sử dụng

### Mobile App Integration

1. **Sau khi Firebase SDK trả token:**
   ```dart
   // Flutter example
   final token = await FirebaseMessaging.instance.getToken();
   await api.post('/notifications/device-tokens', {
     'fcmToken': token,
     'platform': 'android',
     'deviceModel': deviceInfo.model,
   });
   ```

2. **Khi logout hoặc gỡ app:**
   ```dart
   await api.delete('/notifications/device-tokens/$tokenId');
   ```

### Push Notification Scenarios

| Scenario | Trigger |
|----------|---------|
| Lobby invitation | Member receives invite |
| Lobby status update | Member joined/left |
| Reservation reminder | 2h/30min before play time |
| Karma change | After session completed |
| Deposit refund | Reservation cancelled |

---

## Idempotency

- Gọi `POST` nhiều lần với cùng `fcmToken` → chỉ cập nhật `updatedAt`.
- Không tạo duplicate token cho cùng một device.
