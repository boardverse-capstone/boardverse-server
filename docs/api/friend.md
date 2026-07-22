# FriendController

**Base route:** `/api/v1/friends`
**Controller:** `FriendController.cs`
**Role:** Player — đã đăng nhập

API quản lý quan hệ bạn bè: gửi lời mời, accept/decline, block, search user, danh sách bạn bè, mutual friends, suggestions, privacy, note, report. Dùng cho Player muốn kết nối trước khi tạo lobby chung.

## Endpoints

| Endpoint | Method | Role | Mô tả |
|----------|--------|------|-------|
| `/requests` | POST | Player | Gửi lời mời kết bạn |
| `/requests/{id}/accept` | POST | Player (addressee) | Accept lời mời |
| `/requests/{id}/decline` | POST | Player (addressee) | Từ chối lời mời |
| `/requests/{id}/read` | POST | Player (addressee) | Đánh dấu đã đọc lời mời (inbox) |
| `/{id}` | DELETE | Player (1 trong 2 bên) | Hủy kết bạn / xóa quan hệ (auto-cancel lobby invite Pending) |
| `/block/{userId}` | POST | Player | Chặn user |
| `/block/{userId}` | DELETE | Player (người đã chặn) | Bỏ chặn user |
| `/` | GET | Player | Danh sách bạn bè (Accepted) |
| `/activity` | GET | Player | Friend list + activity status |
| `/requests/received` | GET | Player | Inbox: lời mời đến (Pending) |
| `/requests/sent` | GET | Player | Outbox: lời mời đã gửi (Pending) |
| `/search?q=&limit=` | GET | Player | Tìm user theo username + FriendshipStatus + MutualFriendsCount |
| `/suggestions?limit=` | GET | Player | Gợi ý kết bạn (friends-of-friends + same lobby) |
| `/{otherUserId}/mutual` | GET | Player | Bạn chung giữa current user và otherUser |
| `/{otherUserId}/list` | GET | Player | Friend list của otherUser (tôn trọng privacy) |
| `/privacy` | PUT | Player | Cập nhật IsFriendListPublic / AcceptFriendRequestsFrom / FriendLimit |
| `/notes` | GET | Player | Danh sách ghi chú bạn bè |
| `/notes/{friendUserId}` | PUT | Player | Tạo/cập nhật ghi chú |
| `/notes/{noteId}` | DELETE | Player (owner) | Xóa ghi chú |
| `/reports` | POST | Player | Báo cáo vi phạm một user (chỉ Accepted friend) |
| `/reports` | GET | Player | Danh sách báo cáo của current user |

**Header bắt buộc:** `Authorization: Bearer <player-token>`

---

## Luồng tích hợp

```
Player A: GET /api/v1/friends/search?q=alice
   → Nhận UserSearchResultDto[] kèm FriendshipStatus + MutualFriendsCount

Nếu FriendshipStatus = null:
   POST /api/v1/friends/requests   { addresseeId: "...", message: "Chơi Catan nhé" }
   → 201 Created (kèm AddresseeReadAt = null)

Player B: GET /api/v1/friends/requests/received
   → Thấy request từ Player A

Player B: POST /api/v1/friends/requests/{id}/read    // đánh dấu đã đọc
Player B: POST /api/v1/friends/requests/{id}/accept  // accept
   → 200 OK, status = Accepted

Cả 2 bên: GET /api/v1/friends
   → Thấy nhau trong danh sách bạn bè

Cả 2 bên: GET /api/v1/friends/activity
   → Thấy nhau với activityStatus = Online/RecentlyActive/Away/Offline

Sau khi là bạn bè:
   → Có thể gửi lobby invite (lobby private chỉ invite được bạn bè).
   → Nếu A unfriend B → tất cả lobby invite Pending giữa A & B bị tự động hủy.
```

---

## POST /api/v1/friends/requests

Gửi lời mời kết bạn.

**Body:**
```json
{
  "addresseeId": "<guid>",
  "message": "Chơi Catan nhé!"
}
```

| Field | Required | Mô tả |
|-------|----------|-------|
| `addresseeId` | ✅ | Mã người nhận. |
| `message` | ❌ | Lời nhắn (≤ 200 ký tự). |

**Response 201:** `FriendshipResponseDto` kèm status = `Pending`.

**Lỗi:**
- `400` gửi cho chính mình / tài khoản không hoạt động.
- `403` người nhận đã block bạn / privacy = FriendsOfFriends và bạn không có bạn chung.
- `404` không tìm thấy người nhận.
- `409` đã là bạn bè / đã có lời mời Pending.
- `429` vượt quá 20 lời mời/giờ (rate limit).

---

## POST /api/v1/friends/requests/{id}/accept

