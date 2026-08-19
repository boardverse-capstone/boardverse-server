# PosHubService

**Hub Service:** `PosHubService.cs`
**SignalR Hub:** `/hubs/pos`
**Role:** Cafe Staff/Manager (đã đăng nhập)

Real-time events cho POS operations. Staff receive live updates when players scan QR, sessions start/end, etc.

## Connection

```javascript
// POS client connects with staff JWT
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/pos", { accessTokenFactory: () => staffJwtToken })
    .withAutomaticReconnect()
    .build();
```

## Events (Server → Client)

### Session Events

| Event | Trigger | Payload |
|-------|----------|---------|
| `SessionStarted` | Player check-in thành công | `{ sessionId, cafeId, players, gameId, startedAt }` |
| `SessionEnded` | Session thanh toán hoàn tất | `{ sessionId, totalAmount, paidAt }` |
| `PlayerJoinedSession` | Player join session từ POS | `{ sessionId, playerId, playerName }` |
| `PlayerLeftSession` | Player về sớm | `{ sessionId, playerId, playerName }` |

### Check-in Events

| Event | Trigger | Payload |
|-------|----------|---------|
| `CheckInTokenCreated` | Staff tạo token cho player scan | `{ tokenId, token, expiresAt, reservationId }` |
| `PlayerScannedToken` | Player scan QR | `{ tokenId, playerId, playerName, scannedAt }` |
| `PlayerCheckedIn` | Player check-in thành công | `{ reservationId, activeSessionId, playerId }` |

### Game Events

| Event | Trigger | Payload |
|-------|----------|---------|
| `GameBorrowed` | Game assigned to session | `{ sessionId, gameId, gameName, barcode }` |
| `GameReturned` | Game returned to inventory | `{ sessionId, gameId, gameName, checkedAt }` |
| `ComponentCheckRequired` | Need to verify game components | `{ sessionId, gameId, components[] }` |

### Lobby Events (for POS display)

| Event | Trigger | Payload |
|-------|----------|---------|
| `UpcomingReservationAlert` | Reservation sắp đến (15 min before) | `{ reservationId, players[], expectedTime }` |
| `LobbyAtRiskAlert` | Lobby sắp fail | `{ lobbyId, currentPlayers, minPlayers }` |

## Client → Server Methods

| Method | Description | Parameters |
|--------|-------------|-------------|
| `JoinCafe` | Subscribe to cafe events | `cafeId: guid` |
| `LeaveCafe` | Unsubscribe from cafe | `cafeId: guid` |
| `SubscribeSession` | Subscribe to session events | `sessionId: guid` |
| `UnsubscribeSession` | Unsubscribe from session | `sessionId: guid` |

## Example: POS Dashboard

```javascript
// Join cafe group
await connection.invoke("JoinCafe", cafeId);

// Listen for new check-in
connection.on("PlayerCheckedIn", (data) => {
    showToast(`Player ${data.playerId} checked in!`);
    refreshSessionList();
});

// Listen for upcoming reservations
connection.on("UpcomingReservationAlert", (data) => {
    showAlert(`Reservation in 15 min: ${data.players.join(', ')}`);
});

// Listen for component check
connection.on("ComponentCheckRequired", (data) => {
    showComponentCheckModal(data);
});
```

## Authentication

- Staff JWT token required
- Token contains `cafeId` claim
- Staff can only subscribe to their own cafe

## Cafe Groups

- Each cafe has its own SignalR group: `cafe:{cafeId}`
- Only staff of that cafe can join
- Useful for multi-POS displays showing same cafe

---

## Liên quan

- [cafe-pos.md](./cafe-pos.md) — POS REST API
- [player-check-in.md](./player-check-in.md) — Player QR scan flow
