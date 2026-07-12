# CafePosController

**Base route:** `/api/cafes/{cafeId}/pos`  
**Controller:** `CafePosController.cs`  
**Role:** Manager hoặc CafeStaff

API vận hành quầy: bàn, kho hộp game, phiên chơi, kiểm kê, khách vô danh, thanh toán một phần và thanh toán toàn bộ.

> **Lưu ý:** Một số endpoint liên quan đến phiên chơi đang được tách sang `ActiveSessionController` với base route `/api/cafes/{cafeId}/sessions`. Xem bảng bên dưới để biết chính xác từng thao tác thuộc controller nào.

||| Endpoint | Method | Mô tả | Controller |
||----------|--------|--------|------------|
||| `/tables` | GET | Sơ đồ bàn realtime | `CafePosController` |
||| `/boxes` | GET | Danh sách hộp game | `CafePosController` |
||| `/boxes/by-barcode/{barcode}` | GET | Tra cứu hộp sau khi quét POS | `CafePosController` |
||| `/sessions/active` | GET | Phiên đang chơi | `CafePosController` |
||| `/sessions` | POST | Giao game cho bàn — bắt đầu phiên chơi | `CafePosController` |
||| `/sessions/{sessionId}/end` | POST | Trả game / kết thúc phiên chơi | `CafePosController` |
||| `/sessions/{sessionId}/checkout` | POST | Thanh toán toàn bộ sau kiểm kê linh kiện | `ActiveSessionController` |
||| `/sessions/{sessionId}/guest-slots` | POST | Thêm khách vô danh | `ActiveSessionController` |
||| `/sessions/{sessionId}/partial-checkout` | POST | Thanh toán một phần khi có người về sớm | `ActiveSessionController` |

---

## POST /api/cafes/{cafeId}/pos/sessions

POS quét barcode và chọn bàn → tạo `ActiveSession`, đặt hộp `InUse`, bàn `InUse` nếu đang trống.

**Body mẫu:**

```json
{
  "cafeTableId": "table-id",
  "barcode": "BV-bbbbbbbb-xxxxxxxx-002",
  "bookingId": "booking-id",
  "lobbyId": "lobby-id",
  "initialMemberUserIds": ["user-id-1"]
}
```

**Response 201:** `ActiveSessionDto` — `startedAt`, `elapsedMinutes`, `estimatedRemainingMinutes`, `defaultPlayTimeMinutes`.

**Lỗi:** `404` bàn/barcode; `409` hộp không Available, đã có session, hoặc bàn Reserved/Event.

---

## POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/end

Kết thúc phiên chơi — trả hộp game và giải phóng bàn nếu không còn session khác.

**Response 200:** Phiên đã đóng; hộp về Available; bàn về Available khi không còn session trên bàn đó.

**Lỗi:** `404` không tìm thấy phiên; `500` lỗi hệ thống.

---

## Luồng test

```powershell
# 1. Login staff/manager
# 2. POST .../pos/sessions  (giao game → InUse)
# 3. GET /api/cafes/nearby?latitude=...&longitude=...&gameTemplateId=...
#    → selectedGameAvailabilityStatus: WaitingForGame, estimatedWaitMinutes: N
# 4. POST .../pos/sessions/{id}/end
#    → nearby lại hiển thị GameAvailable
```
