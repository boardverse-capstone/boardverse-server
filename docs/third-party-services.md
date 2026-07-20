# Danh sách dịch vụ bên thứ 3 — BoardVerse

Tài liệu liệt kê các dịch vụ / API bên ngoài mà backend BoardVerse đang tích hợp, theo nhóm **Authentication**, **Storage**, **AI APIs** và các dịch vụ liên quan.

> Cập nhật theo codebase: `BoardVerse.API`, `BoardVerse.Services`, `appsettings.json`.

---

## 1. Authentication

### 1.1 Google OAuth 2.0 (Google Sign-In)

- **Mục đích:** Đăng nhập và liên kết tài khoản bằng Google ID token
- **Config:** `Authentication:Google:ClientId`, `ClientSecret`
- **Code:** `AuthService.cs`, package `Google.Apis.Auth`
- **Endpoint:** `https://oauth2.googleapis.com` (xác thực qua `GoogleJsonWebSignature.ValidateAsync`)
- **Bắt buộc:** Có (nếu bật login Google)

### 1.2 JWT (tự phát hành)

- **Mục đích:** Access token sau login / register / refresh
- **Config:** `JwtSettings:SecurityKey`, `ValidIssuer`, `ValidAudience`
- **Code:** `JwtBearer` middleware
- **Bắt buộc:** Có
- **Ghi chú:** Không phải SaaS bên thứ 3 — ký bằng secret nội bộ

### 1.3 BCrypt

- **Mục đích:** Hash mật khẩu tài khoản local
- **Code:** Package `BCrypt.Net-Next`
- **Bắt buộc:** Có
- **Ghi chú:** Thư viện, không phải cloud service

**API nội bộ liên quan:**

- `POST /api/auth/login`
- `POST /api/auth/google-login`
- `POST /api/auth/link-google`
- `POST /api/auth/refresh-token`

---

## 2. Storage / Database / Cache

### 2.1 Neon (PostgreSQL managed)

- **Mục đích:** Database chính — users, cafe, game, lobby, match, karma…
- **Config:** `ConnectionStrings:DefaultConnection`, `DATABASE_URL`, `NEON_CONNECTION`
- **Code:** Npgsql + EF Core
- **Host:** `*.aws.neon.tech`
- **Bắt buộc:** Có

### 2.2 PostGIS / NetTopologySuite

- **Mục đích:** Lưu và truy vấn tọa độ (GPS, quán gần bạn)
- **Code:** `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`, `GeoLocationHelper`
- **Bắt buộc:** Có (nếu dùng tính năng geo)

### 2.3 Redis

- **Mục đích:** Distributed cache — giới hạn login sai, cache system config
- **Config:** `Redis:ConnectionString`, `REDIS_URL`
- **Code:** `StackExchange.Redis`
- **Bắt buộc:** Không (có fallback memory cache)

### 2.4 In-memory cache

- **Mục đích:** Cache cục bộ khi chưa cấu hình Redis
- **Code:** `RedisServiceExtensions.cs` → `AddDistributedMemoryCache()`
- **Bắt buộc:** Fallback dev/local

**Không dùng object storage bên thứ 3:**

- Không có S3, Azure Blob, Cloudinary, Firebase Storage
- Ảnh quán (`BusinessLicenseImageUrl`, `SpaceImageUrls`) chỉ lưu URL do client gửi — backend không upload file

---

## 3. AI APIs

### Không có

- **Ghi chú:** Không tích hợp OpenAI, Azure OpenAI, Google Gemini, Anthropic Claude, Hugging Face hay dịch vụ AI/LLM nào khác.

---

## 4. Email & API ngoài khác

### 4.1 Brevo (trước Sendinblue)

- **Mục đích:** Email transactional
- **Config:** `Brevo:ApiKey`, `ApiBaseUrl`, `SenderEmail`, `SenderName`
- **Code:** `BrevoEmailService`
- **Endpoint:** `https://api.brevo.com`
- **Bắt buộc:** Có (nếu gửi email thật)

**Luồng email:**

- **Auth:** Mã xác minh email, mã đặt lại mật khẩu
- **Cafe partner:** Nhận đơn, duyệt/từ chối, tạo manager, kích hoạt quán

Chi tiết: [docs/api/auth.md](./api/auth.md), [docs/api/cafe-partner.md](./api/cafe-partner.md)

