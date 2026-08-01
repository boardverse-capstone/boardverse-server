# SignalR Hub — Lobby + Booking + Cafe Events

**Source:** `BoardVerse.API/Hubs/LobbyHub.cs` + `LobbyHubService.cs`
**Hub URL:** `/hubs/lobby`
**Auth:** JWT bearer (Player, Manager, CafeStaff, Admin — `[Authorize]` ở class level)

SignalR hub chính phục vụ realtime cho Lobby module + mở rộng cho Booking + Cafe events (qua group pattern). Inject `ILobbyHubService` vào các service khác để broadcast.

| Module | Groups | Events | Trigger |
|---|---|---|---|
| Lobby | `{lobbyId}` | `MemberJoined`, `MemberLeft`, `LobbyFull`, `LobbyCancelled`, `LobbyTimeout`, `LobbyInProgress`, `MemberKicked`, `MemberReady`, `HostChanged`, `LobbyUpdated`, `MessagePosted`, `BookingConfirmed`, `LobbyAutoCancelled` | Lobby service flow |
| Booking | `booking-{bookingId}` | `BookingCheckedIn`, `BookingCheckedOut`, `BookingCancelled`, `BookingNoShowMarked` | Booking service flow |
| Cafe | `cafe-{cafeId}` | `CafePricingChanged` | CafeService.UpdatePricingConfigAsync |
| Nearby | `nearby:{lat:F2}:{lng:F2}:{radiusKm}` | (location-based feed) | Discovery module |

---

## Hub methods (client → server)

| Method | Param | Mục đích |
|---|---|---|
| `JoinLobby(lobbyId)` | `Guid` | Subscribe lobby group — gọi khi mở LobbyDetailPage |
| `LeaveLobby(lobbyId)` | `Guid` | Unsubscribe khi đóng page |
| `SubscribeNearbyLobbies(latitude, longitude, radiusKm)` | `double, double, double` | Subscribe location-based feed (Discovery) |
| `JoinBookingGroup(bookingId)` | `Guid` | Subscribe booking events — mobile gap #7 |
| `LeaveBookingGroup(bookingId)` | `Guid` | Unsubscribe khi đóng BookingDetailPage |
| `JoinCafeGroup(cafeId)` | `Guid` | Subscribe cafe pricing changes — mobile gap #13 |
| `LeaveCafeGroup(cafeId)` | `Guid` | Unsubscribe |

> **Group naming convention:**
> - Lobby groups: just `{lobbyId}` (raw Guid)
> - Booking groups: prefix `booking-` → `booking-{bookingId}`
> - Cafe groups: prefix `cafe-` → `cafe-{cafeId}`
> - Nearby groups: `nearby:{lat:F2}:{lng:F2}:{radiusKm}`

---

## Events (server → client)

### Lobby events

#### `MemberJoined`

Trigger: lobby service gọi khi user xin vào lobby.

```json
{
  "lobbyId": "uuid",
  "member": { ... LobbyMemberDto ... },
  "timestamp": "2026-08-01T19:00:00Z"
}
```

#### `MemberLeft`

Trigger: user rời lobby.

```json
{ "lobbyId": "uuid", "memberId": "uuid", "timestamp": "..." }
```

#### `LobbyFull`

Trigger: lobby đạt MaxMembers hoặc Host bấm khóa.

```json
{
  "lobbyId": "uuid",
  "message": "Lobby is now full. Ready for booking.",
  "timestamp": "..."
}
```

#### `LobbyCancelled`

Trigger: Host hủy thủ công.

```json
{ "lobbyId": "uuid", "reason": "HostCancelled", "timestamp": "..." }
```

#### `LobbyTimeout` + `LobbyAutoCancelled`

Trigger: `LobbyTimeoutJob` (BR-08) khi lobby hết hạn do không đủ members.

**`LobbyTimeout` (legacy, chỉ message):**
```json
{
  "lobbyId": "uuid",
  "message": "Lobby has timed out due to insufficient members.",
  "timestamp": "..."
}
```

