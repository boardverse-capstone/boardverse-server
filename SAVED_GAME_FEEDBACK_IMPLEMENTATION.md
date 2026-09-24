# SavedBoardGame Feedback Loop Implementation

## ✅ Hoàn thành: 2026-01-23

## 🎯 Tổng quan

Cải tiến 5 sử dụng **lịch sử saved games** của user để **personalize recommendations**. Khi user đã save một số game, hệ thống sẽ:

1. **Phân tích sở thích** từ saved games (categories, weight, duration)
2. **Boost score** (+15 pts) cho game có đặc điểm tương tự
3. **Áp dụng cho cả Solo Survey và Group Discovery**

---

## 📊 Kiến trúc

### Flow Chart

```
User gọi Discovery API (với userId)
    ↓
Service extract preferences từ SavedGames table
    ↓
    ├─→ Nếu có saved games → tạo UserGamePreference DTO
    │   ├─→ FavoriteCategoryIds (top 3)
    │   ├─→ PreferredWeightRange (median ± 0.5)
    │   └─→ PreferredDuration (average play time)
    │
    └─→ Nếu chưa có saved games → null (skip personalization)
    ↓
Scoring: CalculatePreferenceBoost(game, userPref)
    ↓
    ├─→ Category overlap: +8 pts (max)
    ├─→ Weight match: +5 pts
    └─→ Duration match: +2 pts
    ↓
Response: Games sorted by total score
```

---

## 🔧 Implementation Details

### 1. DTO: `UserGamePreference`

**File:** `BoardVerse.Core/DTOs/Discovery/UserGamePreference.cs`

```csharp
/// <summary>
/// User's inferred preferences from saved games history.
/// </summary>
public class UserGamePreference
{
    /// <summary>
    /// Top 3 favorite categories (by frequency in saved games).
    /// </summary>
    public List<Guid> FavoriteCategoryIds { get; set; } = [];

    /// <summary>
    /// Preferred weight range (median weight ± 0.5).
    /// Example: If user saved games with weights [2.1, 2.5, 2.8] → range is [2.0, 3.0]
    /// </summary>
    public (double Min, double Max) PreferredWeightRange { get; set; }

    /// <summary>
    /// Preferred duration range (average play time ± 15 min).
    /// </summary>
    public (int Min, int Max) PreferredDurationMinutes { get; set; }

    /// <summary>
    /// Number of saved games used for analysis.
    /// </summary>
    public int SampleSize { get; set; }
}
```

---

### 2. Service Method: `ExtractPreferencesFromSavedGames`

**File:** `BoardVerse.Services/Services/BoardGameDiscoveryService.cs`

```csharp
/// <summary>
/// Phân tích lịch sử saved games của user để tạo preference profile.
/// </summary>
private async Task<UserGamePreference?> ExtractPreferencesFromSavedGames(
    Guid userId,
    CancellationToken cancellationToken)
{
    // ── Load saved games ────────────────────────────────────────
    var saves = await _saveRepository.GetByUserAsync(userId, cancellationToken);
    if (saves.Count == 0) return null;

    var gameIds = saves.Select(s => s.GameTemplateId).ToList();

    var query = new GetMasterGamesQuery { PageSize = 100, PageNumber = 1 };
    var allGames = await _gameTemplateRepository.GetBoardGamesPagedAsync(query, cancellationToken);

    var savedGames = allGames.Data
        .Where(g => gameIds.Contains(g.Id))
        .ToList();

    if (savedGames.Count == 0) return null;

    // ── Extract favorite categories (top 3) ─────────────────────
    var categoryFrequency = savedGames
        .SelectMany(g => g.Categories.Select(gc => gc.CategoryId))
        .GroupBy(id => id)
        .OrderByDescending(grp => grp.Count())
        .Take(3)
        .Select(grp => grp.Key)
        .ToList();

    // ── Calculate weight range (median ± 0.5) ───────────────────
    var weights = savedGames
        .Where(g => g.Weight.HasValue)
        .Select(g => g.Weight!.Value)
        .OrderBy(w => w)
        .ToList();

    double weightMin = 1.0, weightMax = 5.0;
    if (weights.Count > 0)
    {
        double median = weights.Count % 2 == 0
            ? (weights[weights.Count / 2 - 1] + weights[weights.Count / 2]) / 2.0
            : weights[weights.Count / 2];

        weightMin = Math.Max(1.0, median - 0.5);
        weightMax = Math.Min(5.0, median + 0.5);
    }

    // ── Calculate duration range (average ± 15 min) ─────────────
    int avgDuration = savedGames.Count > 0
        ? (int)savedGames.Average(g => g.PlayTime)
        : 45;

    int durationMin = Math.Max(10, avgDuration - 15);
    int durationMax = avgDuration + 15;

    return new UserGamePreference
    {
        FavoriteCategoryIds = categoryFrequency,
        PreferredWeightRange = (weightMin, weightMax),
        PreferredDurationMinutes = (durationMin, durationMax),
        SampleSize = savedGames.Count
    };
}
```

