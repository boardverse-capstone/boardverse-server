# SignalR Hub — POS Real-time Notifications

**Source:** `BoardVerse.API/Hubs/PosHub.cs` + `PosHubService.cs`
**Hub URL:** `/hubs/pos`
**Auth:** JWT bearer (`[Authorize]` ở class level — tất cả role phải login)
**Service interface:** `IPosHubService`

Hub **riêng biệt** cho POS realtime notifications (AC 1.4). Tập trung vào events liên quan đến `ActiveSession` lifecycle cho player mobile (session activated, status changed, generic update). Tách khỏi `LobbyHub` để mỗi hub phục vụ 1 concern rõ ràng.

> **Phân biệt với [lobby-hub.md](./lobby-hub.md):**
> - `/hubs/lobby` — Lobby + Booking + Cafe events (BR-04, BR-07, BR-08, BR-10)
> - `/hubs/pos` — POS/ActiveSession events (AC 1.4)

---

## Hub methods (client → server)

| Method | Param | Mục đích |
|---|---|---|
| `JoinSession(sessionId)` | `Guid` | Subscribe updates cho 1 `ActiveSession` — mobile gọi khi mở SessionDetailPage |
| `LeaveSession(sessionId)` | `Guid` | Unsubscribe |
| `JoinUserNotifications(userId)` | `Guid` | Subscribe user-specific notifications (session của cá nhân user) |
| `JoinLobby(lobbyId)` | `Guid` | Subscribe lobby group từ PosHub (cho staff xem lobby khi scan QR) |
| `LeaveLobby(lobbyId)` | `Guid` | Unsubscribe |

> **Group naming convention (PosHub):**
> - Session groups: `session:{sessionId}`
> - User groups: `user:{userId}`
> - Lobby groups (PosHub): `lobby:{lobbyId}`
>
> **KHÔNG trộn với LobbyHub**: PosHub dùng `session:`/`user:` prefix; LobbyHub dùng raw `{lobbyId}` và `booking-`/`cafe-` prefix. Đây là **2 endpoint SignalR khác nhau**, client app cần connect cả 2 nếu muốn cả Lobby + POS events.

---

## Events (server → client)

### `SessionActivated`

Trigger: khi Staff `POST /api/pos/sessions/from-booking` thành công — phiên chơi được kích hoạt cho group. Broadcast đến cả session group + từng member qua user group.

```json
{
  "eventType": "SessionActivated",
  "sessionId": "uuid",
  "cafeId": "uuid",
  "cafeName": "Cờ Cá Nhà Bà Tám",
  "hostId": "uuid",
  "timestamp": "2026-08-01T19:00:00Z"
}
```

**Side effects trên mobile:**
- Hiển thị toast/banner "Phiên chơi đã bắt đầu tại quán"
- Navigate tới SessionDetailPage nếu đang mở BookingDetailPage
- Đếm giờ realtime (giờ chơi còn lại, bill ước tính)

> **AC 1.4:** *"Phát tín hiệu đồng bộ thông báo cho các thiết bị di động của người chơi để cập nhật trạng thái UI."* — Đây chính là trigger đầu tiên trong chuỗi session lifecycle events.

### `SessionStatusChanged`

Trigger: backend thay đổi status của session liên quan đến 1 user (vd: user checkout sớm, phiên của user đó merge sang nhóm khác). Push cho cá nhân user qua `user:{userId}` group.

```json
{
  "eventType": "SessionStatusChanged",
  "sessionId": "uuid",
  "status": "Active",
  "message": "Phiên của bạn đã chuyển sang ActiveSession khác",
  "timestamp": "2026-08-01T20:00:00Z"
}
```

`status` các giá trị khả dĩ: `"Active"` / `"Checking"` / `"UNPAID"` / `"PAID"` / `"SuspendedMutation"`.

### Generic session events

Trigger: tùy workflow (vd: penalty added, member left early, refund processed). Gọi `NotifySessionUpdateAsync(sessionId, eventType, data)` — payload shape linh hoạt.

```json
{
  "eventType": "<custom>",
  "sessionId": "uuid",
  "data": { ... },
  "timestamp": "..."
}
```

Custom events có thể là:

