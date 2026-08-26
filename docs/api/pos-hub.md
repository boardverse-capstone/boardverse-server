# PosHub & PosHubService

**Hub Service:** `BoardVerse.API/Hubs/PosHubService.cs`
**SignalR Hub:** `/hubs/pos`
**Role:** Đã đăng nhập (Player + Staff/Manager đều dùng chung hub)

Real-time events cho POS + Player apps: check-in, session lifecycle, lobby updates, extension request, payment.

> **Lưu ý cập nhật 2026-08-22:** Hub trước đây chỉ dùng cho POS staff. Sau khi gộp PlayerSession events vào cùng hub, **c**** cả player lẫn staff/manager** cùng connect tới `/hubs/pos`. Player subscribe group `user:{userId}` + `session:{sessionId}`; staff subscribe group `cafe:{cafeId}` + `session:{sessionId}`.

## Connection

```javascript
// Cả player và staff đều dùng hub này — chỉ JWT khác role
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/pos", { accessTokenFactory: () => jwtToken })
    .withAutomaticReconnect()
    .build();
```

## Groups (chuẩn hóa từ 2026-08-22)

| Group | Format | Subscribe ai? | Mục đích |
|-------|--------|----------------|----------|
| **Session** | `session:{sessionId}` | POS staff + members trong session | Nhận event về phiên chơi cụ thể (check-in, paid, completed, …) |
| **Lobby** | `lobby:{lobbyId}` | Lobby host + members | Nhận event về lobby (member join, host cancel, status change) |
| **User** | `user:{userId}` | Player (chỉ chính mình) | Nhận notification cá nhân (extension approved/rejected, session status) |
| **Cafe** | `cafe:{cafeId}` | POS staff của cafe | Nhận event phạm vi quán (extension requested, upcoming reservation) |

> **Quy tắc đặt tên:** dùng `:` (colon) làm separator. Trước đây có bug drift `cafe-{guid}` vs `cafe:{guid}` — đã chuẩn hóa về `cafe:{guid}`.

## Client → Server Methods

| Method | Params | Ai gọi? | Validation | Mục đích |
|--------|--------|---------|------------|----------|
| `JoinSession` | `cafeId: Guid, sessionId: Guid` | Player + POS | User phải là participant của session **trong cafe đó** (IDOR chống multi-tenant leak) | Subscribe group `session:{sessionId}` |
| `LeaveSession` | `cafeId: Guid, sessionId: Guid` | Player + POS | Validate participant trước khi remove | Unsubscribe |
| `JoinUserNotifications` | `userId: Guid` | Player | **Chỉ được join đúng `userId` của mình** (chống IDOR) | Subscribe group `user:{userId}` |
| `JoinLobby` | `lobbyId: Guid` | Player | Phải là lobby member | Subscribe group `lobby:{lobbyId}` |
| `LeaveLobby` | `cafeId: Guid, lobbyId: Guid` | Player | Validate member trước khi remove | Unsubscribe |

> **Lỗi trả về:** Khi validation fail, server throw `HubException` với `ApiErrorMessages.Jwt.AccessDenied`. Client phải handle như 403.

## Events (Server → Client)

### Session Events

| Event | Trigger | Group | Payload |
|-------|---------|-------|---------|
| `SessionActivated` | Player check-in hoặc POS start session | `session:{sessionId}` + `user:{userId}` của từng member | `{ eventType, sessionId, cafeId, cafeName, hostId, timestamp }` |
| `SessionStatusChanged` | Trạng thái session đổi (Active/Checking/Unpaid/Paid) | `user:{userId}` | `{ eventType, sessionId, status, message?, timestamp }` |
| `SessionCompleted` | Phiên kết thúc (session → Closed) | `session:{sessionId}` | `{ eventType, sessionId, lobbyId?, message, timestamp }` |
| `SessionPaid` | Player/Staff thanh toán thành công | `session:{sessionId}` | `{ eventType, sessionId, cafeId, lobbyId?, totalAmount, paidAt, timestamp }` |
| `SessionUpdate` | Custom update từ server (generic channel) | `session:{sessionId}` | `{ eventType, sessionId, data?, timestamp }` |

### Session Extension Events <a id="session-extension-events"></a>

| Event | Trigger | Group | Payload |
|-------|---------|-------|---------|
| `SessionExtensionRequested` | Player gọi `POST /api/v1/sessions/me/extend` | `cafe:{cafeId}` | `{ eventType, sessionId, cafeId, requestedByUserId, requestedMinutes, estimatedAdditionalCostVnd, message, timestamp }` |
| `SessionExtensionApproved` | Staff approve extension request | `user:{playerId}` | `{ eventType, requestId, playerId, approvedMinutes, message, timestamp }` |
| `SessionExtensionRejected` | Staff reject extension request | `user:{playerId}` | `{ eventType, requestId, playerId, reason?, message, timestamp }` |