---

### 3. Service Method: `CalculatePreferenceBoost`

**File:** `BoardVerse.Services/Services/BoardGameDiscoveryService.cs`

```csharp
/// <summary>
/// Tính bonus score dựa trên user preference history.
/// Max: +15 pts (8 category + 5 weight + 2 duration).
/// </summary>
private double CalculatePreferenceBoost(
    GameTemplate game,
    UserGamePreference userPref)
{
    double boost = 0;

    // ── Category overlap: +8 pts max ────────────────────────────
    if (userPref.FavoriteCategoryIds.Count > 0)
    {
        var gameCategoryIds = game.Categories
            .Select(gc => gc.CategoryId)
            .ToHashSet();

        int matchCount = userPref.FavoriteCategoryIds
            .Count(id => gameCategoryIds.Contains(id));

        if (matchCount > 0)
        {
            // Partial credit: 8 pts * (matchCount / topFavorites)
            boost += 8.0 * matchCount / userPref.FavoriteCategoryIds.Count;
        }
    }

    // ── Weight match: +5 pts ────────────────────────────────────
    if (game.Weight.HasValue)
    {
        if (game.Weight.Value >= userPref.PreferredWeightRange.Min
            && game.Weight.Value <= userPref.PreferredWeightRange.Max)
        {
            boost += 5.0;
        }
    }

    // ── Duration match: +2 pts ──────────────────────────────────
    if (game.PlayTime >= userPref.PreferredDurationMinutes.Min
        && game.PlayTime <= userPref.PreferredDurationMinutes.Max)
    {
        boost += 2.0;
    }

    return boost;
}
```

---

### 4. Integration: Solo Survey

**Updated method:** `RunSurveyAsync`

```csharp
public async Task<BoardGameSurveyResponseDto> RunSurveyAsync(
    BoardGameSurveyRequestDto request,
    Guid? userId,
    double? latitude,
    double? longitude,
    CancellationToken cancellationToken = default)
{
    // ... validation ...

    // ── NEW: Extract user preferences from saved games ──────────
    UserGamePreference? userPref = null;
    if (userId.HasValue)
    {
        userPref = await ExtractPreferencesFromSavedGames(userId.Value, cancellationToken);
    }

    // ... query games ...

    // ── Scoring with preference boost ──────────────────────────
    var scoredGames = games.Select(game =>
    {
        var score = CalculateMatchScore(
            game,
            request,
            effectivePlayerCount,
            userPref);  // ← Pass user preference
        return new { Game = game, Score = score };
    })
    .OrderByDescending(x => x.Score)
    .ToList();

    // ... build response ...
}
```

**Updated method:** `CalculateMatchScore`

```csharp
private double CalculateMatchScore(
    GameTemplate game,
    BoardGameSurveyRequestDto request,
    int effectivePlayerCount,
    UserGamePreference? userPref = null)  // ← NEW parameter
{
    double score = 0;

    // ... existing scoring logic (player count, category, duration, weight, experience) ...

    // ── NEW: User Preference Boost: +15 pts ────────────────────
    if (userPref != null)
    {
        double prefBoost = CalculatePreferenceBoost(game, userPref);
        score += prefBoost;
    }

    return Math.Min(score, 100);
}
```

---

### 5. Integration: Group Discovery

**Updated DTO:** `MemberPreferenceDto`

