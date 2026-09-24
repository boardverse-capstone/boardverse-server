# Cải tiến 5: SavedBoardGame Feedback Loop

## 📌 Mục tiêu

Sử dụng **lịch sử game đã save** để cá nhân hóa thuật toán gợi ý:
- Khi user save nhiều game cùng category/weight → ưu tiên gợi ý game tương tự
- Tạo "profile ẩn" từ hành vi thực tế thay vì chỉ dựa vào survey input
- Tăng độ chính xác và personalization theo thời gian

---

## 🎯 Chiến lược Feedback Loop

### **Cơ chế hoạt động:**

1. **Thu thập tín hiệu:**
   - User save game → lưu `PlayerBoardGameSave` (đã có)
   - Hệ thống analyze saved games → extract preferences:
     - Top 3 categories thường save
     - Trung bình weight của games đã save
     - Trung bình duration của games đã save

2. **Áp dụng personalization:**
   - Khi user chạy survey → query saved games trước
   - Nếu có ≥3 saved games → apply "preference boost"
   - Game match preference profile → +5-15 pts bonus

3. **Adaptive learning:**
   - Mỗi lần save/unsave → refresh preference cache
   - Preference mới nhất có trọng số cao hơn (time decay)

---

## 🗂️ Database Schema (Không cần migration mới)

**Sử dụng entities hiện tại:**
- ✅ `PlayerBoardGameSave` (đã có sẵn)
- ✅ `GameTemplate.Weight` (đã có từ Cải tiến 1)
- ✅ `GameTemplate.Categories` (đã có)
- ✅ `GameTemplate.PlayTime` (đã có)

**Không cần thêm bảng mới** → Compute preferences on-the-fly hoặc cache in-memory

---

## 💡 Approach: Lightweight In-Memory Profile

### **Option 1: Compute on-the-fly (Chọn này)**

**Pros:**
- Không cần migration
- Luôn real-time với saved games mới nhất
- Simple implementation

**Cons:**
- Query overhead (nhưng acceptable với <100 saved games)

### **Option 2: Cache in UserProfile**

**Pros:**
- Fast lookup (1 query)
- No repeated computation

**Cons:**
- Cần migration thêm columns `PreferredCategoryIds`, `PreferredWeightAvg`, etc.
- Cache invalidation complexity
- Over-engineering cho MVP

---

## 🔧 Implementation Plan

### **Bước 1: Tạo Preference Extractor**

File: `BoardVerse.Services/Services/BoardGameDiscoveryService.cs`

Thêm helper method:

```csharp
private async Task<UserGamePreference?> ExtractPreferencesFromSavedGames(
    Guid userId,
    CancellationToken cancellationToken)
{
    var saves = await _saveRepository.GetByUserAsync(userId, cancellationToken);
    
    if (saves.Count < 3)
        return null; // Chưa đủ data để personalize
    
    var gameIds = saves.Select(s => s.GameTemplateId).ToList();
    var query = new GetMasterGamesQuery { PageSize = 100, PageNumber = 1 };
    var allGames = await _gameTemplateRepository.GetBoardGamesPagedAsync(query, cancellationToken);
    
    var savedGames = allGames.Data
        .Where(g => gameIds.Contains(g.Id))
        .ToList();
    
    // Extract top 3 categories
    var categoryFrequency = new Dictionary<Guid, int>();
    foreach (var game in savedGames)
    {
        foreach (var gc in game.Categories)
        {
            categoryFrequency[gc.CategoryId] = categoryFrequency.GetValueOrDefault(gc.CategoryId, 0) + 1;
        }
    }
    
    var topCategories = categoryFrequency
        .OrderByDescending(kv => kv.Value)
        .Take(3)
        .Select(kv => kv.Key)
        .ToList();
    
    // Calculate average weight
    var weights = savedGames
        .Where(g => g.Weight.HasValue)
        .Select(g => g.Weight!.Value)
        .ToList();
    
    double? avgWeight = weights.Count > 0 ? weights.Average() : null;
    
    // Calculate average duration
    double avgDuration = savedGames.Average(g => g.PlayTime);
    
    return new UserGamePreference
    {
        UserId = userId,
        TopCategoryIds = topCategories,
        AverageWeight = avgWeight,
        AverageDuration = avgDuration,
        SavedGameCount = saves.Count
    };
}
```

