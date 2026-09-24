# API: Solo Personalized Discovery

## Tổng quan

API gợi ý board game **cá nhân hóa** dựa trên:
- **Saved games** của user (ít nhất 3 games để personalization hoạt động)
- **Play history** (tần suất chơi, game chơi gần đây) — *sẽ tích hợp trong Cải tiến 3*

Điểm khác với `/survey`:
- `/survey`: gợi ý generic, áp dụng cho anonymous user hoặc user chưa có preference profile
- `/solo-personalized`: gợi ý dựa trên **hành vi cá nhân** (categories thích, weight/duration trung bình, play history)

---

## Endpoint

```
POST /api/v1/discovery/solo-personalized
```

**Authentication:** Required (Bearer token)

**Role:** Player (user đã đăng nhập)

---

## Request

### Headers

```
Authorization: Bearer <access_token>
Content-Type: application/json
```

### Query Parameters

| Parameter | Type | Required | Mô tả |
|---|---|---|---|
| `latitude` | `number` | No | Vĩ độ GPS (WGS84) để tìm cafe gần |
| `longitude` | `number` | No | Kinh độ GPS (WGS84) |

### Request Body

```json
{
  "categoryIds": ["uuid1", "uuid2"],
  "preferredDurations": ["under30", "30to60", "over60"],
  "weightRanges": [1, 2, 3, 4, 5],
  "playerCount": 4,
  "searchKeyword": "catan",
  "pageSize": 20,
  "excludeSavedGames": false
}
```

#### Body Schema

| Field | Type | Required | Mô tả |
|---|---|---|---|
| `categoryIds` | `string[]` (UUID) | No | Filter theo thể loại. UNION logic: game khớp ít nhất 1 category. |
| `preferredDurations` | `string[]` | No | Filter theo thời gian chơi. Giá trị hợp lệ: `under30`, `30to60`, `over60`. UNION logic. |
| `weightRanges` | `number[]` | No | Filter theo độ phức tạp BGG Weight. Giá trị hợp lệ: `1` (Light ≤2.0), `2` (Medium-Light 2.01–3.0), `3` (Medium 3.01–3.5), `4` (Medium-Heavy 3.51–4.0), `5` (Heavy >4.0). UNION logic. |
| `playerCount` | `number` | No | Số người chơi (1-5+). Nếu `null` → không filter theo player count. |
| `searchKeyword` | `string` | No | Tìm kiếm theo tên game hoặc description. |
| `pageSize` | `number` | No | Số lượng games trả về tối đa. Default: `20`. |
| `excludeSavedGames` | `boolean` | No | `true`: chỉ gợi ý games chưa lưu. `false` (default): gợi ý tất cả (bao gồm cả games đã lưu). |

---

## Response

### Success (200 OK)

