# Notifications API

**Base route:** `/api/notifications/device-tokens`
**Controller:** `DeviceTokenController.cs`
**Role:** Player — đã đăng nhập (JWT bearer)

API đăng ký / xóa FCM device token cho push notification. Backend dùng các token này để gửi push khi:

| Trigger | Event type | Nguồn |
|---|---|---|
| Lobby auto-cancel (timeout / orphan) | `LobbyAutoCancelled` | `LobbyTimeoutJob` (mobile gap #9) |
| Manager đổi giá cafe (BR-04) | `CafePricingChanged` | `CafeService.UpdatePricingConfigAsync` (mobile gap #13) |

> **Cấu hình:** `Firebase:Enabled` + `Firebase:ProjectId` + `Firebase:CredentialsJson` trong `appsettings.json` (hoặc env `FIREBASE_CREDENTIALS_JSON` cho production). Khi `Enabled=false`, service log payload thay vì gửi FCM thật — phù hợp local dev / integration test.

---

## Mục lục

- [REST Endpoints](#rest-endpoints)
- [Luồng mobile](#luồng-mobile)
- [Payload format](#payload-format)
- [Connection guide cho mobile](#connection-guide-cho-mobile)

---

## REST Endpoints

### Đăng ký / cập nhật FCM token

**POST /api/notifications/device-tokens**

Mobile app gọi sau khi `FirebaseMessaging.getToken()` trả token (sau login + permission granted).

**Auth:** Player đã đăng nhập.

**Request body:**

| Field | Type | Required | Validation |
|---|---|---|---|
| `token` | string | ✅ | 10–512 chars (FCM registration token) |
| `platform` | string | ✅ | `"android"` / `"ios"` / `"web"` |
| `appVersion` | string | ❌ | ≤32 chars |
| `deviceModel` | string | ❌ | ≤128 chars |

```json
{
  "token": "fK3jH...long_fcm_token...XmPq",
  "platform": "android",
  "appVersion": "1.2.3",
  "deviceModel": "Pixel 7"
}
```

**Idempotent:** gọi nhiều lần với cùng `token` → UPDATE timestamp + re-enable token nếu trước đó bị `IsInvalidated=true`. Nếu token đang thuộc user khác (device reused), reassign sang user hiện tại.

**Response 200:**

```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Đăng ký device token thành công.",
  "data": {
    "id": "uuid",
    "userId": "uuid",
    "platform": "android",
    "appVersion": "1.2.3",
    "deviceModel": "Pixel 7",
    "createdAt": "2026-08-01T19:00:00Z",
    "lastSeenAt": "2026-08-01T19:00:00Z"
  }
}
```

**Errors:**

| Code | Message |
|---|---|
| 400 | `Giá trị platform không hợp lệ. Chỉ chấp nhận 'android', 'ios' hoặc 'web'.` |
| 401 | Thiếu / hết hạn JWT |

---

### Xóa FCM token

**DELETE /api/notifications/device-tokens/{id}**

Mobile gọi khi user logout hoặc gỡ app. Chỉ xóa được token thuộc user hiện tại.

**Auth:** Player đã đăng nhập.

**Response 200:**

```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Đã xóa device token.",
  "data": { "id": "uuid" }
}
```

**Errors:**

| Code | Message |
|---|---|
| 401 | Thiếu / hết hạn JWT |
| 404 | `Không tìm thấy device token để xóa.` |

---

## Luồng mobile

```
1. Mobile: User login → JWT token
2. Mobile: FirebaseMessaging.requestPermission() (iOS / Android 13+)
3. Mobile: FirebaseMessaging.getToken() → FCM registration token
4. Mobile: POST /api/notifications/device-tokens {token, platform, ...}
5. Backend: lưu vào DeviceTokens table (idempotent — re-register OK)

[Sau này, khi Lobby timeout hoặc Manager đổi giá]

6. Backend: gửi FCM push cho tất cả active tokens của user nhận
7. Mobile: onMessageReceived → deeplink theo payload.type
8. Mobile: FCM gọi onTokenRefresh khi token rotate → gọi lại POST
9. Mobile: User logout → DELETE /api/notifications/device-tokens/{id}
```

---

## Payload format

Mỗi push gồm **notification** (title/body — hiển thị trên system tray) + **data** (key-value strings — mobile dùng để route/deeplink).

### LobbyAutoCancelled

Trigger: `LobbyTimeoutJob` khi lobby hết hạn (BR-08 / orphan).

```json
{
  "notification": {
    "title": "Phòng chờ đã bị hủy",
    "body": "Phòng chờ tại Cờ Cá Nhà Bà Tám (19:00 01/08) đã bị hủy do không đủ thành viên trước giờ hẹn."
  },
  "data": {
    "type": "LobbyAutoCancelled",
    "lobbyId": "uuid",
    "cafeId": "uuid",
    "cafeName": "Cờ Cá Nhà Bà Tám",
    "scheduledTime": "2026-08-01T19:00:00Z",
    "reason": "NotEnoughMembers"
  }
}
```

`reason` có thể là `"NotEnoughMembers"` (thiếu người) hoặc `"OrphanLobbyExpired"` (lobby không có scheduledTime quá 24h).

### CafePricingChanged

Trigger: `CafeService.UpdatePricingConfigAsync` khi Manager sửa giá ngoài giờ hoạt động (BR-04). Push cho tất cả user có booking trong tuần của cafe đó.

```json
{
  "notification": {
    "title": "Biểu phí quán đã thay đổi",
    "body": "Cờ Cá Nhà Bà Tám: giờ đầu từ 80,000đ → 100,000đ. Có 12 đơn đặt chỗ trong tuần bị ảnh hưởng."
  },
  "data": {
    "type": "CafePricingChanged",
    "cafeId": "uuid",
    "cafeName": "Cờ Cá Nhà Bà Tám",
    "oldFirstHourPrice": "80000",
    "newFirstHourPrice": "100000",
    "effectiveDate": "2026-08-01T19:00:00Z",
    "affectedBookingsCount": "12"
  }
}
```

---

## Connection guide cho mobile

### Android (Flutter `firebase_messaging`)

```dart
import 'package:firebase_messaging/firebase_messaging.dart';

final token = await FirebaseMessaging.instance.getToken();
await api.post('/api/notifications/device-tokens', body: {
  'token': token,
  'platform': 'android',
  'appVersion': packageInfo.version,
  'deviceModel': deviceInfo.model,
});

FirebaseMessaging.onTokenRefresh.listen((newToken) async {
  await api.post('/api/notifications/device-tokens', body: {
    'token': newToken, 'platform': 'android',
  });
});

FirebaseMessaging.onMessage.listen((RemoteMessage message) {
  switch (message.data['type']) {
    case 'LobbyAutoCancelled':
      navigator.pushNamed('/lobby-detail', arguments: message.data['lobbyId']);
      break;
    case 'CafePricingChanged':
      navigator.pushNamed('/cafe-detail', arguments: message.data['cafeId']);
      break;
  }
});
```

### iOS (Swift)

```swift
Messaging.messaging().token { token, error in
    guard let token = token else { return }
    api.post("/api/notifications/device-tokens", body: [
        "token": token, "platform": "ios",
        "appVersion": Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String,
    ])
}
```

---

## Token lifecycle

| Status | Meaning | Push? |
|---|---|---|
| `IsInvalidated=false`, `LastSeenAt` recent | Active token | ✅ |
| `IsInvalidated=true` | FCM trả `UNREGISTERED` (user gỡ app / token expired) — auto-marked bởi `FcmPushNotificationService.InvalidateFailedTokensAsync` | ❌ |
| Mobile chưa gọi POST lại sau khi token refresh | Stale | ❌ |

Backend **không tự động xóa** token — chỉ mark invalidated. Cleanup job (TODO) sẽ DELETE rows có `IsInvalidated=true` + `LastSeenAt < now - 90 days`.