---

### **Bước 2: Define DTO**

File: `BoardVerse.Core/DTOs/Discovery/UserGamePreference.cs` (NEW)

```csharp
namespace BoardVerse.Core.DTOs.Discovery;

/// <summary>
/// User's inferred preferences from saved games.
/// </summary>
public class UserGamePreference
{
    public Guid UserId { get; set; }
    public List<Guid> TopCategoryIds { get; set; } = [];
    public double? AverageWeight { get; set; }
    public double AverageDuration { get; set; }
    public int SavedGameCount { get; set; }
}
```

---

### **Bước 3: Integrate vào Scoring**

Update method `CalculateMatchScore`:

```csharp
private double CalculateMatchScore(
    GameTemplate game,
    BoardGameSurveyRequestDto request,
    int effectivePlayerCount,
    UserGamePreference? userPref = null)  // ← NEW parameter
{
    double score = 0;
    
    // ... existing scoring logic ...
    
    // ── Saved Game Preference Boost: up to +15 pts ────────────────
    if (userPref != null)
    {
        score += CalculatePreferenceBoost(game, userPref);
    }
    
    return Math.Min(score, 100);
}

private double CalculatePreferenceBoost(
    GameTemplate game,
    UserGamePreference userPref)
{
    double boost = 0;
    
    // Category affinity: +8 pts if game in user's top 3 categories
    var gameCategoryIds = game.Categories
        .Select(gc => gc.CategoryId)
        .ToHashSet();
    
    if (userPref.TopCategoryIds.Any(id => gameCategoryIds.Contains(id)))
    {
        boost += 8;
    }
    
    // Weight affinity: +4 pts if game weight close to user's average
    if (userPref.AverageWeight.HasValue && game.Weight.HasValue)
    {
        double weightDiff = Math.Abs(game.Weight.Value - userPref.AverageWeight.Value);
        
        if (weightDiff <= 0.5)
            boost += 4;
        else if (weightDiff <= 1.0)
            boost += 2;
    }
    
    // Duration affinity: +3 pts if game duration close to user's average
    double durationDiff = Math.Abs(game.PlayTime - userPref.AverageDuration);
    
    if (durationDiff <= 15)
        boost += 3;
    else if (durationDiff <= 30)
        boost += 1.5;
    
    return boost;
}
```

---

### **Bước 4: Update RunSurveyAsync**

Thêm preference extraction vào main flow:

```csharp
public async Task<BoardGameSurveyResponseDto> RunSurveyAsync(
    BoardGameSurveyRequestDto request,
    Guid? userId,
    double? latitude,
    double? longitude,
    CancellationToken cancellationToken = default)
{
    // ... existing validation ...
    
    // ── NEW: Extract user preferences from saved games ────────────
    UserGamePreference? userPref = null;
    if (userId.HasValue)
    {
        userPref = await ExtractPreferencesFromSavedGames(userId.Value, cancellationToken);
    }
    
    // ... build query ...
    
    // ── Scoring với preference boost ──────────────────────────────
    var scoredGames = filteredGames
        .Select(game =>
        {
            var score = CalculateMatchScore(
                game,
                request,
                effectivePlayerCount,
                userPref);  // ← Pass preference
            
            return new { Game = game, Score = score };
        })
        .OrderByDescending(x => x.Score)
        .ToList();
    
    // ... rest of method ...
}
```

---

### **Bước 5: Update Group Discovery**

