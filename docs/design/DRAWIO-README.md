# BoardVerse System Architecture — Hướng dẫn Insert vào Draw.io

> File đính kèm: `boardverse-architecture.drawio` (file XML chuẩn Draw.io, có thể mở trực tiếp).

File này chứa **2 diagram (2 tab)** trong cùng 1 file Draw.io:

| Tab | Diagram | Mục đích | Kích thước |
|-----|---------|----------|------------|
| `1 - Detailed Architecture` | Diagram chi tiết — đầy đủ 3 swimlanes, 21 boxes, 13 connectors | Phân tích từng lớp (Presentation, Application, Domain, Infrastructure) cho stakeholder kỹ thuật | Lớn (2200×1400) |
| `2 - High-Level Overview` | Diagram đơn giản — 3-tier (Client / Application / Third Party) | Tổng quan hệ thống cho slide, presentation, executive review | Nhỏ gọn (1400×900) |

---

## Cách 1 — Mở file drawio có sẵn (Khuyến nghị)

### 1.1. Trên web (app.diagrams.net)
1. Truy cập **https://app.diagrams.net** (Draw.io Online).
2. Menu **File → Open from Device** → chọn file `boardverse-architecture.drawio`.
3. Diagram sẽ hiển thị ngay với tất cả boxes, swimlanes, connectors.
4. Bạn có thể edit, thêm box mới, sửa text, đổi màu tùy ý.
5. Lưu lại: **File → Save** (lưu về máy) hoặc **File → Export as** PNG/SVG/PDF.

### 1.2. Trong VS Code (Draw.io Integration extension)
1. Cài extension **Draw.io Integration** (hediet.vscode-drawio).
2. Mở file `boardverse-architecture.drawio` trực tiếp trong VS Code.
3. Edit và lưu như file bình thường.

### 1.3. Trên desktop (Draw.io Desktop app)
1. Tải Draw.io Desktop từ https://github.com/jgraph/drawio-desktop/releases.
2. Mở app → **File → Open** → chọn file `.drawio`.

---

## Cách 2 — Copy từng phần (nếu muốn tự vẽ lại)

Diagram đã được chia thành **3 swimlanes lớn** theo chiều dọc. Bạn có thể dùng bảng dưới đây để tự tạo từng box trong Draw.io.

### Swimlane 1 — CLIENT APPLICATIONS (top, màu xám nhạt `#F3F4F6`)

| Box | Nội dung | Style |
|-----|----------|-------|
| Player Mobile App | "Player Mobile App (iOS / Android)\n• Auth & Profile\n• Lobby / Match / Tournament\n• Booking / Deposit\n• Friend / Karma\n• SignalR real-time lobby" | Blue `#DBEAFE` / border `#1D4ED8` |
| Cafe POS Dashboard | "Cafe POS Dashboard (Web SPA)\n• Cafe Profile / Inventory\n• POS / Active Session\n• Staff / Settlement\n• Tournament POS\n• SePay Account Linking" | Yellow `#FEF3C7` / border `#B45309` |
| Cafe Partner Portal | "Cafe Partner Portal (Web SPA)\n• Cafe onboarding application\n• Manager account\n• Cafe profile editing" | Yellow `#FEF3C7` / border `#B45309` |
| Admin Web Portal | "Admin Web Portal (Browser)\n• User moderation\n• Master catalog\n• Cafe operational\n• SePay account mgmt\n• System configuration" | Pink `#FCE7F3` / border `#9D174D` |

### Swimlane 2 — BACKEND CORE (giữa, màu xanh lá nhạt `#ECFDF5`)

| Box | Layer | Nội dung | Style |
|-----|-------|----------|-------|
| Presentation Layer | BoardVerse.API | "• 36 API Controllers\n• LobbyHub (SignalR)\n• Swagger / Swagger UI\n• ApiExceptionMiddleware\n• JWT Bearer Authentication\n• Model Validation Filter\n• CORS / Static Files\n• System.Text.Json + Newtonsoft" | Light green `#A7F3D0` |
| Application / Service Layer | BoardVerse.Services | "• AuthService, UserService\n• CafeService, CafePosService\n• LobbyService, BookingService\n• TournamentService, MatchService\n• PaymentService, SePayAccountService\n• FriendService, KarmaRatingService\n• 10+ Background Jobs:\n  - LobbyTimeoutJob\n  - BookingDepositExpiryJob\n  - KarmaWindowJob\n  - SettlementRetryJob\n  - TournamentReminderJob\n  - TournamentNoShowDetectionJob" | Green `#BBF7D0` |
| Domain Layer | BoardVerse.Core | "• Entities (User, Cafe, Lobby, Booking, Match, Tournament, ActiveSession…)\n• DTOs (Request / Response)\n• Enums (BookingStatus, LobbyStatus, TournamentStatus…)\n• Custom Exceptions\n• Helpers (Karma, Settlement, GeoLocation)\n• Settings (Jwt, Brevo, SePay)" | Darker green `#86EFAC` |
| Infrastructure Layer | BoardVerse.Data | "• BoardVerseDbContext (EF Core 8)\n• 70+ Repositories\n• Entity Type Configurations\n• EF Core Migrations\n• Npgsql + NetTopologySuite\n• HttpClient (Google, Brevo, BGG, SePay)\n• StackExchange.Redis\n• BCrypt" | Darkest green `#4ADE80` |

