# Booking Rating API

**Controller:** `BookingRatingController.cs`
**Base route:** `/api/bookings/{bookingId:guid}`
**Auth:** Player (đã đăng nhập — phải là lobby member của booking)

API voting + chấm điểm sau khi check-in/check-out (mobile gap #4 + #5). Tất cả endpoint yêu cầu user là member của lobby liên kết với booking.

> **Workflow:** Mobile hiển thị form voting/rating sau khi Staff gọi `POST /api/bookings/{id}/check-out`. Staff check-out tự động trigger `AggregateBookingOutcomesAsync` (xem [booking.md](./booking.md) §Aggregate Karma).

---

## REST Endpoints

### Submit No-show vote

**POST /api/bookings/{bookingId}/no-show-votes**

Mobile gọi khi user muốn vote các thành viên vắng mặt trong phiên chơi. Idempotent: vote lần 2 sẽ UPDATE vote trước.

**Auth:** Player (lobby member), voter ≠ absent.

**Validation window:** Voter chỉ được vote sau `CheckedInAt + 30 phút` (tránh vote ngay khi vừa check-in) và phải trước `ScheduleEndTime + 24h`.

**Request body:**

| Field | Type | Required | Validation |
|---|---|---|---|
| `bookingId` | guid | ✅ | Echo lại từ path |
| `absentMemberIds` | guid[] | ✅ | Danh sách thành viên vắng mặt — KHÔNG bao gồm chính voter |
| `votedAt` | datetime | ❌ | UTC — default `Now` |

```json
{
  "bookingId": "uuid",
  "absentMemberIds": ["userId-A", "userId-B"],
  "votedAt": "2026-08-01T20:30:00Z"
}
```

**Response 200:**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Gửi phiếu vote vắng mặt thành công.",
  "data": {
    "bookingId": "uuid",
    "voterId": "uuid",
    "absentMemberIds": ["userId-A", "userId-B"],
    "currentVoteCounts": {
      "userId-A": { "absentVotes": 2, "presentVotes": 1, "totalMembers": 4 },
      "userId-B": { "absentVotes": 2, "presentVotes": 1, "totalMembers": 4 }
    },
    "noShowConfirmedMembers": ["userId-A"],
    "processedAt": "2026-08-01T20:30:00Z"
  }
}
```

**Errors:**

| Code | Message |
|---|---|
| 400 | Dữ liệu không hợp lệ (vote chính mình / ngoài cửa sổ vote) |
| 401 | Thiếu / hết hạn JWT |
| 403 | Không phải lobby member |
| 404 | Không tìm thấy booking/lobby |
| 409 | Booking không ở CheckedIn |

---

### Submit Cross-rating

**POST /api/bookings/{bookingId}/ratings**

Mobile gọi khi user muốn chấm điểm thái độ các thành viên sau phiên chơi. Mỗi `ratedUserId` chỉ chấm 1 lần (vote lần 2 sẽ UPDATE).

**Auth:** Player (lobby member), voter ≠ rated.

**Request body:**

| Field | Type | Required | Validation |
|---|---|---|---|
| `bookingId` | guid | ✅ | Echo lại từ path |
| `ratings` | array | ✅ | 1-N items |
| `ratings[].ratedUserId` | guid | ✅ | Lobby member khác voter |
| `ratings[].attitude` | int | ✅ | 1-5 (thái độ chơi) |
| `ratings[].sportsmanship` | int | ✅ | 1-5 (tinh thần thể thao) |
| `ratings[].punctuality` | int | ✅ | 1-5 (đúng giờ) |
| `ratings[].comment` | string | ❌ | ≤500 chars |

```json
{
  "bookingId": "uuid",
  "ratings": [
    {
      "ratedUserId": "userId-A",
      "attitude": 5,
      "sportsmanship": 4,
      "punctuality": 5,
      "comment": "Chơi rất vui"
    }
  ]
}
```

**Response 200:**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Gửi chấm điểm thành công.",
  "data": {
    "bookingId": "uuid",
    "voterId": "uuid",
    "submittedAt": "2026-08-01T21:00:00Z",
    "ratedCount": 3
  }
}
```