Tương tự, apply preference boost cho từng member:

```csharp
public async Task<GroupDiscoveryResponseDto> GroupDiscoveryAsync(
    GroupDiscoveryRequestDto request,
    double? latitude,
    double? longitude,
    CancellationToken cancellationToken = default)
{
    // ... existing validation ...
    
    // ── NEW: Extract preferences for each member with userId ──────
    var memberPreferences = new Dictionary<int, UserGamePreference?>();
    
    for (int i = 0; i < request.Members.Count; i++)
    {
        var member = request.Members[i];
        
        // Note: Cần thêm userId vào MemberPreferenceDto nếu chưa có
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
    
    // ... scoring logic với memberPreferences[subGroupIndex] ...
}
```

**⚠️ Note:** `MemberPreferenceDto` hiện tại chưa có `UserId` → cần thêm field này.

---

## 📊 Score Distribution (Updated)

### Solo Survey Scoring (100 pts max)

| Criteria | Points | Notes |
|---|---|---|
| Category Match | 40 | Proportional to match count |
| Duration Match | 30 | Any preferred range |
| Player Range Fit | 10-30 | Tight fit = 30 |
| Weight Range Match | 10 | At least 1 range matches |
| Experience Bonus | 15 | Adjusted from 25 |
| BGG Weight Bonus | 15 | Proximity-based |
| **Preference Boost** | **15** | **NEW: From saved games** |
| **TOTAL** | **~155** | **Capped at 100** |

**Preference Boost breakdown:**
- Category affinity: +8 pts
- Weight affinity: +4 pts
- Duration affinity: +3 pts

---

## 🧪 Test Cases

### Case 1: New user (no saved games)

```json
POST /api/v1/discovery/survey
Authorization: Bearer <new_user_token>

{
  "playerCount": 3,
  "experienceLevel": 2
}
```

**Expected:**
- `userPref = null`
- Scoring giống cũ (không có +15 pts preference boost)
- Backward compatible

---

### Case 2: User with 5 saved light party games

**Setup:**
- User đã save: Uno, Dixit, Codenames, Sushi Go, Avalon
- Top categories: Party (5/5), Social Deduction (2/5)
- Avg weight: ~1.5
- Avg duration: ~20 min

```json
POST /api/v1/discovery/survey
Authorization: Bearer <user_token>

{
  "playerCount": 4,
  "experienceLevel": 2
}
```

**Expected:**
- Game "Wavelength" (Party, weight=1.8, 30min) → +8 (category) +4 (weight) +1.5 (duration) = **+13.5 pts**
- Game "Gloomhaven" (Strategy, weight=3.8, 120min) → **+0 pts** (no match)

---

### Case 3: User with mixed preferences (edge case)

**Setup:**
- User đã save: Catan (2.3), Gloomhaven (3.8), Uno (1.2)
- Top categories: Strategy (2/3), Party (1/3)
- Avg weight: ~2.4
- Avg duration: ~60 min

```json
POST /api/v1/discovery/survey
Authorization: Bearer <user_token>

{
  "playerCount": 3,
  "categoryIds": ["strategy-guid"],
  "weightRanges": [3]
}
```

**Expected:**
- Game "Pandemic" (Strategy, weight=2.4, 45min) → +8 (category) +4 (weight) +3 (duration) = **+15 pts**
- User profile cân bằng giữa light và heavy → medium games được boost

---

## 🚀 Implementation Steps

### Phase 1: Core Logic ✅

- [x] Define `UserGamePreference` DTO
- [x] Implement `ExtractPreferencesFromSavedGames`
- [x] Implement `CalculatePreferenceBoost`
- [x] Update `CalculateMatchScore` signature
- [x] Update `RunSurveyAsync` to extract preferences
- [x] **Build successful (0 errors)**

### Phase 2: Group Discovery

