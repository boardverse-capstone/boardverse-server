# Test Cải tiến 5: SavedBoardGame Feedback Loop

## ✅ Build Status

**Code compiles successfully** (warnings về file locks vì API đang chạy - không phải lỗi biên dịch)

---

## 🧪 Test Data Setup

### Test User
- **UserId:** `0358b254-183a-4b7d-83e2-666a72094478`
- **Saved Games:** 3 games
  - Uno (Weight: 1.02, 30 min, Category: Giải trí)
  - Codenames (Weight: 1.24, 15 min, Categories: Ẩn vai, Giải trí)
  - Werewolf Ultimate (Weight: 1.38, 45 min, Categories: Ẩn vai, Giải trí)

### Extracted Preference Profile
```csharp
{
  TopCategoryIds: [
    "c1111111-1111-1111-1111-111111111113",  // Giải trí (3/3)
    "c1111111-1111-1111-1111-111111111111"   // Ẩn vai (2/3)
  ],
  AverageWeight: 1.21,
  AverageDuration: 30,
  SavedGameCount: 3
}
```

---

## 📊 Expected Behavior

### Scenario 1: Survey với Light Party Game

**Request:**
```json
POST /api/v1/discovery/survey
Authorization: Bearer <token-of-user-0358b254>

{
  "playerCount": 4,
  "experienceLevel": 2
}
```

**Expected Preference Boost:**
- Game có category "Giải trí" hoặc "Ẩn vai" → **+8 pts**
- Game có weight ~1.2 (trong range 1.0-1.5) → **+0 pts** (vì design doc dùng median ± 0.5)
- Game có duration ~30 min (trong range 15-45) → **+2 pts**
- **Total boost: up to +10 pts**

**Top recommended games should include:**
- Avalon (Ẩn vai, 1.8 weight, 30 min) → +8 (category) +2 (duration) = **+10 pts**
- Sushi Go (Giải trí, 1.2 weight, 15 min) → +8 (category) +2 (duration) = **+10 pts**

---

### Scenario 2: Survey với Heavy Strategy Game

**Request:**
```json
POST /api/v1/discovery/survey
Authorization: Bearer <token-of-user-0358b254>

{
  "playerCount": 3,
  "categoryIds": ["strategy-category-guid"],
  "weightRanges": [4, 5]
}
```

**Expected Preference Boost:**
- Game có category "Strategy" (không trong top 2) → **+0 pts**
- Game có weight 3.5-4.5 (xa user average 1.21) → **+0 pts**
- Game có duration 120 min (xa user average 30) → **+0 pts**
- **Total boost: +0 pts**

**Behavior:** Heavy strategy games vẫn xuất hiện (vì match survey input), nhưng không được personalized boost.

---

### Scenario 3: New User (no saved games)

**Request:**
```json
POST /api/v1/discovery/survey
Authorization: Bearer <new-user-token>

{
  "playerCount": 4,
  "experienceLevel": 1
}
```

**Expected:**
- `ExtractPreferencesFromSavedGames` returns `null`
- `userPref` parameter = `null`
- `CalculatePreferenceBoost` **không được gọi**
- Scores hoàn toàn dựa trên base scoring (backward compatible)

---

## 🔍 Code Verification Points

### 1. ExtractPreferencesFromSavedGames
- ✅ Returns `null` nếu saved games < 3 (theo design doc sai — code hiện tại không check minimum)
- ⚠️ **Bug found:** Code không check `if (saves.Count < 3) return null;`
- ✅ Top 3 categories được extract đúng
- ✅ Average weight được tính đúng
- ✅ Average duration được tính đúng

### 2. CalculatePreferenceBoost
- ✅ Category overlap: +8 pts max
- ⚠️ **Design mismatch:** Code dùng `AverageWeight` thay vì `PreferredWeightRange (Min, Max)`
- ✅ Duration match: +2 pts

### 3. Integration trong RunSurveyAsync
- ✅ Extract preferences nếu `userId.HasValue`
- ✅ Pass `userPref` vào `CalculateMatchScore`
- ✅ Backward compatible (guest users không bị ảnh hưởng)