```json
{
  "statusCode": 200,
  "message": "Gợi ý cá nhân hóa hoàn tất dựa trên sở thích của bạn.",
  "data": {
    "games": [
      {
        "id": "550e8400-e29b-41d4-a716-446655440000",
        "name": "Catan",
        "thumbnailUrl": "https://example.com/catan.jpg",
        "description": "Game chiến thuật xây dựng đảo",
        "minPlayers": 3,
        "maxPlayers": 4,
        "playTimeMinutes": 60,
        "weight": 2.3,
        "categories": ["Chiến thuật", "Kinh tế"],
        "baseScore": 75.0,
        "personalizationBoost": 12.0,
        "playHistoryPenalty": 0.0,
        "personalizedScore": 87.0,
        "matchReason": "Khớp với thể loại bạn thường chơi",
        "isSaved": true,
        "hasOpenLobby": false
      },
      {
        "id": "660e8400-e29b-41d4-a716-446655440001",
        "name": "Ticket to Ride",
        "thumbnailUrl": "https://example.com/ttr.jpg",
        "description": "Game xây tuyến đường sắt",
        "minPlayers": 2,
        "maxPlayers": 5,
        "playTimeMinutes": 45,
        "weight": 1.9,
        "categories": ["Gia đình", "Chiến thuật"],
        "baseScore": 70.0,
        "personalizationBoost": 8.0,
        "playHistoryPenalty": -5.0,
        "personalizedScore": 73.0,
        "matchReason": "Độ phức tạp phù hợp với sở thích của bạn",
        "isSaved": false,
        "hasOpenLobby": true
      }
    ],
    "userProfile": {
      "userId": "123e4567-e89b-12d3-a456-426614174000",
      "topCategoryIds": [
        "cat-1-uuid",
        "cat-2-uuid",
        "cat-3-uuid"
      ],
      "averageWeight": 2.5,
      "averageDuration": 55.0,
      "savedGameCount": 12
    },
    "totalCount": 48,
    "nearestCafe": {
      "cafeId": "cafe-uuid",
      "cafeName": "BoardVerse Cafe",
      "cafeAddress": "123 Nguyễn Huệ, Q1, TP.HCM",
      "distanceKm": 2.5,
      "latitude": 10.7769,
      "longitude": 106.7009,
      "availableGamesCount": 120,
      "hasOpenLobby": false
    },
    "openLobbies": [
      {
        "lobbyId": "lobby-uuid",
        "gameTemplateId": "660e8400-e29b-41d4-a716-446655440001",
        "gameName": "Ticket to Ride",
        "currentMembers": 2,
        "maxMembers": 4,
        "playDate": "2026-09-24",
        "startTime": "19:00:00",
        "cafeId": "cafe-uuid",
        "cafeName": "BoardVerse Cafe",
        "isOpen": true
      }
    ]
  }
}
```

#### Response Schema

**Root**

| Field | Type | Mô tả |
|---|---|---|
| `statusCode` | `number` | HTTP status code (200) |
| `message` | `string` | Success message |
| `data` | `object` | Response payload |

**`data` object**

| Field | Type | Mô tả |
|---|---|---|
| `games` | `PersonalizedBoardGame[]` | Danh sách games gợi ý, sắp xếp theo `personalizedScore` giảm dần |
| `userProfile` | `UserGamePreference` hoặc `null` | Profile sở thích user. `null` nếu user có <3 saved games. |
| `totalCount` | `number` | Tổng số games tìm thấy (trước khi áp dụng `pageSize`) |
| `nearestCafe` | `NearbyCafeForGame` hoặc `null` | Quán gần nhất có game top 1 (nếu có `latitude`, `longitude`) |
| `openLobbies` | `OpenLobbySummary[]` | Lobbies đang mở cho game top 1 (tối đa 5) |

