# FriendController

**Base route:** `/api/v1/friends`
**Controller:** `FriendController.cs`
**Role:** Player — đã đăng nhập

API quản lý quan hệ bạn bè: gửi lời mời, accept/decline, block, search user, danh sách bạn bè, mutual friends, suggestions, privacy, note, report. Dùng cho Player muốn kết nối trước khi tạo lobby chung.

## Endpoints

| Endpoint | Method | Role | Mô tả |
|----------|--------|------|-------|
| `/requests` | POST | Player | Gửi lời mời kết bạn |
| `/requests/{id}` | DELETE | Player (requester) | Hủy lời mời đã gửi (chỉ Pending) |
| `/requests/{id}` | GET | Player (requester/addressee) | Xem chi tiết 1 lời mời (cho notification deeplink) |
| `/requests/{id}/accept` | POST | Player (addressee) | Accept lời mời |
| `/requests/{id}/decline` | POST | Player (addressee) | Từ chối lời mời |
| `/requests/{id}/read` | POST | Player (addressee) | Đánh dấu đã đọc lời mời (inbox) |
| `/{id}` | DELETE | Player (1 trong 2 bên) | Hủy kết bạn / xóa quan hệ (auto-cancel lobby invite Pending) |
| `/block/{userId}` | POST | Player | Chặn user |
| `/block/{userId}` | DELETE | Player (người đã chặn) | Bỏ chặn user |
| `/blocked` | GET | Player | Danh sách user mà current user đã chặn |
| `/blocked-by` | GET | Player | Danh sách user đã chặn current user (debug/UX) |
| `/` | GET | Player | Danh sách bạn bè (Accepted) |
| `/by-direction?direction=IncomingRequest` | GET | Player | Lọc quan hệ theo direction từ góc nhìn current user (BR-FRIEND-UI-DIRECTION-01) |
| `/activity` | GET | Player | Friend list + activity status |
| `/requests/received` | GET | Player | Inbox: lời mời đến (Pending) |
| `/requests/sent` | GET | Player | Outbox: lời mời đã gửi (Pending) |
| `/search?q=&limit=` | GET | Player | Tìm user theo username + FriendshipStatus + MutualFriendsCount |
| `/suggestions?limit=` | GET | Player | Gợi ý kết bạn (friends-of-friends + same lobby) |
| `/{otherUserId}/mutual` | GET | Player | Bạn chung giữa current user và otherUser |
| `/{userId}/profile` | GET | Player | Xem chi tiết public profile của 1 player (kèm quan hệ + mutual + permission flags) |
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

**DB note (BR-FRIEND-BLOCK-VIEW):**
- Khi chặn, hệ thống set `Friendship.Status = Blocked` và `BlockerUserId = <người đã chặn>`.
- `BlockerUserId` cho phép tra ngược ai là người chặn, vì 1 cặp (Requester, Addressee) chỉ có 1 record và cả 2 user đều có thể chặn nhau (block lẫn nhau tạo ra nhiều record khác nhau, không phải 2 chiều trên cùng record).
- Cột `BlockerUserId` nullable (uuid), có partial index filter `"BlockerUserId" IS NOT NULL` để thống kê.
- Khi bỏ chặn (`DELETE /api/v1/friends/block/{userId}`), quan hệ chuyển `Removed` và `BlockerUserId` được giữ nguyên (audit trail).

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

## DELETE /api/v1/friends/requests/{id}

Hủy lời mời kết bạn đã gửi (khi gửi nhầm hoặc đổi ý trước khi addressee phản hồi).

**Lỗi:**
- 403 — không phải người gửi.
- 404 — không tìm thấy.
- 409 — lời mời không còn ở trạng thái Pending (đã accept/decline → dùng `DELETE /friends/{id}` để xóa quan hệ, hoặc block).

**Lưu ý:** sau khi hủy, record chuyển `Status = Removed` nhưng vẫn giữ trong DB để audit/inbox cleanup.

---

## GET /api/v1/friends/requests/{id}

Xem chi tiết 1 lời mời kết bạn. Dùng cho notification deeplink (user tap vào push notification → mở chi tiết).

**Response 200:** `FriendshipResponseDto` — đầy đủ sender/receiver info, `message`, `status`, `addresseeReadAt`, timestamps.

**Lỗi:**
- 403 — không phải một bên của quan hệ HOẶC status = Blocked và mình bị chặn (ẩn thông tin người chặn mình).
- 404 — không tìm thấy.

---

## GET /api/v1/friends/blocked

Danh sách user mà current user đã chặn (chỉ mình thấy mình chặn ai).