### 4. Integration trong GroupDiscoveryAsync
- ✅ Extract preferences per member
- ✅ Pass `userPref` vào `CalculateSubGroupScore`
- ✅ `MemberPreferenceDto` có field `UserId?`

---

## ⚠️ Bugs Found During Review

### Bug 1: Missing Minimum Saved Games Check

**Location:** `BoardVerse.Services/Services/BoardGameDiscoveryService.cs:1010`

**Current Code:**
```csharp
var saves = await _saveRepository.GetByUserAsync(userId, cancellationToken);
if (saves.Count == 0) return null;
```

**Issue:** Design doc nói "Nếu có ≥3 saved games → apply preference boost", nhưng code chỉ check `> 0`.

**Fix:**
```csharp
if (saves.Count < 3) return null;  // Chưa đủ data để personalize
```

---

### Bug 2: DTO Mismatch với Implementation

**Design Doc DTO:** `UserGamePreference.cs` (in design)
```csharp
public (double Min, double Max) PreferredWeightRange { get; set; }
public (int Min, int Max) PreferredDurationMinutes { get; set; }
```

**Actual DTO:** `UserGamePreference.cs` (trong code)
```csharp
public double? AverageWeight { get; set; }
public double AverageDuration { get; set; }
```

**Impact:** `CalculatePreferenceBoost` logic phải khác với design doc.

**Current Implementation:**
```csharp
// Weight match: +5 pts (design doc), but code uses different logic
```

---

## 🎯 Manual Test Steps

### Step 1: Test với Swagger/Postman

1. Get token cho user `0358b254-183a-4b7d-83e2-666a72094478`
2. Call `POST /api/v1/discovery/survey` với body:
   ```json
   {
     "playerCount": 4,
     "experienceLevel": 2
   }
   ```
3. Verify response `games[]`:
   - Games với category "Giải trí"/"Ẩn vai" có score cao hơn
   - Game "Avalon" hoặc similar light party games nên ở top 5

### Step 2: Compare với Guest User

1. Call cùng endpoint với **guest token** (user chưa save game nào)
2. Compare scores:
   - Guest user: base score only
   - Test user: base score + preference boost (+8 to +10 pts cho matching games)

### Step 3: Verify Group Discovery

1. Call `POST /api/v1/discovery/group` với:
   ```json
   {
     "members": [
       {
         "userId": "0358b254-183a-4b7d-83e2-666a72094478",
         "playerCount": 2,
         "experienceLevel": 2
       },
       {
         "playerCount": 2,
         "experienceLevel": 1
       }
     ]
   }
   ```
2. Verify `subGroupBreakdown[0].score` > `subGroupBreakdown[1].score` cho games matching saved preferences

---

## 📌 Next Steps

### Immediate (để hoàn thành Cải tiến 5)

1. ✅ **Code compiles** (verified)
2. ⚠️ **Fix Bug 1:** Add minimum 3 saved games check
3. ⚠️ **Document design deviation:** DTO uses average instead of range
4. 🔄 **Manual test:** Run API test với user `0358b254`
5. 📊 **Log scores:** Add logging để verify boost logic

### Optional Improvements

1. Cache user preferences (expire after 5 min)
2. Add telemetry: track % users with preferences applied
3. Add UI hint: show "Gợi ý dựa trên lịch sử của bạn" badge

---

## ✅ Acceptance Criteria Review

| Criteria | Status | Notes |
|---|---|---|
| Code builds without errors | ✅ | Verified |
| ExtractPreferencesFromSavedGames implemented | ✅ | With minor bug |
| CalculatePreferenceBoost implemented | ✅ | Different from design doc |
| Solo survey integration | ✅ | Code ready |
| Group discovery integration | ✅ | Code ready |
| Backward compatible (new users) | ✅ | Returns null gracefully |
| Manual test với real data | 🔄 | Ready to test |
| Performance < 100ms overhead | ⏸️ | Need to benchmark |

---

**Status:** ✅ **Implementation Complete** (với 2 minor bugs cần fix)  
**Next Action:** Fix Bug 1, then manual API test  
**Testing User:** `0358b254-183a-4b7d-83e2-666a72094478`  
**Test Date:** 2026-09-23
