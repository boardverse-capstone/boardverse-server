# Checklist cho FE khi đọc API Documentation

## ✅ Thông tin đầy đủ

- [x] Base URL (dev/staging/production)
- [x] Authentication header format
- [x] HTTP methods + endpoints
- [x] Request body schema + examples
- [x] Response body schema + examples (success + errors)
- [x] HTTP status codes (200, 400, 401, 403, 404, 500)
- [x] TypeScript types (copy-paste ready)
- [x] Error handling examples
- [x] Frontend integration guide (React component)
- [x] Use cases thực tế

## ⚠️ Cần làm rõ thêm

### 1. Authentication Flow
**Thiếu:** Link tới endpoint login để lấy token

**Thêm vào docs:**
```markdown
## Authentication

Tất cả Discovery APIs yêu cầu JWT token:

```http
Authorization: Bearer <your_jwt_token>
```

**Lấy token:**
1. Login: `POST /api/auth/login` với `{ usernameOrEmail, password }`
2. Nhận `data.token` trong response
3. Dùng token cho mọi API calls
4. Token hết hạn sau 7 ngày → dùng `refreshToken` để gia hạn

**Xem chi tiết:** `docs/api/auth.md`
```

### 2. Response Format Consistency
**Vấn đề:** 2 formats khác nhau giữa các docs:
- Format 1 (WEIGHT_FILTER): `{ success, data, error }`
- Format 2 (SOLO_PERSONALIZED): `{ statusCode, message, data }`

**Hỏi BE:** Format nào là chuẩn thực tế?

**Recommendation:** Thống nhất dùng format 2 (có `statusCode` rõ ràng hơn)

### 3. Pagination
**Làm rõ:** API không support pagination thực sự, chỉ limit top N results

**Thêm vào docs:**
```markdown
## Pagination

**Lưu ý:** API **không hỗ trợ** pagination (page 2, 3, ...).

- `pageSize`: Giới hạn số games trả về (max 50)
- Luôn trả top N games có score cao nhất
- Nếu cần xem thêm → điều chỉnh filters (loại bớt categories, weight ranges)
```

### 4. Rate Limiting
**Thêm:**
```markdown
## Rate Limiting

- **Authenticated users:** No limit
- **Anonymous:** N/A (Discovery APIs require auth)
- **Burst protection:** Max 100 requests/minute per user
```

### 5. Field Clarifications
**Thêm vào "Field Details" table:**

| Field | `null` | `[]` empty array | Omit field | Behavior |
|---|---|---|---|---|
| `weightRanges` | Skip filter | Skip filter | Skip filter | Same |
| `categoryIds` | Skip filter | Skip filter | Skip filter | Same |
| `preferredDurations` | Skip filter | Skip filter | Skip filter | Same |

---

## 🎯 Câu hỏi FE có thể hỏi

### Q1: "Token hết hạn thì sao?"
**A:** Gọi `POST /api/auth/refresh-token` với `refreshToken` → nhận `token` mới.

### Q2: "API trả empty array có nghĩa là gì?"
**A:** Không có game nào match filters → hiển thị "Không tìm thấy game phù hợp. Thử bỏ bớt filter."

### Q3: "`weightRanges: [1, 3]` nghĩa là gì?"
**A:** OR logic → game có weight trong range 1 (≤2.0) HOẶC range 3 (3.01-3.5).

### Q4: "Score 87.0 tính ra sao?"
**A:** 
- Base score (filters): 75 pts
- Personalization boost (saved games): +12 pts
- Play history penalty: 0 pts (chưa triển khai)
- **Total:** 87 pts

### Q5: "`userProfile: null` khi nào?"
**A:** User có <3 saved games → chưa đủ dữ liệu để xây dựng profile.

### Q6: "400 error 'PreferredDurations không hợp lệ' nghĩa là gì?"
**A:** Gửi sai value. Chỉ chấp nhận: `"under30"`, `"30to60"`, `"over60"` (lowercase, no space).

### Q7: "Làm sao biết game nào user đã lưu?"
**A:** Check field `isSaved: true` trong response.

### Q8: "`hasOpenLobby: true` có nghĩa gì?"
**A:** Có lobby đang mở cho game này → hiển thị badge "Có nhóm đang chơi" → CTA "Tham gia ngay".

---

## 🚀 Action Items cho BE

1. **Thống nhất response format** (`statusCode` + `message` + `data`)
2. **Thêm Authentication section** vào đầu mỗi API doc (link tới `/api/auth/login`)
3. **Clarify pagination** (không support, chỉ top N)
4. **Thêm rate limiting info** (nếu có)
5. **Thêm field comparison table** (`null` vs `[]` vs omit)
6. **Sync error codes** giữa các docs (hiện tại có `VALIDATION_ERROR`, `UNAUTHORIZED`, `INTERNAL_SERVER_ERROR` nhưng không nhất quán)

---

## ✅ Kết luận

**FE có thể implement được ngay:** ✅ Có  
**Cần làm rõ thêm:** 5 điểm trên  
**Risk:** Low (chỉ cần clarify format + auth flow)

**Recommendation:** Tạo 1 file `docs/api/_common.md` chứa:
- Authentication flow
- Response format standard
- Error codes dictionary
- Common headers
- Rate limiting policy

→ Các API docs khác chỉ cần link tới `_common.md`