**Response 200:** `FriendshipResponseDto[]` — mỗi entry kèm `otherUserId`, `username`, `avatarUrl`, `blockedAt = UpdatedAt`.

**Use case UI:** tab "Privacy / Blocked users" trong Settings, cho user bỏ chặn 1 lần.

---

## GET /api/v1/friends/blocked-by

Danh sách user đã chặn current user.

**Response 200:** `FriendshipResponseDto[]`.

**Use case UI:** hiển thị trong trang profile hoặc sau action fail (vd. gửi friend request fail vì lý do `BlockedByOtherParty`) — explain tại sao action không thành công.

> ⚠️ FE nên handle gracefully: chỉ hiển thị "Bạn không thể tương tác với user này" thay vì list cụ thể, để tránh tiết lộ identity của người chặn nếu user không muốn.

---

## GET /api/v1/friends/search

Tìm user theo username (case-insensitive contains). Kết quả kèm `FriendshipStatus` + `MutualFriendsCount` để UI biết có thể gửi request không.

**Query params:**
- `q` (string, ≥ 2 ký tự) — từ khóa.
- `limit` (int, 1-50, mặc định 20).

**Response 200:** `UserSearchResultDto[]` — `userId`, `username`, `avatarUrl`, `karmaPoints`, `friendshipStatus`, `mutualFriendsCount`, `relationshipDirection`.

**`relationshipDirection` (BR-FRIEND-UI-DIRECTION-01):**
Trả về quan hệ từ **góc nhìn current user**, không phải raw DB status. UI render theo field này:

| Direction | Điều kiện | UI render |
|-----------|-----------|-----------|
| `None` | Không có quan hệ hoặc `Removed` | "Gửi lời mời kết bạn" |
| `OutgoingRequest` | `Status = Pending` và current user là **requester** | "Đã gửi lời mời" (disable) |
| `IncomingRequest` | `Status = Pending` và current user là **addressee** | "Chấp nhận / Từ chối" |
| `Accepted` | `Status = Accepted` | "Bạn bè" + nút nhắn / mời lobby |
| `BlockedByMe` | `Status = Blocked` và `BlockerUserId = currentUser` | "Bỏ chặn" |
| `BlockedByThem` | `Status = Blocked` và `BlockerUserId = otherUser` | Ẩn hoặc disable mọi action |

> ⚠️ Trước đây UI chỉ đọc `friendshipStatus = "Pending"` nên Jonny search player5 thấy "đã gửi lời mời" và player5 search Jonny cũng thấy "đã gửi lời mời" — **sai**, vì cùng `Status = Pending` nhưng phía player5 là addressee, phải thấy "Chấp nhận / Từ chối". Field `relationshipDirection` fix bug này.

---

## GET /api/v1/friends/suggestions

Gợi ý kết bạn:
- **MutualFriends**: bạn của bạn chưa kết bạn với bạn.
- **SameLobbyRecent**: người cùng chơi trong lobby trong 30 ngày gần đây (weight cao hơn).

**Query params:**
- `limit` (int, 1-50, mặc định 20).

**Response 200:** `FriendSuggestionDto[]` — `userId`, `username`, `avatarUrl`, `karmaPoints`, `gamerTier`, `mutualFriendsCount`, `reason`.

---

## GET /api/v1/friends/by-direction?direction=IncomingRequest&limit=50

Lấy danh sách quan hệ bạn bè lọc theo `direction` từ góc nhìn current user (BR-FRIEND-UI-DIRECTION-01).

**Mục đích:** thay vì FE tự gọi 4-5 endpoint riêng (`/requests/received`, `/requests/sent`, `/`, `/block/...`) rồi tự filter, FE có thể gọi 1 endpoint duy nhất với direction enum.

**Query params:**
- `direction` (enum **required**): một trong `IncomingRequest`, `OutgoingRequest`, `Accepted`, `BlockedByMe`, `BlockedByThem`.
  - `None` không hợp lệ cho endpoint này (vì "chưa có quan hệ" không có row để query) — trả 200 mảng rỗng nhưng nên tránh dùng.
- `limit` (int, 1-100, mặc định 50).

**Response 200:** `FriendshipResponseDto[]` — cùng schema với `/requests/received`.

**Mapping direction → query (BR-FRIEND-UI-DIRECTION-01):**

| Direction | SQL filter |
|-----------|-----------|
| `IncomingRequest` | `Status = Pending AND AddresseeId = currentUser` |
| `OutgoingRequest` | `Status = Pending AND RequesterId = currentUser` |
| `Accepted` | `Status = Accepted` (cả 2 phía) |
| `BlockedByMe` | `Status = Blocked AND BlockerUserId = currentUser` |
| `BlockedByThem` | `Status = Blocked AND BlockerUserId != currentUser` |

