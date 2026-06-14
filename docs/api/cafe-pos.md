# CafePosController

**Base route:** `/api/cafes/{cafeId}/pos`  
**Controller:** `CafePosController.cs`  
**Role:** Manager (chủ quán) hoặc CafeStaff (đã gắn quán)

API vận hành quầy: sơ đồ bàn, tra barcode hộp game, bắt đầu/kết thúc phiên chơi (`ActiveSessions`). Dữ liệu này cung cấp `estimatedWaitMinutes` cho `GET /api/cafes/nearby`.

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/tables` | GET | Sơ đồ bàn realtime |
| `/boxes` | GET | Danh sách hộp game (barcode + status) |
| `/boxes/by-barcode/{barcode}` | GET | Tra cứu sau khi quét POS |
| `/sessions/active` | GET | Phiên đang chơi |
| `/sessions` | POST | Giao game cho bàn (bắt đầu session) |
| `/sessions/{sessionId}/end` | POST | Trả game / kết thúc session |

---

## POST /api/cafes/{cafeId}/pos/sessions

POS quét barcode và chọn bàn → tạo `ActiveSession`, đặt hộp `InUse`, bàn `InUse` nếu đang trống.

```json
{
  "cafeTableId": "guid",
  "barcode": "BV-bbbbbbbb-xxxxxxxx-002"
}
```

**Response 201:** `ActiveSessionDto` — `startedAt`, `elapsedMinutes`, `estimatedRemainingMinutes`, `defaultPlayTimeMinutes`.

**Lỗi:** `404` bàn/barcode; `409` hộp không Available hoặc đã có session; bàn Reserved/Event.

---

## POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/end

Kết thúc phiên: hộp → `Available`; bàn → `Available` nếu không còn session khác trên bàn đó.

---

## Luồng test (discovery + chờ game)

```powershell
# 1. Login staff/manager
# 2. POST .../pos/sessions  (giao game → InUse)
# 3. GET /api/cafes/nearby?latitude=...&longitude=...&gameTemplateId=...
#    → selectedGameAvailabilityStatus: WaitingForGame, estimatedWaitMinutes: N
# 4. POST .../pos/sessions/{id}/end
#    → nearby lại hiển thị GameAvailable
```

---

## Inventory full response

`GET /api/cafes/{cafeId}/inventory` (Manager/Staff) trả thêm `boxes[]` với `barcode` và `status` từng hộp.