### Lobby Events

| Event | Trigger | Group | Payload |
|-------|---------|-------|---------|
| `LobbyUpdate` | Custom update (host cancel, member join, status change) | `lobby:{lobbyId}` | `{ eventType, lobbyId, data?, timestamp }` |
| `LobbyTimeout` | Lobby timeout (legacy channel, backward-compat) | `lobby:{lobbyId}` | (payload theo `NotifyLobbyTimeout`) |
| `LobbyAutoCancelled` | Lobby auto-cancel (kèm lý do) | `lobby:{lobbyId}` | `{ eventType, lobbyId, cafeId, cafeName, scheduledTime?, reason, timestamp }` |

> **Reason values** trong `LobbyAutoCancelled`: `LobbyReadyTimeout` | `OrphanLobbyExpired` | `NotEnoughReadyMembers`.

### Game / Component Check Events

| Event | Trigger | Group | Payload |
|-------|---------|-------|---------|
| `GameBorrowed` | Game gán vào session | `session:{sessionId}` | `{ sessionId, gameId, gameName, barcode }` |
| `GameReturned` | Game trả về kho | `session:{sessionId}` | `{ sessionId, gameId, gameName, checkedAt }` |
| `ComponentCheckRequired` | Cần kiểm kê linh kiện | `session:{sessionId}` | `{ sessionId, gameId, components[] }` |

### Reservation / Check-in Events

| Event | Trigger | Group | Payload |
|-------|---------|-------|---------|
| `CheckInTokenCreated` | Staff tạo token cho player scan | `cafe:{cafeId}` | `{ tokenId, token, expiresAt, reservationId }` |
| `PlayerScannedToken` | Player scan QR POS | `cafe:{cafeId}` | `{ tokenId, playerId, playerName, scannedAt }` |
| `PlayerCheckedIn` | Player check-in thành công | `cafe:{cafeId}` | `{ reservationId, activeSessionId, playerId }` |
| `UpcomingReservationAlert` | Reservation sắp đến (15 phút trước) | `cafe:{cafeId}` | `{ reservationId, players[], expectedTime }` |
| `LobbyAtRiskAlert` | Lobby có nguy cơ fail | `cafe:{cafeId}` | `{ lobbyId, currentPlayers, minPlayers }` |

---

## Example: Player mobile app

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/pos", { accessTokenFactory: () => playerJwt })
    .build();

await connection.start();

// Subscribe cá nhân
await connection.invoke("JoinUserNotifications", currentUserId);

// Khi vào lobby
await connection.invoke("JoinLobby", lobbyId);

// Khi vào phiên (sau check-in)
await connection.invoke("JoinSession", cafeId, sessionId);

// Listen extension approved
connection.on("SessionExtensionApproved", (data) => {
    showToast(`Gia hạn ${data.approvedMinutes} phút đã được duyệt`);
});

connection.on("SessionPaid", (data) => {
    router.push(`/receipt/${data.sessionId}`);
});
```

## Example: POS Dashboard

```javascript
await connection.start();

await connection.invoke("JoinCafe", cafeId);
// (server enforces staff must belong to cafeId)

// Listen extension requested — staff click vào toast để approve
connection.on("SessionExtensionRequested", (data) => {
    showToast(`Yeu cau gia han ${data.requestedMinutes} phut (${data.estimatedAdditionalCostVnd.toLocaleString()} VND)`);
    refreshExtensionQueue();
});

connection.on("PlayerCheckedIn", (data) => {
    refreshSessionList();
});
```

---

## Authentication

- JWT token **bắt buộc** (`[Authorize]` trên hub class).
- Player JWT chứa `userId` claim → dùng cho `JoinUserNotifications`.
- Staff/Manager JWT chứa `userId` + `role` claim → validate quyền truy cập `cafe:{cafeId}` ở controller trước khi subscribe.
- Mọi method đều có IDOR guard: server check user có phải participant/member/owner hay không trước khi add vào group.

---

## Lifecycle

```javascript
// Trên app start
await connection.start();

// Subscribe tương ứng screen hiện tại
await connection.invoke("JoinUserNotifications", currentUserId);

// Cleanup khi rời screen / đóng app
await connection.invoke("LeaveSession", cafeId, sessionId);
await connection.invoke("LeaveLobby", cafeId, lobbyId);

// Server-side: tự cleanup khi connection drop (OnDisconnectedAsync).
```

---

## Liên quan

- [cafe-pos.md](./cafe-pos.md) — POS REST API (gọi các hub method ngầm)
- [lobby-hub.md](./lobby-hub.md) — Lobby-specific SignalR events (nếu tách hub)
- [player-session.md](./player-session.md) — Player-facing API (subscribe `user:{userId}`)
- Controller: `BoardVerse.API/Hubs/PosHub.cs`
- Service: `BoardVerse.API/Hubs/PosHubService.cs`