| Event type | Trigger | Use case |
|---|---|---|
| `PenaltyAdded` | `POST /api/pos/sessions/{id}/check-component` thiếu linh kiện | UI update penalty bill realtime |
| `MemberLeftEarly` | Partial checkout 1 member | Hiển thị "Đã rời nhóm" cho các member còn lại |
| `SessionMerged` | A3 merge từ Nhóm A → Nhóm B | UI update avatar + countdown |
| `RefundProcessed` | Refund hoàn tiền cọc | Toast "Đã hoàn 50,000đ" |

---

## Server-side integration

Inject `IPosHubService` vào service để broadcast:

```csharp
public class ActiveSessionService : IActiveSessionService
{
    private readonly IPosHubService _posHubService;

    public async Task<ActiveSession> ActivateSessionAsync(...)
    {
        // ... business logic ...

        var session = await _sessionRepo.CreateAsync(...);

        // Broadcast POS realtime
        await _posHubService.NotifySessionActivatedAsync(
            sessionId: session.Id,
            cafeId: cafe.Id,
            cafeName: cafe.Name,
            hostId: lobby.HostUserId,
            memberUserIds: lobby.Members.Select(m => m.UserId).ToList());

        return session;
    }
}
```

Có sẵn các method:

| Method | Signature | Mục đích |
|---|---|---|
| `NotifySessionActivatedAsync` | `(sessionId, cafeId, cafeName, hostId, memberUserIds)` | Broadcast khi session mới activate |
| `NotifyUserSessionUpdateAsync` | `(userId, sessionId, status, message?)` | Push status change cho 1 user |
| `NotifySessionUpdateAsync` | `(sessionId, eventType, data?)` | Broadcast generic event cho session group |

---

## Mobile client pattern

```dart
// Connect POS hub
final posConnection = HubConnectionBuilder()
    .withUrl(
      'https://api.boardverse.app/hubs/pos',
      options: HttpConnectionOptions(accessTokenFactory: () async => getJwt()),
    )
    .build();

await posConnection.start();

// Subscribe user-specific channel
final userId = await getCurrentUserId();
await posConnection.invoke('JoinUserNotifications', args: [userId]);

// Subscribe session channel khi vào SessionDetailPage
await posConnection.invoke('JoinSession', args: [sessionId]);

// Handlers
posConnection.on('SessionActivated', (args) {
  final data = args![0];
  showToast('Phiên chơi đã bắt đầu!');
  navigateToSessionDetail();
});

posConnection.on('SessionStatusChanged', (args) {
  final data = args![0];
  // refresh session UI
});

posConnection.on('PenaltyAdded', (args) {
  final data = args![0];
  // hiển thị penalty bill realtime
});

// Cleanup khi đóng page
await posConnection.invoke('LeaveSession', args: [sessionId]);
await posConnection.invoke('LeaveUserNotifications', args: [userId]);
```

---

## Connection lifecycle

`PosHub` override `OnConnectedAsync` + `OnDisconnectedAsync` để log:

```
Client connected: <connectionId> for user <userId>
Client disconnected: <connectionId> for user <userId>. Exception: <ex>
```

Group membership KHÔNG tự động cleanup khi disconnect. Client **PHẢI** gọi `Leave*` khi đóng page để giảm leak. Với kết nối reconnect (mobile mất mạng → mở lại), gọi lại `Join*` sau khi `OnReconnected`.

---

## Sơ đồ kiến trúc

```
POS Staff actions (web POS):
  - Scan QR (StartSessionFromBooking)
  - Check-in
  - Check-out + components
  - Partial checkout
  - Merge session

   ↓ (gọi IPosHubService)

PosHubService → SignalR Broadcast → Mobile (SignalR connection)

Mobile response:
  - SessionActivated → navigate to SessionDetail
  - SessionStatusChanged → refresh status
  - PenaltyAdded → update bill
  - MemberLeftEarly → update member list
```

---

## Liên quan

- [lobby-hub.md](./lobby-hub.md) — Lobby + Booking + Cafe events
- [active-session.md](./active-session.md) — ActiveSession REST endpoints
- [settlement.md](./settlement.md) — Settlement flow + penalty
- [notifications.md](./notifications.md) — FCM push notification (cho background events)