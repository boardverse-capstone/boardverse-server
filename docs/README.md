# BoardVerse Documentation

Tài liệu tổng hợp cho hệ thống BoardVerse — board game center management với matchmaking trực tuyến, POS vận hành tại quán, payment gateway SePay và tournament module.

---

## Mục lục

### 📐 Business & Architecture

- [Business context & rules](../.cursor/rules/boardverse-business-context.mdc) — Business rules (BR-01..BR-18), state machines
- [BoardVerse consolidated](../.cursor/rules/boardverse.mdc) — Phiên bản hợp nhất: kiến trúc + BR + happy/exception path + state machine
- [API error messages](../.cursor/rules/api-error-messages.mdc) — Quy ước thông điệp lỗi
- [Project structure](../.cursor/rules/project-structure.mdc) — Coding conventions
- [Neon migration guide](../.cursor/rules/neon-migration-guide.mdc) — Quy trình migration Neon DB
- [SePay payment flow](../.cursor/rules/sepay-payment-flow.mdc) — Luồng thanh toán SePay + retry/fallback

### 📄 Tài liệu nghiệp vụ — Module

- [Tournament business flow](./tournament-business-flow.md) — Format Swiss + Final, Elo integration, shortage handling
- [Tournament presentation](./tournament-presentation.md) — High-level cho leadership review
- [Lobby lifecycle presentation](./lobby-lifecycle-presentation.md) — Luồng phòng chờ trực tuyến
- [Game session billing presentation](./game-session-billing-presentation.md) — BR-15, BR-16, BR-17 (billing & settlement)
- [Table reservation lifecycle](./table-reservation-lifecycle-presentation.md)

### 🔌 API Reference — `docs/api/`

#### Auth & User
- [auth.md](./api/auth.md) — `AuthController` (10 endpoints)
- [user-profile.md](./api/user-profile.md) — `UserProfileController` (10 endpoints)
- [user-management.md](./api/user-management.md) — `UserManagementController` Admin (8 endpoints)
- [user-ratings.md](./api/user-ratings.md) — `UserRatingController` — Karma cross-rating
- [protected.md](./api/protected.md) — `ProtectedController` — token smoke test

#### Cafe & Manager
- [cafe.md](./api/cafe.md) — `CafeController` (8 endpoints, bao gồm SePay config)
- [cafe-inventory.md](./api/cafe-inventory.md) — `CafeInventoryController` (8 endpoints)
- [cafe-pos.md](./api/cafe-pos.md) — `CafePosController` — POS session start/end
- [cafe-partner.md](./api/cafe-partner.md) — `CafePartnerApplicationController`, `AdminCafePartnerApplicationController`, `ManagerCafeProfileController`
- [manager.md](./api/manager.md) — `ManagerController` + `ManagerCafeProfileController`
- [staff.md](./api/staff.md) — `StaffController`

#### Boards & Master Catalog
- [board-games.md](./api/board-games.md) — `BoardGameController` (Public catalog)
- [master-games.md](./api/master-games.md) — `MasterGameController` (Manager view)
- [admin-master-catalog.md](./api/admin-master-catalog.md) — `AdminMasterCatalogController` (Categories + components + master games)
- [bgg.md](./api/bgg.md) — `BggController` (Admin — BoardGameGeek import)

#### Lobby & Match
- [lobby.md](./api/lobby.md) — `LobbyController` + SignalR Hub
- [matches.md](./api/matches.md) — `MatchController` (Elo & match consensus)

#### Active Session (POS)
- [active-session.md](./api/active-session.md) — `ActiveSessionController` (12 endpoints — phần lớn POS workflow)

#### Payment
- [booking.md](./api/booking.md) — Booking flow & payment (cập nhật → không còn `BookingController`, đi qua Payment)
- [settlement.md](./api/settlement.md) — `CafeSettlementController` (giải ngân deposit)
- [payment-master-account.md](./api/payment-master-account.md) — `PaymentMasterAccountController` Admin
- [sepay-account.md](./api/sepay-account.md) — `SePayAccountController` (Admin + Manager endpoints)
- [sepay-webhook.md](./api/sepay-webhook.md) — `SePayWebhookController` (webhook + return + mock)
- [debug-sepay.md](./api/debug-sepay.md) — `DebugSePayController` (dev-only)