```csharp
public class MemberPreferenceDto
{
    /// <summary>
    /// User ID của thành viên (optional — để personalize từ saved games).
    /// </summary>
    public Guid? UserId { get; set; }  // ← NEW field

    public int PlayerCount { get; set; }
    public PlayerExperienceLevel? ExperienceLevel { get; set; }
    public List<Guid>? CategoryIds { get; set; }
    public List<string>? PreferredDurations { get; set; }
    public List<WeightRange>? WeightRanges { get; set; }
    public string? Note { get; set; }
}
```

**Updated method:** `GroupDiscoveryAsync`

```csharp
public async Task<GroupDiscoveryResponseDto> GroupDiscoveryAsync(
    GroupDiscoveryRequestDto request,
    double? latitude,
    double? longitude,
    CancellationToken cancellationToken = default)
{
    // ... validation ...

    // ── NEW: Extract preferences for each member with userId ───
    var memberPreferences = new Dictionary<int, UserGamePreference?>();

    for (int i = 0; i < request.Members.Count; i++)
    {
        var member = request.Members[i];

        if (member.UserId.HasValue)
        {
            memberPreferences[i] = await ExtractPreferencesFromSavedGames(
                member.UserId.Value,
                cancellationToken);
        }
        else
        {
            memberPreferences[i] = null;
        }
    }

    // ... query games ...

    // ── AWM Scoring with per-member preference ─────────────────
    var scoredGames = games.Select(game =>
    {
        var subgroupScores = new List<SubGroupScoreDto>();

        foreach (var member in request.Members)
        {
            int memberIndex = request.Members.IndexOf(member);
            var userPref = memberPreferences.GetValueOrDefault(memberIndex);

            var (score, reason) = CalculateSubGroupScore(game, member, userPref);
            subgroupScores.Add(new SubGroupScoreDto
            {
                SubGroupIndex = memberIndex,
                PlayerCount = member.PlayerCount,
                ExperienceLevel = member.ExperienceLevel,
                Score = score,
                Reason = reason
            });
        }

        // ... aggregate scoring ...
    })
    .OrderByDescending(x => x.AggregateScore)
    .ToList();

    // ... build response ...
}
```

**Updated method:** `CalculateSubGroupScore`

```csharp
private (double Score, string? Reason) CalculateSubGroupScore(
    GameTemplate game,
    MemberPreferenceDto member,
    UserGamePreference? userPref = null)  // ← NEW parameter
{
    double score = 0;
    var reasons = new List<string>();

    // ... existing scoring logic ...

    // ── NEW: User Preference Boost: +15 pts ────────────────────
    if (userPref != null)
    {
        double prefBoost = CalculatePreferenceBoost(game, userPref);
        if (prefBoost > 0)
        {
            score += prefBoost;
            reasons.Add($"Phù hợp với lịch sử chơi của bạn");
        }
    }

    string? topReason = reasons.Count > 0
        ? reasons.OrderByDescending(r => r.Length).First()
        : "Game có thể chơi được";

    return (Math.Min(score, 100), topReason);
}
```

---

## 📡 API Endpoint Documentation

### 1. Group Discovery (Updated with userId support)

#### Endpoint

```
POST /api/v1/discovery/group
```

**Authentication:** Required (Bearer token)

#### Base URLs

| Environment | URL |
|---|---|
| **Production** | `https://api.boardverse.com` |
| **Staging** | `https://staging-api.boardverse.com` |
| **Local Dev** | `http://localhost:5000` |

#### Request Headers

```http
Authorization: Bearer <your_jwt_token>
Content-Type: application/json
```

#### Request Body

```json
{
  "members": [
    {
      "userId": "550e8400-e29b-41d4-a716-446655440000",
      "playerCount": 2,
      "experienceLevel": 2,
      "categoryIds": ["cat-guid-1", "cat-guid-2"],
      "preferredDurations": ["30to60", "60to90"],
      "weightRanges": [1, 2, 3],
      "note": "Thích game chiến thuật nhẹ"
    },
    {
      "playerCount": 2,
      "experienceLevel": 1,
      "categoryIds": ["cat-guid-3"],
      "preferredDurations": ["under30"],
      "weightRanges": [1]
    }
  ],
  "latitude": 10.762622,
  "longitude": 106.660172
}
```

**Field Details:**