**`LobbyAutoCancelled` (mobile gap #9, payload chi tiết):**
```json
{
  "type": "LobbyAutoCancelled",
  "lobbyId": "uuid",
  "cafeId": "uuid",
  "cafeName": "Cờ Cá Nhà Bà Tám",
  "scheduledTime": "2026-08-01T19:00:00Z",
  "reason": "NotEnoughMembers",
  "message": "Lobby của bạn đã bị hủy do không đủ người trước giờ hẹn.",
  "timestamp": "..."
}
```

`reason` có thể là `"NotEnoughMembers"` (thiếu người) hoặc `"OrphanLobbyExpired"` (lobby không có scheduledTime quá 24h).

#### `LobbyInProgress`

Trigger: lobby chuyển sang InProgress khi nhóm check-in tại quán.

```json
{
  "lobbyId": "uuid",
  "message": "All members ready. Lobby transitioned to InProgress.",
  "timestamp": "..."
}
```

#### `MemberKicked`

```json
{ "lobbyId": "uuid", "userId": "uuid", "timestamp": "..." }
```

#### `MemberReady`

Trigger: user bấm "Sẵn sàng" trong lobby.

```json
{ "lobbyId": "uuid", "userId": "uuid", "isReady": true, "timestamp": "..." }
```

#### `HostChanged`

Trigger: Host chuyển nhượng quyền host.

```json
{ "lobbyId": "uuid", "newHostUserId": "uuid", "timestamp": "..." }
```

#### `LobbyUpdated`

Trigger: bất kỳ update metadata nào (settings, schedule, v.v.).

```json
{ "lobbyId": "uuid", "timestamp": "..." }
```

#### `MessagePosted`

Trigger: user gửi message trong lobby chat. Payload là `LobbyMessageDto`:

```json
{
  "id": "uuid",
  "lobbyId": "uuid",
  "senderId": "uuid",
  "senderUsername": "alice",
  "content": "Còn chỗ không?",
  "isSystem": false,
  "createdAt": "2026-08-01T19:00:00Z"
}
```

#### `BookingConfirmed`

Trigger: Host thanh toán cọc thành công (BR-05).

```json
{
  "lobbyId": "uuid",
  "bookingId": "uuid",
  "message": "Booking confirmed. Proceed to cafe.",
  "timestamp": "..."
}
```

---

### Booking events (group `booking-{bookingId}`)

> Trigger khi Staff thao tác trên POS hoặc khi booking bị hủy. Mobile app subscribe khi mở `BookingDetailPage` để update realtime status thay vì polling 5s.

#### `BookingCheckedIn`

Trigger: `POST /api/bookings/{id}/check-in` (Staff/Manager).

```json
{
  "bookingId": "uuid",
  "checkedInAt": "2026-08-01T19:00:00Z",
  "checkedInBy": "uuid",
  "timestamp": "..."
}
```

#### `BookingCheckedOut`

Trigger: `POST /api/bookings/{id}/check-out` (Staff/Manager).

```json
{
  "bookingId": "uuid",
  "checkedOutAt": "2026-08-01T21:30:00Z",
  "totalAmount": 250000,
  "timestamp": "..."
}
```

#### `BookingCancelled`

Trigger: `DELETE /api/bookings/{id}` hoặc Manager cancel.

```json
{
  "bookingId": "uuid",
  "cancelledBy": "uuid",
  "reason": "OutOfStock",
  "refundStatus": "Refunded",
  "timestamp": "..."
}
```

`refundStatus`: `"Refunded"` (100%) / `"PartiallyRefunded"` (theo policy) / `"Forfeited"` (policy=None).

#### `BookingNoShowMarked`

Trigger: sau khi Staff check-out + `AggregateBookingOutcomesAsync`.

```json
{
  "bookingId": "uuid",
  "noShowMemberIds": ["uuid-1", "uuid-2"],
  "karmaDeltas": {
    "uuid-1": -10,
    "uuid-2": -10
  },
  "timestamp": "..."
}
```

---

### Cafe events (group `cafe-{cafeId}`)

#### `CafePricingChanged`

Trigger: `PUT /api/cafes/{id}/pricing-config` (Manager). Push cả SignalR + FCM notification cho booking trong tuần (BR-04).

```json
{
  "type": "CafePricingChanged",
  "cafeId": "uuid",
  "cafeName": "Cờ Cá Nhà Bà Tám",
  "oldFirstHourPrice": 80000,
  "newFirstHourPrice": 100000,
  "effectiveDate": "2026-08-05T00:00:00Z",
  "affectedBookingsCount": 12,
  "timestamp": "..."
}
```

---

## Mobile client pattern (Flutter / dart)

```dart
import 'package:signalr_netcore/signalr_netcore.dart';

final connection = HubConnectionBuilder()
    .withUrl(
      'https://api.boardverse.app/hubs/lobby',
      options: HttpConnectionOptions(
        accessTokenFactory: () async => getJwt(),
      ),
    )
    .build();

// Subscribe khi mở BookingDetailPage
await connection.invoke('JoinBookingGroup', args: [bookingId]);

// Unsubscribe khi đóng page
await connection.invoke('LeaveBookingGroup', args: [bookingId]);

// Handler cho booking events
connection.on('BookingCheckedIn', (args) {
  final payload = args![0];
  // refresh UI, show "Đã check-in!"
});

connection.on('LobbyAutoCancelled', (args) {
  final payload = args![0];
  // show alert + navigate back
});
```

---

## Connection lifecycle

`LobbyHub` override `OnConnectedAsync` + `OnDisconnectedAsync` để log:

```
Client connected: <connectionId> for user <userId>
Client disconnected: <connectionId> for user <userId>. Exception: <ex>
```

Không có auto-cleanup group membership — server side dựa vào `Context.ConnectionId` lifetime. Client **PHẢI** gọi `LeaveLobby/LeaveBookingGroup/LeaveCafeGroup` khi đóng page để tránh leak.

---

## Server-side integration

Inject `ILobbyHubService` vào service cần broadcast:

```csharp
public class BookingService : IBookingService
{
    private readonly ILobbyHubService _hubService;

    public async Task<BookingResponseDto> CheckInAsync(Guid bookingId, Guid staffUserId)
    {
        // ... business logic ...

        // Broadcast SignalR event
        await _hubService.NotifyBookingCheckedIn(bookingId, checkedInAt, staffUserId);
        return dto;
    }
}
```

Có sẵn các method:

- `NotifyMemberJoined/Left/Kicked/Ready`, `NotifyLobbyFull/Cancelled/Timeout/InProgress/Updated/AutoCancelled`
- `NotifyBookingConfirmed/CheckedIn/CheckedOut/Cancelled/NoShowMarked`
- `NotifyCafePricingChanged`
- `NotifyHostChanged`, `NotifyMessagePosted`

---

## Sơ đồ kiến trúc

```
┌─────────────────────┐         ┌─────────────────────────┐
│ BackgroundService    │         │ Web API Controllers      │
│ (LobbyTimeoutJob)    │         │ (BookingController,      │
│                      │         │  CafeController, ...)     │
└──────────┬──────────┘         └───────────┬─────────────┘
           │                                  │
           │ IPushNotificationService         │ ILobbyHubService
           │ (FCM)                            │
           │                                  ▼
           │                       ┌────────────────────┐
           ▼                       │ LobbyHubService    │
   ┌─────────────┐                 │  - NotifyXxxAsync │
   │ Firebase FCM │                 └─────────┬──────────┘
   └──────┬──────┘                           │
          │                                    │ SignalR Broadcast
          ▼                                    ▼
   ┌──────────────────────────────────────────────────┐
   │ Mobile Clients                                    │
   │  - FCM push (background)                          │
   │  - SignalR connection (foreground)                │
   └──────────────────────────────────────────────────┘
```

> Mobile app dùng **FCM push** cho background events + **SignalR** cho foreground realtime updates. Cả 2 đều reference cùng payload schema (xem [notifications.md](./notifications.md) cho FCM payload chi tiết).

---

## Liên quan

- [booking.md](./booking.md) — Booking controller + SignalR events
- [lobby.md](./lobby.md) — Lobby module
- [notifications.md](./notifications.md) — FCM payload format (tương ứng với SignalR events)
- [cafe.md](./cafe.md) — Pricing-config trigger
- [.cursor/rules/boardverse.mdc](../../.cursor/rules/boardverse.mdc) — BR rules (BR-04, BR-08, BR-10)