#### Admin
- [admin-cafe.md](./api/admin-cafe.md) — `AdminCafeController` (operational status)
- [admin-configuration.md](./api/admin-configuration.md) — `AdminConfigurationController` (system config key-value)
- [admin-moderation.md](./api/admin-moderation.md) — `AdminModerationController` (Karma logs, punish, adjust)

#### Tournament
- [tournament.md](./api/tournament.md) — `TournamentController` (Player-facing)
- [tournament-pos.md](./api/tournament-pos.md) — `TournamentPosController` (Manager/POS-facing)

#### Health & Common
- [health.md](./api/health.md) — `HealthController` — API + DB status
- [_common.md](./api/_common.md) — Common conventions (response envelope, status codes, v.v.)

### 🌐 Tích hợp bên thứ 3

- [third-party-services.md](./third-party-services.md) — Google, JWT, Neon, Redis, Brevo, BGG, **SePay**

---

## Controllers tham chiếu nhanh

| Controller | Doc |
|---|---|
| `ActiveSessionController` | [active-session.md](./api/active-session.md) |
| `AdminCafeController` | [admin-cafe.md](./api/admin-cafe.md) |
| `AdminCafePartnerApplicationController` | [cafe-partner.md](./api/cafe-partner.md) |
| `AdminConfigurationController` | [admin-configuration.md](./api/admin-configuration.md) |
| `AdminMasterCatalogController` | [admin-master-catalog.md](./api/admin-master-catalog.md) |
| `AdminModerationController` | [admin-moderation.md](./api/admin-moderation.md) |
| `AuthController` | [auth.md](./api/auth.md) |
| `BggController` | [bgg.md](./api/bgg.md) |
| `BoardGameController` | [board-games.md](./api/board-games.md) |
| `CafeController` | [cafe.md](./api/cafe.md) |
| `CafeInventoryController` | [cafe-inventory.md](./api/cafe-inventory.md) |
| `CafePartnerApplicationController` | [cafe-partner.md](./api/cafe-partner.md) |
| `CafePosController` | [cafe-pos.md](./api/cafe-pos.md) |
| `CafeSettlementController` | [settlement.md](./api/settlement.md) |
| `DebugSePayController` | [debug-sepay.md](./api/debug-sepay.md) |
| `HealthController` | [health.md](./api/health.md) |
| `LobbyController` | [lobby.md](./api/lobby.md) |
| `ManagerController` | [manager.md](./api/manager.md) |
| `ManagerCafeProfileController` | [manager.md](./api/manager.md) |
| `MasterGameController` | [master-games.md](./api/master-games.md) |
| `MatchController` | [matches.md](./api/matches.md) |
| `PaymentController` | [booking.md](./api/booking.md) |
| `PaymentMasterAccountController` | [payment-master-account.md](./api/payment-master-account.md) |
| `ProtectedController` | [protected.md](./api/protected.md) |
| `SePayAccountController` | [sepay-account.md](./api/sepay-account.md) |
| `SePayWebhookController` | [sepay-webhook.md](./api/sepay-webhook.md) |
| `StaffController` | [staff.md](./api/staff.md) |
| `TournamentController` | [tournament.md](./api/tournament.md) |
| `TournamentPosController` | [tournament-pos.md](./api/tournament-pos.md) |
| `UserManagementController` | [user-management.md](./api/user-management.md) |
| `UserProfileController` | [user-profile.md](./api/user-profile.md) |
| `UserRatingController` | [user-ratings.md](./api/user-ratings.md) |

---

## Quy ước

### Response envelope

Mọi response JSON đều theo shape:
```json
{
  "statusCode": 200,
  "message": "...",     // Thông điệp i18n
  "data": { ... }       // Payload (object hoặc array; null nếu lỗi)
}
```

### Error messages

- Mọi lỗi phải có message **unique & operation-specific** (xem `api-error-messages.mdc`)
- Service throw typed exception với message cụ thể → Controller trả 400/404/409/500 tương ứng
- Middleware fallback dùng `ApiErrorMessages.Http.Fallback(statusCode, path)` — không hardcode `"Not Found"`/`"Unauthorized"`

### XML doc mỗi action

Mỗi action trong `BoardVerse.API/Controllers/` phải có:
- `/// <summary>` — Mô tả ngắn + `[Role: ...]`
- `/// <param name="...">` — Mỗi tham số
- `/// <response code="...">` — Mọi status code có thể trả

Xem mẫu tại `CafeController.cs`, `BoardGameController.cs`. Quy ước chi tiết: `api-controller-xml-docs.mdc`.