### Swimlane 3 — EXTERNAL / 3RD-PARTY SYSTEMS (bottom, màu đỏ nhạt `#FEF2F2`)

#### Cột DATABASE
| Box | Style |
|-----|-------|
| Neon PostgreSQL (Serverless on AWS) + PostGIS extension | Red `#FECACA` |
| Redis (Distributed Cache, optional) | Red `#FECACA` |
| PostGIS (Spatial extension) — Geo queries | Red `#FECACA` |

#### Cột PAYMENT & AUTH
| Box | Style |
|-----|-------|
| SePay Payment Gateway — pgapi.sepay.vn / pay.sepay.vn / HMAC-SHA256 | Red `#FCA5A5` |
| VietQR (Fallback dev/CI) | Red `#FCA5A5` |
| JWT (HS256 self-signed) | Red `#FCA5A5` |

#### Cột COMMUNICATION & DATA
| Box | Style |
|-----|-------|
| Brevo (Sendinblue) — Transactional Email — api.brevo.com | Red `#FCA5A5` |
| Google OAuth 2.0 — Social login & linking | Red `#FCA5A5` |
| BoardGameGeek (BGG) XML API v2 — Game catalog import | Red `#FCA5A5` |

#### Row DEPLOYMENT / HOSTING
| Box | Style |
|-----|-------|
| Render.com — Web Service hosting, reads PORT env var | Light red `#FEE2E2` |
| Cloudflare — DNS + CDN in front of API | Light red `#FEE2E2` |
| BCrypt — Password hashing (in-process library) | Light red `#FEE2E2` |

---

## Connectors (mũi tên) cần vẽ

| Từ | Đến | Nhãn | Màu |
|----|-----|------|-----|
| Player Mobile App | Presentation Layer | HTTPS / REST + JSON + SignalR | Blue `#1D4ED8` |
| Cafe POS Dashboard | Presentation Layer | HTTPS / REST + JSON | Brown `#B45309` |
| Cafe Partner Portal | Presentation Layer | HTTPS / REST + JSON | Brown `#B45309` |
| Admin Web Portal | Presentation Layer | HTTPS / REST + JSON | Pink `#9D174D` |
| Presentation Layer | Application Layer | "calls" | Green `#047857` |
| Application Layer | Domain Layer | "uses entities" | Green `#15803D` |
| Application Layer | Infrastructure Layer | "via DI" | Green `#166534` |
| Infrastructure Layer | Neon PostgreSQL | "Npgsql / EF Core" | Red `#B91C1C` |
| Infrastructure Layer | Redis | "StackExchange.Redis" | Red `#B91C1C` |
| Application Layer | SePay | "HttpClient / HMAC-SHA256" | Red `#991B1B` |
| Presentation Layer | Brevo | "HTTP" | Red `#991B1B` |
| Presentation Layer | Google | "OAuth 2.0" | Red `#991B1B` |
| Infrastructure Layer | BGG | "HTTP" | Red `#991B1B` |

---

## Tips khi edit trong Draw.io

1. **Snap to grid:** View → Grid để boxes canh đều.
2. **Snap & Guides:** Format → enable Snap để box tự canh.
3. **Group khi cần:** Select nhiều boxes → Ctrl+G để gom nhóm, dễ di chuyển cả layer.
4. **Thay đổi style nhanh:** Right-click → Style → chỉnh fillColor, strokeColor, fontSize.
5. **Export PNG/SVG:** File → Export as → PNG (cho docx) / SVG (cho web).
6. **Embed vào docx:** File → Export as → PNG, rồi Insert → Picture trong Word.

---

## File output

- **Draw.io XML:** `docs/design/boardverse-architecture.drawio`
- **Markdown report:** `docs/design/system-architecture-1.1.md`
