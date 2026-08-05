# ActiveSessionController

> **DEPRECATED (05/08/2026):** Controller này đã được **xóa khỏi codebase**. Toàn bộ endpoint `/api/cafes/{cafeId}/sessions/*` cũ đã được gộp vào [CafePosController](./cafe-pos.md) dưới base route `/api/cafes/{cafeId}/pos/sessions/*`.

Tài liệu này được giữ lại làm **lịch sử tham chiếu** cho các phiên bản trước.

---

## Mapping endpoint cũ → mới

| Endpoint cũ (đã xóa) | Endpoint mới |
|----------------------|--------------|
| `GET /api/cafes/{cafeId}/sessions/{sessionId}` | `GET /api/cafes/{cafeId}/pos/sessions/{sessionId}` |
| `POST /api/cafes/{cafeId}/sessions/{sessionId}/games` | `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/games` |
| `POST /api/cafes/{cafeId}/sessions/{sessionId}/checkout` | `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/checkout` |
| `POST /api/cafes/{cafeId}/sessions/{sessionId}/partial-checkout` | `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/partial-checkout` |
| `POST /api/cafes/{cafeId}/sessions/{sessionId}/guest-slots` | `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/guest-slots` |
| `POST /api/cafes/{cafeId}/sessions/{sessionId}/pay` | `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/pay` |
| `POST /api/cafes/{cafeId}/sessions/{sessionId}/merge` | `POST /api/cafes/{cafeId}/pos/sessions/{sourceSessionId}/merge` |
| `POST /api/cafes/{cafeId}/sessions/{sessionId}/members/add` | `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/members/add` |
| `POST /api/cafes/{cafeId}/sessions/{sessionId}/inventory-loss` | `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/inventory-loss` |
| `GET /api/cafes/{cafeId}/sessions/alternative-cafes` | Vẫn giữ — xem chi tiết bên dưới |

---

## Endpoint còn hoạt động

### GET /api/cafes/{cafeId}/sessions/alternative-cafes

**Public** (không cần token). Gợi ý quán thay thế khi quán mục tiêu hết chỗ (Exception 1).

**Query:**

| Param | Type | Required | Mô tả |
|-------|------|----------|--------|
| `gameTemplateId` | Guid | ✅ | Tựa game |
| `memberCount` | int | ✅ | Số người cần chỗ |
| `scheduledTime` | DateTime | ✅ | Giờ hẹn |

**Response 200:** danh sách quán gợi ý (cùng game + còn đủ chỗ + trong khu vực lân cận).

---

## Lý do gộp controller

1. **Một entry point duy nhất cho POS** — nhân viên không cần nhớ 2 base route (`/pos/...` vs `/sessions/...`).
2. **Authorize attribute thống nhất** — `[Authorize(Roles = "Manager,CafeStaff")]` ở 1 chỗ.
3. **Shared service injection** — `ICafePosService` + `IActiveSessionService` ở cùng controller, dễ DI.
4. **Idempotency + Nonce** áp dụng nhất quán cho cả check-in và các flow khác.

## Liên quan

- [cafe-pos.md](./cafe-pos.md) — controller mới, có đầy đủ endpoint + 10 luồng nghiệp vụ.
- **State machine canonical**: [boardverse.mdc §V](../../.cursor/rules/boardverse.mdc) — đặc tả transition cho `ActiveSession`.