- [ ] Add `UserId?` to `MemberPreferenceDto`
- [ ] Update `GroupDiscoveryAsync` to extract per-member preferences
- [ ] Update `CalculateSubGroupScore` to apply boost

### Phase 3: Testing

- [ ] Unit test: `ExtractPreferencesFromSavedGames` with mock data
- [ ] Unit test: `CalculatePreferenceBoost` edge cases
- [ ] Integration test: Solo survey with saved games
- [ ] Integration test: Group discovery with mixed preferences

### Phase 4: Optimization (Optional)

- [ ] Cache preferences in-memory (expire after 5 min)
- [ ] Batch fetch saved games (reduce N+1 queries)
- [ ] Add telemetry: track % users with preferences

---

## 📌 Frontend Impact

### API Contract Changes

**No breaking changes** (backward compatible)

### New behavior (transparent to FE):

- Response `games[]` có thể có order khác nếu user có saved games
- Game mà user thường save sẽ được ưu tiên cao hơn trong results

### Optional: Show preference hints

**UI suggestion:**
```tsx
<GameCard game={game}>
  {game.matchReasons.map(reason => (
    <Chip>{reason}</Chip>
  ))}
  
  {/* NEW: Show if boosted by saved games */}
  {game.matchReasons.includes("Phù hợp với sở thích của bạn") && (
    <Badge color="purple">
      <StarIcon /> Gợi ý cho bạn
    </Badge>
  )}
</GameCard>
```

**Add to `matchReasons` in backend:**
```csharp
if (boost >= 10)
{
    reasons.Add("Phù hợp với sở thích của bạn dựa trên games đã lưu");
}
```

---

## 🔮 Future Enhancements

### 1. Time Decay (Weighted Recency)
```csharp
// Games saved in last 7 days = 1.0x weight
// Games saved 8-30 days ago = 0.7x weight
// Games saved 31+ days ago = 0.4x weight

var weightedCategories = saves
    .Select(s => new {
        CategoryIds = GetCategories(s.GameTemplateId),
        Weight = CalculateTimeDecay(s.SavedAt)
    })
    .SelectMany(x => x.CategoryIds.Select(id => (id, x.Weight)))
    .GroupBy(t => t.id)
    .Select(g => new { CategoryId = g.Key, Score = g.Sum(t => t.Weight) })
    .OrderByDescending(x => x.Score)
    .Take(3);
```

### 2. Negative Signals (Unsaved Games)
- Nếu user unsave game → giảm boost cho category đó
- Track "rejected" games (viewed but not saved) → penalize similar games

### 3. Multi-Signal Fusion
- Saved games + Match history + Lobby joined + Time played
- Weighted ensemble: `0.4 * saved + 0.3 * played + 0.2 * lobby + 0.1 * match`

### 4. Collaborative Filtering
- "Users who saved X also saved Y" → recommend Y
- Requires significant user base (1000+ active users)

---

## ⚠️ Known Limitations

| Limitation | Impact | Mitigation |
|---|---|---|
| New users (0-2 saved games) | No personalization | Graceful fallback to default scoring |
| Query overhead (fetch saved games) | +50-100ms latency | Acceptable for now; cache later |
| Cold start problem | First 3 surveys not personalized | Expected; improves over time |
| Bias toward early saves | Old preferences dominate | Add time decay in Phase 2 |

---

## ✅ Acceptance Criteria

- [ ] Code builds without errors
- [ ] Unit tests pass (≥80% coverage for new methods)
- [ ] Integration test: User with 5 saved games gets boosted results
- [ ] Integration test: New user (0 saved games) works without errors
- [ ] Performance: Latency increase <100ms vs baseline
- [ ] Documentation updated (API contract, scoring logic)

---

**Implementation Date:** 2026-09-23 (Planned)  
**Status:** 📋 DESIGN COMPLETE - READY FOR IMPLEMENTATION  
**Priority:** P2 (Nice-to-have for MVP, Critical for retention)