| Field | Type | Required | Description |
|---|---|---|---|
| `members` | `array` | Yes | Danh sách sub-groups (min 1, max 10) |
| `members[].userId` | `string` (UUID) | **No** | **NEW:** User ID để personalize từ saved games. Nếu null → skip personalization |
| `members[].playerCount` | `number` | Yes | Số người chơi trong sub-group (1-20) |
| `members[].experienceLevel` | `number` | No | Trình độ (1=Beginner, 2=Casual, 3=Intermediate, 4=Advanced, 5=Expert) |
| `members[].categoryIds` | `string[]` | No | Danh sách category IDs (UUID) |
| `members[].preferredDurations` | `string[]` | No | `["under30", "30to60", "60to90", "over90"]` |
| `members[].weightRanges` | `number[]` | No | `[1, 2, 3, 4, 5]` (1=Light, 2=MediumLight, 3=Medium, 4=MediumHeavy, 5=Heavy) |
| `members[].note` | `string` | No | Ghi chú tùy chọn |
| `latitude` | `number` | No | Vĩ độ GPS để tìm cafe gần |
| `longitude` | `number` | No | Kinh độ GPS |

#### Response - Success (200 OK)

```json
{
  "success": true,
  "data": {
    "games": [
      {
        "id": "550e8400-e29b-41d4-a716-446655440000",
        "name": "Catan",
        "thumbnailUrl": "https://example.com/catan.jpg",
        "description": "Game chiến thuật xây dựng đảo",
        "minPlayers": 3,
        "maxPlayers": 4,
        "playTime": 90,
        "weight": 2.3,
        "categories": ["Chiến thuật", "Kinh tế"],
        "aggregateScore": 85.5,
        "subGroupBreakdown": [
          {
            "subGroupIndex": 0,
            "playerCount": 2,
            "experienceLevel": 2,
            "score": 88.0,
            "reason": "Phù hợp với lịch sử chơi của bạn"
          },
          {
            "subGroupIndex": 1,
            "playerCount": 2,
            "experienceLevel": 1,
            "score": 76.0,
            "reason": "Thời gian chơi phù hợp"
          }
        ]
      },
      {
        "id": "660e8400-e29b-41d4-a716-446655440001",
        "name": "Ticket to Ride",
        "thumbnailUrl": "https://example.com/ttr.jpg",
        "description": "Game xây tuyến đường sắt",
        "minPlayers": 2,
        "maxPlayers": 5,
        "playTime": 45,
        "weight": 1.9,
        "categories": ["Gia đình", "Chiến thuật"],
        "aggregateScore": 82.3,
        "subGroupBreakdown": [
          {
            "subGroupIndex": 0,
            "playerCount": 2,
            "experienceLevel": 2,
            "score": 80.0,
            "reason": "Độ phức tạp vừa phải"
          },
          {
            "subGroupIndex": 1,
            "playerCount": 2,
            "experienceLevel": 1,
            "score": 84.5,
            "reason": "Phù hợp người mới chơi"
          }
        ]
      }
    ],
    "totalCount": 25,
    "nearestCafe": {
      "cafeId": "cafe-uuid",
      "cafeName": "BoardVerse Cafe",
      "cafeAddress": "123 Nguyễn Huệ, Q1, TP.HCM",
      "distanceKm": 2.5,
      "availableGames": ["550e8400-e29b-41d4-a716-446655440000"]
    }
  }
}
```