**Errors:** `400` dữ liệu không hợp lệ; `401`; `403` không phải lobby member; `404`; `500`.

---

### Get rating status

**GET /api/bookings/{bookingId}/ratings/status**

Mobile dùng để hiển thị form chấm điểm (nếu `canRate=true`) hoặc ẩn form (nếu đã chấm hoặc quá hạn).

**Auth:** Player (lobby member).

**Response 200:**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Lấy trạng thái chấm điểm thành công.",
  "data": {
    "bookingId": "uuid",
    "canRate": true,
    "rateDeadline": "2026-08-02T20:00:00Z",
    "alreadyRated": false,
    "ratedUserIds": [],
    "missingMemberIds": ["userId-C", "userId-D"]
  }
}
```

| Field | Type | Mô tả |
|---|---|---|
| `canRate` | bool | Voter có trong cửa sổ vote + chưa rate (khi `alreadyRated=false`) |
| `rateDeadline` | datetime? | `ScheduleEndTime + 24h` — null nếu đã aggregate |
| `alreadyRated` | bool | Voter đã gửi rating trước đó |
| `ratedUserIds` | guid[] | Members voter đã rate (kể cả update) |
| `missingMemberIds` | guid[] | Members chưa được voter rate (gợi ý hiển thị UI) |

**Errors:** `401`; `403` không phải lobby member; `404`; `500`.

---

## Luồng tích hợp

```
1. Staff: POST /api/bookings/{id}/check-out (POS)
   → ActiveSession.Status = PAID
   → BookingService.CheckOutAsync tự gọi AggregateBookingOutcomesAsync
   → KarmaLog ghi nhận (cross-rating + no-show)

2. Mobile (mỗi lobby member):
   - GET /api/bookings/{id}/ratings/status → check canRate
   - POST /api/bookings/{id}/no-show-votes  (nếu có thành viên vắng)
   - POST /api/bookings/{id}/ratings        (chấm điểm attitude/sportsmanship/punctuality)
```

> **Note:** Mobile gọi voting/rating SAU khi Staff check-out. Trong phiên chơi (Booking.Status = CheckedIn nhưng session chưa PAID), user có thể vote no-show nhưng chưa thể aggregate cho đến khi check-out.

---

## Aggregate workflow (internal — không exposed qua HTTP)

Khi Staff `POST /api/bookings/{id}/check-out`, backend tự aggregate:

1. **Cross-rating:** Đọc `BookingRating` rows (`IsAggregated=false`). Tính `avgScore = (attitude + sportsmanship + punctuality) / 3` của tất cả voters cho mỗi `ratedUserId`. Delta: `(avgScore - 3.0) * 10`. Cộng/trừ `UserProfile.KarmaPoints` + ghi `KarmaLog` (Source=`PlayerCrossRating`, Category=`CrossRating`).
2. **No-show confirm:** UserId xuất hiện trong `>=50%+1 votes` → trừ 10 Karma (Source=`SystemAutomatic`, Category=`NoShow`).
3. **Forfeit deposit:** Nếu user no-show có `BookingDeposit` ở `Paid` + `RefundPolicy=None` → `Status=Forfeited` + KarmaLog audit.
4. **Idempotent:** Set `IsAggregated=true` cho tất cả rating rows. Staff check-out lại nhiều lần OK.

**Aggregate signature:**
```csharp
Task<BookingRatingAggregationResultDto> AggregateBookingOutcomesAsync(Guid bookingId);
```

**Audit (`KarmaLog` table):**

| Field | Value |
|---|---|
| `UserId` | UserId bị ảnh hưởng |
| `Source` | `PlayerCrossRating` / `SystemAutomatic` |
| `ViolationCategory` | `CrossRating` / `NoShow` |
| `KarmaPointsChange` | Delta (int) |
| `RelatedLobbyId` | Correlation → `BookingId` |

---

## Liên quan

- [booking.md](./booking.md) — BookingController, sessions, SignalR events
- [lobby.md](./lobby.md) — Lobby member / host relation
- [karma-log.md](./karma-log.md) — KarmaLog audit table
- [settlement.md](./settlement.md) — Settlement workflow
