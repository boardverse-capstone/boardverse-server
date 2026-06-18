# MatchController (Elo & match consensus)

**Base route:** `/api/v1/matches`  
**Controller:** `MatchController.cs`

| Endpoint | Method | Role |
|----------|--------|------|
| `/results/lobbies/{lobbyId}` | GET | Player (thành viên phòng) |
| `/results` | POST | Player (thành viên phòng) |

Game hỗ trợ nhập kết quả khi có thể loại **Đối kháng** (`doi-khang`) hoặc **Chiến thuật** (`chien-thuat`) — AC 4.1.

Elo cập nhật vào `UserProfiles.GlobalElo` (không phải bảng `Users`).

---

## GET /api/v1/matches/results/lobbies/{lobbyId}

Trạng thái đồng thuận kết quả trong phòng (AC 4.1, 4.2).

**Response 200:**

```json
{
  "statusCode": 200,
  "message": "Match result status retrieved successfully",
  "data": {
    "lobbyId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "gameTemplateId": "22222222-2222-2222-2222-222222222222",
    "gameName": "Catan",
    "supportsMatchResults": true,
    "consensusStatus": "AwaitingSubmissions",
    "submittedCount": 1,
    "requiredCount": 4,
    "conflictReason": null,
    "availableOutcomes": [
      { "outcome": "Win", "label": "Thắng" },
      { "outcome": "Loss", "label": "Thua" },
      { "outcome": "Draw", "label": "Hòa" }
    ],
    "submissions": [
      { "userId": "...", "username": "player1", "outcome": "Win", "isCurrentUser": true }
    ]
  }
}
```

| `consensusStatus` | Ý nghĩa |
|-------------------|---------|
| `AwaitingSubmissions` | Chưa đủ thành viên gửi kết quả |
| `Conflict` | Mâu thuẫn (vd. cả hai bên đều Win) — cần nhập lại |
| `Finalized` | Đồng thuận 100%, Elo đã cập nhật |

---

## POST /api/v1/matches/results

Gửi/cập nhật kết quả từ góc nhìn người chơi.

### Request

```json
{
  "lobbyId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "outcome": "Win"
}
```

| `outcome` | UI |
|-----------|-----|
| `Win` | Thắng |
| `Loss` | Thua |
| `Draw` | Hòa |

### Quy tắc đồng thuận (AC 4.2)

- **Hòa:** tất cả thành viên gửi `Draw`.
- **Thắng/Thua:** đúng **một** người `Win`, các người còn lại `Loss`.
- Trường hợp khác → `Conflict`, cho phép gửi lại (upsert).

### Response 200 — đã finalize (AC 4.3)

```json
{
  "statusCode": 200,
  "message": "Match result submitted successfully",
  "data": {
    "lobbyId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "consensusStatus": "Finalized",
    "submittedCount": 4,
    "requiredCount": 4,
    "matchHistoryId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    "eloUpdates": [
      {
        "userId": "...",
        "reportedOutcome": "Win",
        "eloBefore": 1200,
        "eloAfter": 1216,
        "eloDelta": 16
      }
    ]
  }
}
```

### Thuật toán Elo

- Công thức expected score chuẩn: `1 / (1 + 10^((R_opponent - R_self)/400))`.
- **K-factor** theo rating hiện tại: `<2100 → 32`, `<2400 → 24`, `≥2400 → 16`.
- Multi-player: actual score = 1 (thắng), 0 (thua), 0.5 (hòa toàn phòng); expected = trung bình vs các đối thủ còn lại.
- Lịch sử lưu `MatchHistories` + `MatchHistoryParticipants`.

**Lỗi:** `400` game không competitive / phòng chưa eligible, `409` đã finalize.

### PowerShell mẫu

```powershell
curl.exe -X POST "http://localhost:5022/api/v1/matches/results" ^
  -H "Authorization: Bearer YOUR_TOKEN" ^
  -H "Content-Type: application/json" ^
  -d "{\"lobbyId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"outcome\":\"Win\"}"
```