#### Response - Validation Error (400 Bad Request)

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Request validation failed for '/api/v1/discovery/group'",
    "details": [
      "members must contain at least 1 sub-group",
      "members[0].playerCount must be between 1 and 20"
    ]
  }
}
```

#### Response - Unauthorized (401 Unauthorized)

```json
{
  "success": false,
  "error": {
    "code": "UNAUTHORIZED",
    "message": "Token is missing or invalid"
  }
}
```

#### Response - Server Error (500 Internal Server Error)

```json
{
  "success": false,
  "error": {
    "code": "INTERNAL_SERVER_ERROR",
    "message": "An unexpected error occurred. Please try again later."
  }
}
```

---

### 2. Solo Survey (No API changes, but behavior enhanced)

**Endpoint:** `POST /api/v1/discovery/survey`

**Changes:**
- **Backend behavior:** If user is authenticated → automatically extract preferences from saved games
- **Request format:** No changes (userId extracted from JWT token)
- **Response format:** No changes (scores may be higher due to +15 pts personalization boost)

**New match reasons may appear:**
- ✅ `"Phù hợp với lịch sử chơi của bạn"` — when user has saved games and game matches preferences

**See:** `WEIGHT_FILTER_IMPLEMENTATION.md` for full Solo Survey API documentation

---

## 🌐 HTTP Status Codes

| Code | Meaning | When it happens |
|---|---|---|
| **200 OK** | Success | Games returned (or empty `[]`) |
| **400 Bad Request** | Validation error | Invalid `userId` format, missing required fields, playerCount out of range |
| **401 Unauthorized** | Authentication failed | Missing token, expired token, invalid signature |
| **403 Forbidden** | Permission denied | Valid token but user blocked or insufficient role |
| **404 Not Found** | Resource not found | Invalid endpoint URL |
| **500 Internal Server Error** | Server error | Database down, unhandled exception |

---

## 🧪 Test Cases

### Test 1: User chưa có saved games

**Given:**
- UserId: `new-user-id`
- SavedGames table: empty

**When:** Gọi `/api/v1/discovery/survey`

**Expected:**
- `ExtractPreferencesFromSavedGames` returns `null`
- No preference boost applied (+0 pts)
- Scores giống logic cũ (backward compatible)

---

### Test 2: User có 3 saved games (Strategy + Medium weight)

**Given:**
- UserId: `user-123`
- SavedGames:
  - Catan (Strategy, Weight 2.3, 90 min)
  - Splendor (Strategy, Weight 2.1, 30 min)
  - Ticket to Ride (Strategy, Weight 1.9, 45 min)

**Extracted Preference:**
```csharp
{
  FavoriteCategoryIds: ["strategy-guid"],
  PreferredWeightRange: (1.6, 2.6),  // median 2.1 ± 0.5
  PreferredDurationMinutes: (40, 70),  // avg 55 ± 15
  SampleSize: 3
}
```

**When:** Survey với game "7 Wonders" (Strategy, Weight 2.3, 30 min)

**Expected Boost:**
- Category match: +8 pts (1 / 1 = 100%)
- Weight match: +5 pts (2.3 in [1.6, 2.6])
- Duration mismatch: +0 pts (30 < 40)
- **Total boost: +13 pts**

---

### Test 3: Group Discovery với mixed userId presence

**Given:**
- Member 1: userId = `user-123` (có 5 saved games)
- Member 2: userId = null (guest)

**Expected:**
- Member 1 scores boosted by +15 pts max
- Member 2 scores không có boost
- AWMS = weighted average (Member 1 score tăng → aggregate score tăng)

---

### Test 4: User có saved games nhưng game mới không match

**Given:**
- User saved: Party games (Light weight 1.2-1.5)
- Candidate game: Gloomhaven (Heavy weight 3.8)

**Expected Boost:**
- Category mismatch: +0 pts
- Weight mismatch: +0 pts
- Duration mismatch: +0 pts
- **Total boost: +0 pts** (game vẫn xuất hiện nhưng score thấp)

---

## 🎯 Score Distribution (Updated)

| Component | Max Points | Notes |
|---|---|---|
| Player count fit | 30 | Tight range gets higher score |
| Category match | 25 | Pro-rated by overlap |
| Duration match | 20 | Survey preference or user history |
| Experience level | 10 | Fit with user level |
| BGG Weight bonus | 10 | Fit with experience + survey preference |
| **User Preference Boost** | **+15** | **NEW: Category (8) + Weight (5) + Duration (2)** |
| **Total** | **110** | **Capped at 100** |

---

## 🚀 Frontend Integration Notes

### TypeScript Type (updated)

```typescript
interface MemberPreferenceDto {
  userId?: string;  // ← NEW: optional UUID
  playerCount: number;
  experienceLevel?: number;
  categoryIds?: string[];
  preferredDurations?: string[];
  weightRanges?: number[];
  note?: string;
}

interface GroupDiscoveryRequest {
  members: MemberPreferenceDto[];
  totalPlayerCount?: number;
  latitude?: number;
  longitude?: number;
  radiusKm?: number;
}
```

### Usage Example (React)

```tsx
const [loggedInUserId, setLoggedInUserId] = useState<string | null>(null);