**Path param:** `id` (Guid).

**Response 200:** `FriendshipResponseDto` với status = `Accepted`, `AcceptedAt` set, `MutualFriendsCount` được tính.

**Lỗi:**
- `400` requester không active / addressee không active.
- `403` không phải addressee / bị block ngược chiều / vượt FriendLimit.
- `404` không tìm thấy.
- `409` không ở trạng thái Pending.

---

## POST /api/v1/friends/requests/{id}/decline

Từ chối lời mời. Record chuyển sang `Removed` (có thể gửi lại sau).

**Response 200:** `FriendshipResponseDto` với status = `Removed`.

---

## POST /api/v1/friends/requests/{id}/read

Đánh dấu lời mời đã đọc (inbox notification). Cập nhật `AddresseeReadAt = Now`.

**Response 200.**

---

## DELETE /api/v1/friends/{id}

Hủy kết bạn. Cả 2 bên đều có thể gọi.

**Side effects:**
- Friendship → `Removed`.
- Tất cả LobbyInvite Pending giữa 2 user bị **tự động hủy** (BR-FRIEND-CASCADE-01).

**Lỗi:**
- `400` quan hệ không ở `Accepted`.
- `403` không thuộc quan hệ.

---

## POST /api/v1/friends/block/{targetUserId}

Chặn user.

**BR:**
- Sau khi chặn, user bị chặn không thể gửi friend request.
- User bị chặn không thể gửi lobby invite (BR-FRIEND-02 / BR-LOBBY-INVITE-04).
- Không thể chặn Admin.
- Không thể chặn chính mình.

---

## DELETE /api/v1/friends/block/{targetUserId}

Bỏ chặn. Quan hệ chuyển `Removed`. Chỉ người đã chặn mới có thể bỏ chặn.

**Lỗi:**
- `403` bạn không phải người đã chặn.

---

## GET /api/v1/friends

Danh sách bạn bè (status = `Accepted`).

**Response 200:** `FriendSummaryDto[]` — `userId`, `username`, `avatarUrl`, `karmaPoints`, `gamerTier`, `friendsSince`, `activityStatus`, `lastActiveAt`.

---

## GET /api/v1/friends/activity

Giống `/friends` nhưng trả `FriendActivityDto` (mở rộng: `activityStatus`, `lastActiveAt`).

**Activity Status:**
- `Online` — lastActiveAt ≤ 5 phút trước.
- `RecentlyActive` — ≤ 1 giờ.
- `Away` — ≤ 7 ngày.
- `Offline` — chưa từng online hoặc > 7 ngày.

---

## GET /api/v1/friends/requests/received

Inbox lời mời đang chờ (current user là addressee). Mỗi entry kèm `message`, `addresseeReadAt`, `mutualFriendsCount`.

---

## GET /api/v1/friends/requests/sent

Outbox lời mời đã gửi (current user là requester).

---

## GET /api/v1/friends/search

Tìm user theo username (case-insensitive contains). Kết quả kèm `FriendshipStatus` + `MutualFriendsCount` để UI biết có thể gửi request không.

**Query params:**
- `q` (string, ≥ 2 ký tự) — từ khóa.
- `limit` (int, 1-50, mặc định 20).

**Response 200:** `UserSearchResultDto[]` — `userId`, `username`, `avatarUrl`, `karmaPoints`, `friendshipStatus`, `mutualFriendsCount`.

---

## GET /api/v1/friends/suggestions

Gợi ý kết bạn:
- **MutualFriends**: bạn của bạn chưa kết bạn với bạn.
- **SameLobbyRecent**: người cùng chơi trong lobby trong 30 ngày gần đây (weight cao hơn).

**Query params:**
- `limit` (int, 1-50, mặc định 20).

**Response 200:** `FriendSuggestionDto[]` — `userId`, `username`, `avatarUrl`, `karmaPoints`, `gamerTier`, `mutualFriendsCount`, `reason`.

---

## GET /api/v1/friends/{otherUserId}/mutual

Bạn chung giữa currentUser và otherUser.

**Response 200:** `MutualFriendDto[]` — `userId`, `username`, `avatarUrl`, `friendsSince`.

---

## GET /api/v1/friends/{otherUserId}/list

Friend list của otherUser. Tôn trọng `IsFriendListPublic`:
- Nếu public → trả về.
- Nếu private → chỉ bạn bè mới xem được, ngược lại 403.

**Lỗi:**
- `403` friend list private và currentUser không phải bạn.

---

## PUT /api/v1/friends/privacy

Cập nhật privacy settings.

**Body:**
```json
{
  "isFriendListPublic": true,
  "acceptFriendRequestsFrom": "Everyone",
  "friendLimit": 0
}
```