**`PersonalizedBoardGame` object**

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` (UUID) | Game template ID |
| `name` | `string` | Tên game |
| `thumbnailUrl` | `string` hoặc `null` | URL ảnh thumbnail |
| `description` | `string` hoặc `null` | Mô tả game |
| `minPlayers` | `number` | Số người chơi tối thiểu |
| `maxPlayers` | `number` | Số người chơi tối đa |
| `playTimeMinutes` | `number` | Thời gian chơi (phút) |
| `weight` | `number` hoặc `null` | BGG Weight (1.0 – 5.0) |
| `categories` | `string[]` | Danh sách tên thể loại |
| `baseScore` | `number` | Điểm cơ bản dựa trên filters (0–85) |
| `personalizationBoost` | `number` | Điểm cộng từ saved games (0–15) |
| `playHistoryPenalty` | `number` | Điểm trừ từ play history (-10 – 0) — *hiện tại luôn 0, sẽ tích hợp trong Cải tiến 3* |
| `personalizedScore` | `number` | Điểm tổng = `baseScore + personalizationBoost + playHistoryPenalty` |
| `matchReason` | `string` hoặc `null` | Lý do match chính (1 câu ngắn) |
| `isSaved` | `boolean` | User đã lưu game này chưa |
| `hasOpenLobby` | `boolean` | Có lobby đang mở cho game này không |

**`UserGamePreference` object**

| Field | Type | Mô tả |
|---|---|---|
| `userId` | `string` (UUID) | User ID |
| `topCategoryIds` | `string[]` (UUID) | Top 3 thể loại user thường lưu |
| `averageWeight` | `number` hoặc `null` | Weight trung bình của saved games. `null` nếu không có game nào có weight data. |
| `averageDuration` | `number` | Thời gian chơi trung bình (phút) |
| `savedGameCount` | `number` | Tổng số games đã lưu |

**`NearbyCafeForGame` object**

| Field | Type | Mô tả |
|---|---|---|
| `cafeId` | `string` (UUID) | Cafe ID |
| `cafeName` | `string` | Tên quán |
| `cafeAddress` | `string` | Địa chỉ |
| `distanceKm` | `number` | Khoảng cách (km) |
| `latitude` | `number` | Vĩ độ quán |
| `longitude` | `number` | Kinh độ quán |
| `availableGamesCount` | `number` | Số lượng games có trong kho quán |
| `hasOpenLobby` | `boolean` | Quán có lobby đang mở không |

**`OpenLobbySummary` object**

| Field | Type | Mô tả |
|---|---|---|
| `lobbyId` | `string` (UUID) | Lobby ID |
| `gameTemplateId` | `string` (UUID) | Game template ID |
| `gameName` | `string` | Tên game |
| `currentMembers` | `number` | Số người hiện tại |
| `maxMembers` | `number` | Số người tối đa |
| `playDate` | `string` (date) hoặc `null` | Ngày chơi (YYYY-MM-DD) |
| `startTime` | `string` (time) hoặc `null` | Giờ bắt đầu (HH:mm:ss) |
| `cafeId` | `string` (UUID) hoặc `null` | Cafe ID |
| `cafeName` | `string` hoặc `null` | Tên quán |
| `isOpen` | `boolean` | Lobby có đang mở không |

---

### Error Responses

#### 400 Bad Request

**Trường hợp 1: PreferredDurations không hợp lệ**

```json
{
  "statusCode": 400,
  "message": "Giá trị PreferredDurations không hợp lệ: 'under_60'. Chỉ chấp nhận: under30, 30to60, over60.",
  "data": null
}
```

**Trường hợp 2: Validation error khác**

```json
{
  "statusCode": 400,
  "message": "Request validation failed for '/api/v1/discovery/solo-personalized': ...",
  "data": null
}
```

#### 401 Unauthorized

```json
{
  "statusCode": 401,
  "message": "Token không hợp lệ hoặc đã hết hạn.",
  "data": null
}
```

#### 500 Internal Server Error

```json
{
  "statusCode": 500,
  "message": "Đã xảy ra lỗi không mong đợi trên máy chủ.",
  "data": null
}
```

---

## Scoring Logic

### Base Score (0–85 pts)

Điểm cơ bản **không** dựa trên saved games, chỉ dựa trên filters trong request:

| Tiêu chí | Điểm tối đa |
|---|---|
| Category match | 40 pts |
| Duration match | 30 pts |
| Player range fit | 15 pts |
| **Tổng** | **85 pts** |

### Personalization Boost (0–15 pts)

Điểm cộng dựa trên **saved games** của user (yêu cầu ≥3 saved games):

| Tiêu chí | Điểm tối đa |
|---|---|
| Category affinity (game thuộc top 3 categories user thích) | 8 pts |
| Weight affinity (weight game gần weight trung bình user) | 4 pts |
| Duration affinity (duration game gần duration trung bình user) | 3 pts |
| **Tổng** | **15 pts** |

**Nếu user có <3 saved games:** `personalizationBoost = 0`, `userProfile = null`.

### Play History Penalty (-10 – 0 pts)

*Tính năng này sẽ được tích hợp trong **Cải tiến 3: Play History Influence***

Penalty dựa trên:
- **Frequency:** game user chơi quá nhiều lần → giảm điểm (tránh nhàm chán)
- **Recency:** game user chơi gần đây → giảm priority (khuyến khích thử game mới)

Hiện tại: `playHistoryPenalty = 0` (chưa triển khai).

### Personalized Score (Final)

```
personalizedScore = baseScore + personalizationBoost + playHistoryPenalty
```

Cap tối đa: **100 pts**.

Games sắp xếp theo `personalizedScore` giảm dần → `name` A-Z.

---

## Use Cases

### 1. User mới (chưa có saved games)

**Request:**
```json
{
  "categoryIds": ["cat-chien-thuat"],
  "playerCount": 3,
  "pageSize": 10
}
```

**Response:**
- `userProfile = null` (chưa đủ 3 saved games)
- `personalizationBoost = 0` cho tất cả games
- Kết quả tương tự `/survey` (generic recommendation)

### 2. User có profile (≥3 saved games)

**Scenario:** User đã lưu 12 games, chủ yếu thể loại **Chiến thuật** và **Kinh tế**, weight trung bình 2.5.

**Request:**
```json
{
  "playerCount": 4,
  "excludeSavedGames": true,
  "pageSize": 20
}
```

**Response:**
- `userProfile`:
  ```json
  {
    "topCategoryIds": ["cat-chien-thuat", "cat-kinh-te", "cat-phat-trien"],
    "averageWeight": 2.5,
    "averageDuration": 55.0,
    "savedGameCount": 12
  }
  ```
- Games thuộc **Chiến thuật** / **Kinh tế** nhận `+8 pts` (category affinity)
- Games có weight 2.0–3.0 nhận `+2` đến `+4 pts` (weight affinity)
- Games đã lưu bị loại ra (do `excludeSavedGames: true`)

### 3. Tìm game mới cho buổi chơi ngắn

**Request:**
```json
{
  "preferredDurations": ["under30"],
  "playerCount": 2,
  "excludeSavedGames": true,
  "latitude": 10.7769,
  "longitude": 106.7009
}
```

**Response:**
- Games ≤30 phút nhận `+30 pts` (duration match)
- Games thuộc top categories user → `+8 pts` (nếu có profile)
- `nearestCafe`: quán gần nhất có game top 1
- `openLobbies`: lobbies đang mở cho game top 1

---

## TypeScript Types (Frontend)

```typescript
// ========== Request ==========