const handleGroupSearch = async () => {
  const request: GroupDiscoveryRequest = {
    members: [
      {
        userId: loggedInUserId,  // ← Pass logged-in user ID
        playerCount: 2,
        experienceLevel: 2
      },
      {
        // Guest member (no userId)
        playerCount: 2,
        experienceLevel: 1
      }
    ]
  };

  const response = await api.post('/api/v1/discovery/group', request);
  // ...
};
```

---

## 📈 Performance Considerations

### Complexity

- **ExtractPreferencesFromSavedGames:**
  - Load saved games: `O(n)` where n = # of saved games (typically < 50)
  - Group categories: `O(m)` where m = total categories across all games
  - **Total:** `O(n + m)` — negligible for typical user

- **CalculatePreferenceBoost:**
  - Category overlap: `O(k)` where k = # favorite categories (max 3)
  - Weight/duration: `O(1)`
  - **Total:** `O(1)` — constant time

### Caching Strategy (Optional Future Enhancement)

```csharp
// Cache user preference for 10 minutes
var cacheKey = $"user_pref:{userId}";
var cachedPref = await _cache.GetAsync<UserGamePreference>(cacheKey);

if (cachedPref != null)
    return cachedPref;

var pref = await ExtractPreferencesFromSavedGames(userId, ct);
await _cache.SetAsync(cacheKey, pref, TimeSpan.FromMinutes(10));

return pref;
```

**Invalidate cache when:**
- User saves/unsaves a game
- User completes a session

---

## ✅ Checklist

### Phase 1: Core Logic ✅

- [x] Define `UserGamePreference` DTO
- [x] Implement `ExtractPreferencesFromSavedGames`
- [x] Implement `CalculatePreferenceBoost`
- [x] Update `CalculateMatchScore` signature
- [x] Update `RunSurveyAsync` to extract preferences
- [x] **Build successful (0 errors)**

### Phase 2: Group Discovery Integration ✅

- [x] Add `UserId` field to `MemberPreferenceDto`
- [x] Update `GroupDiscoveryAsync` to extract per-member preferences
- [x] Update `CalculateSubGroupScore` signature
- [x] Pass `userPref` to sub-group scoring
- [x] **Build successful (0 errors)**

### Phase 3: Testing 🔄 (Next Step)

- [ ] Seed test data với saved games
- [ ] Test solo survey với/không có userId
- [ ] Test group discovery với mixed userId
- [ ] Verify score boost logic
- [ ] Test edge cases (empty saved games, no category overlap)

### Phase 4: Documentation ✅

- [x] Update API documentation
- [x] Add TypeScript types for FE
- [x] Add test cases
- [x] Add performance notes

---

## 🔄 Migration Notes

**Không cần migration mới** — feature này sử dụng existing tables:

- `PlayerBoardGameSave` (đã có)
- `GameTemplate` (đã có)
- `GameTemplateCategory` (đã có)

**Backward compatible:**
- API request format không thay đổi (userId là optional)
- Response format không thay đổi
- Guest users (userId = null) vẫn hoạt động bình thường

---

## 🎉 Summary

### What Changed

1. **Solo Survey:** Personalized dựa trên saved games của chính user
2. **Group Discovery:** Personalized per member nếu có `userId`
3. **Score boost:** +15 pts max cho game match với user history
4. **Backward compatible:** Guest users không bị ảnh hưởng

### Benefits

- ✅ **Better recommendations** cho returning users
- ✅ **Cold start không bị ảnh hưởng** (new users vẫn có base recommendations)
- ✅ **Group suggestions cải thiện** khi members có user accounts
- ✅ **Transparent** (match reasons show "Phù hợp với lịch sử chơi")

### Next Steps

1. **Test với real data** (seed saved games)
2. **Monitor score distribution** (đảm bảo không bị lệch quá nhiều)
3. **A/B testing** (compare personalized vs non-personalized)
4. **Optimize** (cache user preferences nếu cần)

---

**Implementation completed:** 2026-01-23  
**Build status:** ✅ 0 errors, 34 warnings (existing)  
**Ready for testing:** Yes
