# LobbyHubService

**Hub Service:** `LobbyHubService.cs`
**SignalR Hub:** `/hubs/lobby`
**Role:** Player (đã đăng nhập)

Real-time events cho lobby thông qua SignalR. Dùng cho trải nghiệm live khi members join/leave, messages, notification milestones.

## Connection

```javascript
// Client connects with JWT token
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/lobby", { accessTokenFactory: () => jwtToken })
    .withAutomaticReconnect()
    .build();
```

## Events (Server → Client)

### Lobby Events

| Event | Trigger | Payload |
|-------|----------|---------|
| `LobbyCreated` | Lobby mới được tạo thành công | `{ lobbyId, hostId, ... }` |
| `LobbyJoined` | Member join lobby | `{ lobbyId, userId, username, currentPlayers }` |
| `LobbyLeft` | Member leave lobby | `{ lobbyId, userId, currentPlayers, newHostId? }` |
| `LobbyClosed` | Lobby đạt maxPlayers hoặc host đóng | `{ lobbyId, reason }` |
| `LobbyCancelled` | Lobby bị hủy | `{ lobbyId, cancelledBy, reason }` |
| `LobbyTimeout` | Deadline trôi qua không đủ người | `{ lobbyId }` |
| `LobbyReady` | Lobby đạt minPlayers, sẵn sàng check-in | `{ lobbyId }` |

### Member Events

| Event | Trigger | Payload |
|-------|----------|---------|
| `MemberJoined` | Player join lobby | `{ lobbyId, member: { userId, username, karma, avatarUrl } }` |
| `MemberLeft` | Player rời lobby | `{ lobbyId, userId }` |
| `MemberReady` | Member sẵn sàng | `{ lobbyId, userId, readyAt }` |
| `HostChanged` | Host transfer | `{ lobbyId, oldHostId, newHostId }` |

### Message Events

| Event | Trigger | Payload |
|-------|----------|---------|
| `MessageReceived` | Chat message trong lobby | `{ lobbyId, messageId, userId, username, content, sentAt }` |
| `MessageDeleted` | Message bị xóa | `{ lobbyId, messageId }` |

### Reservation Events (BR-NEW-13/14)

| Event | Trigger | Payload |
|-------|----------|---------|
| `LobbyActivated` | Lobby publish (Confirm thành công) | `{ lobbyId, reservationId, ... }` |
| `LobbyConfirmed` | Lobby đạt minPlayers | `{ lobbyId, confirmedAt }` |
| `LobbyAtRiskWarning` | 50% thời gian, < 50% minPlayers | `{ lobbyId, currentPlayers, minPlayers, suggestions }` |
| `LobbyMilestoneNotification` | 48h/24h/2h/30p trước deadline | `{ lobbyId, milestone, message }` |
| `LobbyApprovalRequired` | Lobby cần cafe duyệt (BR-NEW-11) | `{ lobbyId, cafeApprovalDeadline }` |
| `LobbyApproved` | Cafe duyệt thành công | `{ lobbyId }` |
| `LobbyRejected` | Cafe từ chối | `{ lobbyId, reason }` |

## Client → Server Methods

| Method | Description | Parameters |
|--------|-------------|-------------|
| `JoinLobby` | Tham gia lobby group | `lobbyId: guid` |
| `LeaveLobby` | Rời lobby group | `lobbyId: guid` |
| `SendMessage` | Gửi chat message | `{ lobbyId, content }` |
| `SubscribeLobby` | Subscribe lobby events | `lobbyId: guid` |
| `UnsubscribeLobby` | Unsubscribe lobby events | `lobbyId: guid` |

## Example: Subscribe to Lobby

```javascript
// Join lobby group
await connection.invoke("JoinLobby", lobbyId);

// Listen for member join
connection.on("MemberJoined", (data) => {
    console.log(`${data.member.username} joined lobby`);
    updateMemberList(data.lobbyId);
});

// Listen for lobby ready
connection.on("LobbyReady", (data) => {
    console.log("Lobby is ready for check-in!");
    showCheckInButton();
});

// Listen for at-risk warning
connection.on("LobbyAtRiskWarning", (data) => {
    console.log(`Lobby at risk: ${data.currentPlayers}/${data.minPlayers}`);
    showWarningModal(data.suggestions);
});

// Leave when done
await connection.invoke("LeaveLobby", lobbyId);
```

## Authentication

- SignalR JWT token passed via `accessTokenFactory` in JavaScript client.
- Server validates token and extracts `userId` from claims.
- Unauthorized connections are rejected.

## Group Management

- Each lobby has its own SignalR group: `lobby:{lobbyId}`
- When member joins lobby → added to group
- When member leaves or lobby closes → removed from group
- Server broadcasts to entire group

## Reconnection

- Automatic reconnection with exponential backoff
- Client should re-subscribe to groups after reconnect
- `connection.onreconnected` handler to rejoin groups

---

## Liên quan

- [lobby.md](./lobby.md) — Lobby REST API
- [lobby-invite.md](./lobby-invite.md) — Invite system
- [reservation.md](./reservation.md) — Reservation + lobby creation flow