| Field | Allowed |
|-------|---------|
| `acceptFriendRequestsFrom` | `Everyone`, `FriendsOfFriends` |
| `friendLimit` | 0 (không giới hạn) → 5000 |

---

## Friend Notes (`/notes`)

### GET /api/v1/friends/notes

Lấy tất cả ghi chú của current user.

**Response 200:** `FriendNoteDto[]` — `noteId`, `friendUserId`, `friendUsername`, `alias`, `note`, `tags`, `createdAt`, `updatedAt`.

### PUT /api/v1/friends/notes/{friendUserId}

Tạo mới hoặc cập nhật ghi chú.

**Body:**
```json
{
  "alias": "Anh Cường - Catan",
  "note": "Chơi Catan tốt, thích chơi tối.",
  "tags": "Catan,Wingman"
}
```

| Field | Required | Max |
|-------|----------|-----|
| `alias` | ✅ | 100 |
| `note` | ❌ | 1000 |
| `tags` | ❌ | 200 |

### DELETE /api/v1/friends/notes/{noteId}

Xóa ghi chú. Chỉ chủ sở hữu.

---

## Friend Reports (`/reports`)

### POST /api/v1/friends/reports

Báo cáo một user vi phạm. **Chỉ báo cáo được user đang là bạn bè (Accepted)** (BR-FRIEND-REPORT-01).

**Body:**
```json
{
  "targetUserId": "<guid>",
  "category": "Harassment",
  "reason": "Spam tin nhắn trong 3 ngày liên tục."
}
```

| Field | Allowed |
|-------|---------|
| `category` | `Spam`, `Harassment`, `FakeAccount`, `InappropriateContent`, `Other` |
| `reason` | 5-1000 ký tự |

**Lỗi:**
- `400` không phải bạn bè / không thể report chính mình.
- `403` không thể report Admin.
- `409` đã có report Pending cho target.

### GET /api/v1/friends/reports

Lấy danh sách report của current user.

---

## BR liên quan

| BR | Áp dụng |
|----|---------|
| BR-FRIEND-01 | Unique (RequesterId, AddresseeId) — không thể gửi 2 lời mời trùng. |
| BR-FRIEND-02 | User bị block không thể gửi friend request hoặc lobby invite. |
| BR-FRIEND-03 | Status chuyển: `Pending` → `Accepted`/`Removed`/`Blocked`; `Accepted` → `Removed`; `Blocked` → `Removed`. |
| BR-FRIEND-04 | FriendRequest có Message tối đa 200 ký tự. |
| BR-FRIEND-05 | Tự động expire sau FriendRequestExpiryDays (mặc định 30 ngày). |
| BR-FRIEND-BUG-01 | Accept phải check cả 2 user còn active. |
| BR-FRIEND-BUG-02 | Accept phải check block ngược chiều. |
| BR-FRIEND-RATE-01 | Tối đa 20 lời mời/giờ/requestor. |
| BR-FRIEND-CAP-01 | Addressee có `FriendLimit > 0` → không gửi nếu sẽ vượt. |
| BR-FRIEND-CAP-02 | Accept check FriendLimit cho cả 2 bên. |
| BR-FRIEND-CASCADE-01 | Unfriend → tự động hủy lobby invite Pending giữa 2 bên. |
| BR-FRIEND-NOTE-01 | Unique (OwnerUserId, FriendUserId) cho FriendNote. |
| BR-FRIEND-NOTE-02 | Chỉ chủ sở hữu mới đọc/sửa/xóa. |
| BR-FRIEND-REPORT-01 | Reporter phải từng có quan hệ Accepted với Target. |
| BR-FRIEND-REPORT-02 | 1 (ReporterId, TargetId) chỉ có 1 Pending report. |
| BR-FRIEND-REPORT-03 | Không báo cáo chính mình / Admin. |
| BR-FRIEND-SUGGEST-01 | Gợi ý từ friends-of-friends. |
| BR-FRIEND-SUGGEST-02 | Gợi ý từ người cùng lobby trong 30 ngày. |
| BR-LOBBY-INVITE-04 | Private lobby chỉ mời được bạn bè Accepted. |
| BR-LOBBY-INVITE-NEW-01 | SendInvite check friendship cho private lobby. |
| BR-LOBBY-INVITE-NEW-02 | AcceptInvite check friendship (tránh stale invite sau unfriend). |

## Liên quan

- **Lobby invite:** dùng friend list để mời vào lobby — [lobby.md](./lobby.md).
- **Profile:** `UserProfileController.GetByUserId` lấy chi tiết user.
- **Background Job:** `FriendRequestExpiryJob` expire Pending sau 30 ngày, mỗi giờ chạy 1 lần.