interface SoloPersonalizedRequest {
  categoryIds?: string[]; // UUID[]
  preferredDurations?: ('under30' | '30to60' | 'over60')[];
  weightRanges?: (1 | 2 | 3 | 4 | 5)[]; // 1=Light, 2=MediumLight, 3=Medium, 4=MediumHeavy, 5=Heavy
  playerCount?: number; // 1-5+
  searchKeyword?: string;
  pageSize?: number; // default 20
  excludeSavedGames?: boolean; // default false
}

// ========== Response ==========

interface SoloPersonalizedResponse {
  statusCode: number;
  message: string;
  data: {
    games: PersonalizedBoardGame[];
    userProfile: UserGamePreference | null;
    totalCount: number;
    nearestCafe: NearbyCafeForGame | null;
    openLobbies: OpenLobbySummary[];
  };
}

interface PersonalizedBoardGame {
  id: string; // UUID
  name: string;
  thumbnailUrl: string | null;
  description: string | null;
  minPlayers: number;
  maxPlayers: number;
  playTimeMinutes: number;
  weight: number | null; // 1.0 - 5.0
  categories: string[];
  baseScore: number; // 0-85
  personalizationBoost: number; // 0-15
  playHistoryPenalty: number; // -10 - 0 (hiện tại luôn 0)
  personalizedScore: number; // final score (0-100)
  matchReason: string | null;
  isSaved: boolean;
  hasOpenLobby: boolean;
}

interface UserGamePreference {
  userId: string; // UUID
  topCategoryIds: string[]; // top 3 categories (UUID[])
  averageWeight: number | null; // average BGG weight (1.0-5.0)
  averageDuration: number; // average playtime (minutes)
  savedGameCount: number;
}

interface NearbyCafeForGame {
  cafeId: string; // UUID
  cafeName: string;
  cafeAddress: string;
  distanceKm: number;
  latitude: number;
  longitude: number;
  availableGamesCount: number;
  hasOpenLobby: boolean;
}