### 4.2 BoardGameGeek (BGG) XML API

- **Mục đích:** Tìm game, preview, import metadata/linh kiện (Admin)
- **Config:** `Bgg:ApiBaseUrl`, `ApiToken`
- **Code:** `BggApiClient`
- **Endpoint:** `https://boardgamegeek.com/xmlapi2`
- **Bắt buộc:** Có (nếu dùng import BGG)

Chi tiết: [docs/api/bgg.md](./api/bgg.md)

### 4.3 SePay (Payment gateway)

- **Mục đích:** Cổng thanh toán QR cho booking deposit + session payment
- **Config:** `SePay:MerchantId`, `SecretKey`, `ApiBaseUrl`, `Environment` (`Test` / `Production`)
- **Code:** `ISePayClient` → `SePayClient`, `IPaymentService`, `IPaymentGatewayService`
- **Endpoints:**
  - Checkout (cafe): `POST https://pgapi.sepay.vn/v1/checkout/init`
  - Checkout (boardverse central): `GET https://pay.sepay.vn/v1/checkout/init?…`
  - Webhook receive: `POST /api/payments/sepay/webhook`
- **Bắt buộc:** Có (production); dev có thể dùng VietQR fallback
- **Luồng thanh toán:**
  - Deposit payment → BoardVerse master merchant (gom tiền cọc)
  - Session payment → Cafe SePay merchant (POS tại quán)
- **Security:** HMAC-SHA256 signature với cùng field order cho checkout + webhook

Chi tiết: [docs/api/sepay-webhook.md](./api/sepay-webhook.md), [docs/api/sepay-account.md](./api/sepay-account.md), [.cursor/rules/sepay-payment-flow.mdc](../.cursor/rules/sepay-payment-flow.mdc)

---

## 5. Không sử dụng

- Payment: Stripe, PayPal, VNPay, MoMo (chỉ dùng SePay + VietQR fallback)
- SMS / OTP: Twilio
- Push notification: Firebase FCM, OneSignal
- File upload / CDN: AWS S3, Cloudinary, Imgur
- AI / LLM APIs

---

## 6. Endpoint & host bên thứ 3

- `https://oauth2.googleapis.com` — Google token validation
- `https://api.brevo.com` — Brevo email
- `https://boardgamegeek.com/xmlapi2` — BGG game catalog
- `https://pgapi.sepay.vn` / `https://pay.sepay.vn` — SePay payment gateway
- `*.aws.neon.tech` — Neon PostgreSQL
- `<REDIS_URL host>` — Redis (tùy chọn, production)

---

## 7. Biến môi trường

### `DATABASE_URL` / `NEON_CONNECTION`

- **Dịch vụ:** Neon PostgreSQL

### `REDIS_URL` / `REDIS_CONNECTION`

- **Dịch vụ:** Redis

### `ConnectionStrings__DefaultConnection`

- **Dịch vụ:** Neon (override connection)

### `JwtSettings__SecurityKey`

- **Dịch vụ:** JWT signing

### `Authentication__Google__ClientId`

- **Dịch vụ:** Google OAuth

### `Brevo__ApiKey`

- **Dịch vụ:** Brevo email

### `Bgg__ApiToken`

- **Dịch vụ:** BoardGameGeek API

### `SePay__MerchantId` / `SePay__SecretKey` / `SePay__ApiBaseUrl` / `SePay__Environment`

- **Dịch vụ:** SePay payment gateway
- **Environment:** `Test` (sandbox) / `Production`
- **Lưu ý:** Config từ DB (`SePayAccount`) sẽ override config từ appsettings ở runtime

---

## 8. Ghi chú triển khai

### Dev local

- **Database:** Neon (branch test)
- **Redis:** Thường tắt → dùng memory cache
- **Email / BGG:** Cần config nếu test gửi email / import BGG thật

### Production

- **Database:** Neon
- **Redis:** Nên bật `REDIS_URL`
- **Email / BGG:** Brevo + BGG theo `appsettings` hoặc env

---

## 9. Tóm tắt

- **Authentication:** Google OAuth, JWT (nội bộ), BCrypt
- **Storage:** Neon PostgreSQL + PostGIS, Redis (optional), không file storage bên thứ 3
- **AI APIs:** Không
- **Khác:** Brevo (email), BoardGameGeek API (game catalog), SePay (payment gateway)
