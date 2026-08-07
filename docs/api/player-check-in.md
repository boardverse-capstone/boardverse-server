# PlayerCheckInController

**Base route:** `/api/check-in`
**Controller:** `PlayerCheckInController.cs`
**Role:** Player (đã đăng nhập)

API để player quét QR do POS tạo, tự check-in vào reservation của mình. Hỗ trợ chiều thứ 2 của luồng check-in 2 chiều (BR §21A.7): thay vì staff scan QR player, player scan QR POS.

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/scan-qr` | POST | Player scan QR code hiển thị trên POS → check-in vào reservation của mình |

---

## POST /api/check-in/scan-qr

Player scan QR code hiển thị trên POS → gửi token cho backend.

Backend lookup token trong DB, xác minh token còn hiệu lực, xác minh player là thành viên của reservation liên kết, tự động chọn bàn + hộp game còn trống, gọi nội bộ logic check-in hiện có và trả về thông tin ActiveSession.

**Token chỉ dùng được 1 lần** (consumed). Mỗi lần scan trả về:

- **200** nếu thành công hoặc idempotent replay (cùng user scan lại token đã dùng)
- **4xx** với lý do cụ thể

**Token format:** 16 ký tự alphanumeric uppercase, loại trừ `0/1/I/O`.

**Body — `PlayerScanTokenRequestDto`:**

```json
{
  "token": "ABCDEFGHJKLMNPQR"
}
```

**Response 200 — `PlayerScanTokenResponseDto`:**

```json
{
  "activeSessionId": "guid",
  "reservationId": "guid",
  "cafeId": "guid",
  "checkedInAt": "2026-08-07T10:05:00Z"
}
```

**Lỗi:**

| Status | Điều kiện |
|--------|-----------|
| `400` | Token không đúng định dạng (16-char alphanumeric uppercase, loại trừ 0/1/I/O). |
| `401` | Thiếu token, token hết hạn hoặc token không hợp lệ. |
| `403` | Player không phải thành viên của reservation liên kết với QR này. |
| `404` | Không tìm thấy token hoặc reservation liên kết không tồn tại. |
| `409` | Token đã được sử dụng trước đó (bởi user khác). |
| `410` | Token đã hết hạn TTL hoặc đã bị thu hồi (revoked). |
| `422` | Reservation không trong khung giờ check-in hoặc quán không còn bàn/hộp game trống. |
| `500` | Lỗi hệ thống không mong đợi. |

**Ví dụ curl:**

```bash
curl -X POST "https://api.boardverse.local/api/check-in/scan-qr" \
  -H "Authorization: Bearer <player-jwt>" \
  -H "Content-Type: application/json" \
  -d '{ "token": "ABCDEFGHJKLMNPQR" }'
```

---

## Token Lifecycle

```
Staff tạo token (TTL 30 phút mặc định)
    ↓
Player scan QR → gọi /scan-qr
    ↓
Token còn hiệu lực + Player là thành viên?
    ├── ❌ Token hết hạn → 410
    ├── ❌ Token đã revoked → 410
    ├── ❌ Token đã consumed bởi user khác → 409
    ├── ❌ Player không phải member → 403
    ├── ❌ Reservation không trong time window → 422
    ├── ❌ Không có bàn/box trống → 422
    └── ✅ Hợp lệ
            ├── Player scan lần 2 (cùng token, cùng user) → 200 idempotent replay
            └── Lần đầu scan
                    ├── Auto-pick bàn + box available
                    ├── Gọi CheckInByCodeAsync nội bộ
                    ├── Mark token consumed
                    └── 200 ActiveSession info
```

---

## Luồng 2-chiều (BR §21A.7)

| Chiều | Ai quét | Ai giữ QR | Endpoint gọi |
|--------|---------|-----------|--------------|
| 1 (canonical) | Staff | Player (ReservationCode) | `POST /cafes/{cafeId}/pos/check-in` |
| 2 (mới) | Player | Staff (PosCheckInToken) | `POST /check-in/scan-qr` |

**Ưu điểm chiều 2:**
- Player cầm điện thoại dễ scan hơn staff cầm laptop quét
- Hữu ích cho demo không có máy POS chuyên dụng
- Staff không cần nhập mã thủ công

**Token TTL:** Mặc định 30 phút. Hết hạn → tự động bị từ chối, player phải nhờ staff tạo mã mới.

---

## Liên quan

- [cafe-pos.md](./cafe-pos.md) — endpoint `POST /cafes/{cafeId}/pos/check-in-tokens` (tạo QR token)
- [reservation.md](./reservation.md) — Reservation entity và check-in time window