**Response 400:** Direction không hợp lệ (chuỗi không nằm trong enum).

**Ví dụ:**
```
GET /api/v1/friends/by-direction?direction=IncomingRequest&limit=20
→ Trả về các lời mời đang chờ mà current user NHẬN ĐƯỢC.

GET /api/v1/friends/by-direction?direction=OutgoingRequest
→ Trả về các lời mời current user đã gửi đi nhưng chưa phản hồi.

GET /api/v1/friends/by-direction?direction=Accepted
→ Tương đương GET /api/v1/friends (chỉ khác là query trực tiếp theo Status).
```

---

## GET /api/v1/friends/{otherUserId}/mutual

Bạn chung giữa currentUser và otherUser.

**Response 200:** `MutualFriendDto[]` — `userId`, `username`, `avatarUrl`, `friendsSince`.

---

## GET /api/v1/friends/{userId}/profile

Xem chi tiết public profile của 1 player. Kết hợp thông tin user (gamer stats, activity) với ngữ cảnh quan hệ giữa current user và target để FE render đúng nút hành động.

**Path param:** `userId` (Guid) — mã player cần xem.

**Response 200:** `PlayerProfileDto` gồm:
- Thông tin cơ bản: `userId`, `username`, `avatarUrl`, `avatarBorderUrl`, `bio`, `firstName`, `lastName`.
- Gamer stats: `globalElo`, `karmaPoints`, `gamerTier`, `level`.
- Social stats: `friendsCount` (chỉ count, không trả list — tôn trọng `IsFriendListPublic`), `mutualFriendsCount`.
- Activity: `activityStatus` (`Online` / `RecentlyActive` / `Away` / `Offline`), `lastActiveAt`.
- `joinedAt`.
- `relationship` (`RelationshipDto`): `status` (`None` / `PendingSent` / `PendingReceived` / `Accepted` / `BlockedByMe` / `BlockedByThem`), `friendshipId`, `isRequester`, `friendsSince`, `message`.
- Permission flags: `canSendFriendRequest`, `canReport`.

**Quy tắc:**
- `canReport = true` chỉ khi `relationship.status = Accepted` (BR-FRIEND-REPORT-01).
- Ẩn hoàn toàn (`404`) nếu target bị block 2 chiều hoặc tài khoản không `Active` — tránh để lộ identity người đã chặn mình.

**Lỗi:**
- `400` `userId` trùng với current user.
- `401` thiếu token.
- `404` không tìm thấy / account không active / bị block 2 chiều.
- `500` lỗi hệ thống.

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
| BR-FRIEND-BLOCK-VIEW | Lưu `BlockerUserId` để biết ai là người chặn khi `Status = Blocked` (cả 2 user có thể block nhau trong cùng 1 record). Null khi `Status != Blocked`. |
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
| BR-FRIEND-UI-DIRECTION-01 | Search/suggestions/list phải trả `RelationshipDirection` tính từ góc nhìn current user (RequesterId vs AddresseeId), không chỉ raw DB status. |
| BR-FRIEND-SEARCH-BLOCK-FILTER | `SearchUsersAsync` và `GetFriendSuggestionsAsync` phải loại bỏ user đã chặn (cả 2 chiều) khỏi kết quả — tránh gợi ý/search user không thể tương tác. |
| BR-FRIEND-CANCEL-01 | `CancelFriendRequestAsync` chỉ cho phép requester hủy và chỉ khi `Status = Pending`. Record chuyển `Removed` để audit. |
| BR-FRIEND-DETAIL-VISIBILITY | `GetFriendRequestByIdAsync` ẩn thông tin quan hệ `Status = Blocked` cho phía bị chặn (chỉ BlockerUserId được xem chi tiết). |
| BR-LOBBY-INVITE-04 | Private lobby chỉ mời được bạn bè Accepted. |
| BR-LOBBY-INVITE-NEW-01 | SendInvite check friendship cho private lobby. |
| BR-LOBBY-INVITE-NEW-02 | AcceptInvite check friendship (tránh stale invite sau unfriend). |

## Liên quan

- **Lobby invite:** dùng friend list để mời vào lobby — [lobby.md](./lobby.md).
- **Profile:** `UserProfileController.GetByUserId` lấy chi tiết user.
- **Background Job:** `FriendRequestExpiryJob` expire Pending sau 30 ngày, mỗi giờ chạy 1 lần.