interface OpenLobbySummary {
  lobbyId: string; // UUID
  gameTemplateId: string; // UUID
  gameName: string;
  currentMembers: number;
  maxMembers: number;
  playDate: string | null; // YYYY-MM-DD
  startTime: string | null; // HH:mm:ss
  cafeId: string | null; // UUID
  cafeName: string | null;
  isOpen: boolean;
}
```

---

## Base URL

| Môi trường | URL |
|---|---|
| **Development** | `http://localhost:5000` |
| **Staging** | `https://staging-api.boardverse.com` *(chưa có)* |
| **Production** | `https://api.boardverse.com` *(chưa có)* |

---

## Testing

### Postman / Thunder Client

**Request:**

```
POST {{baseUrl}}/api/v1/discovery/solo-personalized?latitude=10.7769&longitude=106.7009
Authorization: Bearer {{access_token}}
Content-Type: application/json

{
  "categoryIds": ["cat-uuid-1"],
  "preferredDurations": ["30to60"],
  "weightRanges": [2, 3],
  "playerCount": 4,
  "pageSize": 10,
  "excludeSavedGames": false
}
```

**Expected Response:** 200 OK với `data.games[]` sorted by `personalizedScore` DESC.

---

## FAQ

### 1. Khi nào nên dùng `/solo-personalized` thay vì `/survey`?

| Tình huống | Endpoint |
|---|---|
| Anonymous user (chưa đăng nhập) | `/survey` |
| User mới (chưa có saved games) | `/survey` hoặc `/solo-personalized` (kết quả tương tự) |
| User có ≥3 saved games | **`/solo-personalized`** (có personalization boost) |
| Quick discovery không cần personalization | `/survey` |

### 2. `userProfile` trả về `null` nghĩa là gì?

User có **< 3 saved games** → không đủ dữ liệu để xây dựng preference profile.

Trong trường hợp này:
- `personalizationBoost = 0` cho tất cả games
- Kết quả gợi ý tương tự `/survey`

### 3. Làm sao để user có profile?

User cần **lưu ít nhất 3 games** qua endpoint:

```
POST /api/v1/discovery/saved/{gameTemplateId}
```

Sau khi lưu ≥3 games, gọi lại `/solo-personalized` sẽ có `userProfile` và personalization boost.

### 4. `playHistoryPenalty` luôn bằng 0?

Đúng vậy. Penalty dựa trên `GameSession` data sẽ được triển khai trong **Cải tiến 3: Play History Influence**.

Hiện tại field này luôn `0` (reserved for future use).

### 5. Filter `weightRanges` hoạt động thế nào?

**UNION logic:** game chỉ cần nằm trong **1 trong các ranges** đã chọn.

Ví dụ:
```json
"weightRanges": [1, 2]  // Light (≤2.0) HOẶC MediumLight (2.01-3.0)
```

Game có `weight = 2.5` → **match** (nằm trong range 2).

Game có `weight = 3.5` → **không match** (không nằm trong range 1 hay 2).

### 6. `excludeSavedGames` có ảnh hưởng gì?

- `false` (default): gợi ý **tất cả games** (bao gồm cả games đã lưu)
- `true`: chỉ gợi ý **games chưa lưu** (filter ra games có `isSaved = true`)

Use case: user muốn khám phá games **mới hoàn toàn**, không muốn thấy games đã lưu.

---

## Changelog

| Version | Date | Changes |
|---|---|---|
| 1.0 | 2026-09-23 | Initial release — Cải tiến 2: Solo Personalized Discovery |

---

## Related APIs

- `POST /api/v1/discovery/survey` — Generic survey (không cần đăng nhập)
- `POST /api/v1/discovery/group` — Group discovery (AWM algorithm)
- `GET /api/v1/discovery/saved` — Lấy danh sách saved games
- `POST /api/v1/discovery/saved/{gameTemplateId}` — Toggle save game
- `DELETE /api/v1/discovery/saved/{gameTemplateId}` — Unsave game
