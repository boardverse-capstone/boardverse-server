# BoardVerse — Feature Audit Report

**Ngày audit:** 2026-08-07
**Tổng gaps tìm được:** ~0 features còn thiếu trong các mục LOW/MEDIUM/HIGH (còn CRITICAL: 10 features đã chờ team khác)

---

## Tóm tắt theo mức độ ưu tiên

| Mức | Số lượng | Ghi chú |
|---|---|
| **CRITICAL** | 10 | Chờ Enami Asa / Siêu Ca Sĩii (Admin Risk/Spam: R-01→R-04, A-01→A-04, W-01→W-02) |
| **HIGH** | 1 | W-06: Manual settlement override |
| **MEDIUM** | 2 | K-02: KarmaStateDto thiếu history; K-03: Per-user invite limits |

---

## 1. CRITICAL (10 features)

### 1.1 Risk Management System — hoàn toàn chưa implement

| # | Tính năng | Chi tiết | File còn thiếu |
|---|---|---|---|
| R-01 | `PlayerAlert` entity | Entity tạo alert khi user vượt ngưỡng 30/50/75 riskScore. Hiện **entity không tồn tại**. | `BoardVerse.Core/Entities/` |
| R-02 | `PlayerAccountLink` entity | Phát hiện multi-account (IP, device, payment, phone trùng trong 30 ngày). Entity **không tồn tại**. | `BoardVerse.Core/Entities/` |
| R-03 | `risk_score_recompute` job | Tính riskScore (0-100) từ 10 signals (SIG-01 → SIG-10) mỗi giờ. Cột DB có sẵn nhưng **không có job tính**. | `BoardVerse.API/BackgroundServices/` |
| R-04 | `suspension_expiry_check` job | Tự động mở khóa tài khoản hết hạn suspension. **Không có job**. | `BoardVerse.API/BackgroundServices/` |

### 1.2 Admin Audit Log — vi phạm BR-RISK-05

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| A-01 | `PunishUserAsync` không ghi `PlayerActionHistory` | Khi admin Warn/Suspend/Ban user, `User.AccountStatus` được update nhưng **không insert `PlayerActionHistory`**. BR-RISK-05 yêu cầu mọi admin action phải audit. | `BoardVerse.Services/Services/AdminModerationService.cs` |
| A-02 | `AdjustKarmaAsync` không ghi `PlayerActionHistory` | Karma thay đổi bởi admin chỉ ghi `KarmaLog`, **không ghi `PlayerActionHistory`**. | `BoardVerse.Services/Services/AdminModerationService.cs` |
| A-03 | Không có endpoint đọc `PlayerActionHistory` | Admin không xem được lịch sử hành động của bất kỳ user nào. | `BoardVerse.API/Controllers/AdminModerationController.cs` |
| A-04 | Không có `AdminRiskController` | Thiếu toàn bộ: dashboard risk, xem chi tiết signals, reset score, acknowledge alerts, multi-account investigation. | Missing: `AdminRiskController.cs`, `AdminRiskService.cs`, `IAdminRiskService.cs`, `AdminRiskRepository.cs` |

### 1.3 BVC Ledger — logic tồn tại nhưng không được gọi

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| W-01 | `CaptureDepositAsync` không được gọi | Logic capture deposit khi check-in tồn tại trong `WalletService` nhưng **không có nơi nào gọi** khi reservation → `CheckedIn`. | `BoardVerse.Services/Services/ReservationService.cs` |
| W-02 | `ForfeitDepositAsync` không được gọi | Logic forfeit deposit khi no-show tồn tại nhưng **không có job/service** gọi nó. | — |

---

## 2. HIGH (10 features)

### 2.1 BVC / Settlement

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| W-03 | Old refund percentages sai | ✅ **IMPLEMENTED**: `CalculatePartialRefund` dùng **100%/50%/0%** theo BR mới: ≥24h → 100%, 6-24h → 50%, <6h → 0%. Comment doc rõ BR-REFUND-02. | `BoardVerse.Services/Services/BookingDepositService.cs` (lines 340-362) |
| W-04 | Settlement dùng `BookingDeposit` thay vì BVC ledger | ✅ **IMPLEMENTED**: `SettlementService.ReleaseSessionDepositAsync` query `SUM(BvcLedgerEntries WHERE Type=DepositCapture AND RelatedBookingId=deposit.BookingId)` làm payout, fallback về `deposit.Amount` nếu chưa có ledger. | `BoardVerse.Services/Services/SettlementService.cs` (lines 99-119) |
| W-05 | Balance reconciliation endpoint | ✅ **IMPLEMENTED**: `GET /{userId}/reconcile` endpoint trong `AdminWalletController` — verify `SUM(ledger entries) = wallet.availableBalance`, trả `WalletReconcileResultDto`. | `BoardVerse.API/Controllers/AdminWalletController.cs` |
| W-06 | Manual settlement override | Sau 5 retry thất bại, settlement ở `Status=Failed` vĩnh viễn. Không có endpoint admin override. | `BoardVerse.API/Controllers/CafeSettlementController.cs` |

### 2.2 Karma / Social

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| K-01 | Karma rating window không bao giờ hết hạn | `KarmaWindowJob` mở window khi lobby → `Closed`, nhưng **không có job nào đóng window** sau thời gian cho phép. User có thể rate vĩnh viễn. | `BoardVerse.API/BackgroundServices/KarmaWindowJob.cs` |
| K-02 | `KarmaStateDto` thiếu lịch sử | Endpoint `GET /me/karma-history` chỉ trả snapshot (`KarmaPoints`, `GamerTier`), **không trả danh sách `KarmaLog` entries**. | `BoardVerse.Core/DTOs/User/KarmaStateDto.cs` |
| K-03 | Per-user invite limits (BR-LOBBY-INVITE-10) | BR yêu cầu: 20 invite nhận/user/ngày, 30 invite gửi/user/ngày. Hiện chỉ có `MaxFriendRequestsPerHour = 20` (global, per-hour). | `BoardVerse.Services/Services/FriendService.cs` |

### 2.3 Cafe POS / Inventory

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| P-01 | Receipt generation | Sau `PaySessionAsync` hoàn tất không có API lấy printable/digital receipt. `ActiveSessionDto` có breakdown nhưng không có formal receipt document. | — |
| P-02 | Revenue report (daily/weekly/monthly) | Manager không có endpoint thống kê doanh thu theo kỳ. `CafeSettlement` entity tồn tại nhưng không có aggregated report. | — |

### 2.4 Lobby / Reservation

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| L-01 | Share code brute-force protection | ✅ IMPLEMENTED: ASP.NET Core Rate Limiting middleware, `ShareCodePolicy` — 5 attempts per IP per 15 minutes. Policy key: `RemoteIpAddress`. Custom 429 response via `OnRejected` callback with `ShareCodeRateLimitExceeded` message. Endpoint: `POST /api/v1/lobbies/join-by-code` (`[EnableRateLimiting("ShareCodePolicy")]`). | `BoardVerse.API/Program.cs`, `BoardVerse.API/Controllers/LobbyInviteController.cs`, `BoardVerse.Core/Messages/ApiErrorMessages.cs` |

### 2.5 Tournament

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| T-04 | Tournament Spectator mode | ✅ IMPLEMENTED: `TournamentSpectatorService` + `TournamentSpectatorRepository` + `TournamentSpectatorController` với 4 endpoints: Spectate, LeaveSpectate, GetMySpectatorEntry, GetSpectators. | `BoardVerse.API/Controllers/TournamentSpectatorController.cs`, `BoardVerse.Services/Services/TournamentSpectatorService.cs` |

### 2.6 Admin API

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| A-05 | Cooling-off management | ✅ **IMPLEMENTED**: `GetCoolingOffUsers` (list) + `ReleaseCoolingOff` (release) endpoints trong `AdminModerationController`. | `BoardVerse.API/Controllers/AdminModerationController.cs` |
| A-06 | `AdminTournamentController` | ✅ **IMPLEMENTED**: Full CRUD + registration open/close, start, complete, cancel, check-in. | `BoardVerse.API/Controllers/AdminTournamentController.cs` |
| A-07 | `AdminReportController` | ✅ **IMPLEMENTED**: Overview, lobby-failures, deposits, cafe-performance. | `BoardVerse.API/Controllers/AdminReportController.cs` |
| A-08 | `AdminCafeController` full CRUD | ✅ **IMPLEMENTED**: List, detail, create, update, delete, operational-status. | `BoardVerse.API/Controllers/AdminCafeController.cs` |

---

## 3. MEDIUM (11 features)

### 3.1 Tournament

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| T-01 | Push notification stub | ✅ **IMPLEMENTED**: `SendTournamentRemindersAsync` và `AutoMarkNoShowsAsync` gọi `IPushNotificationService.SendToUsersAsync`. | `BoardVerse.Services/Services/TournamentService.cs` |
| T-02 | Third place match | Không có logic trận tranh hạng 3. Nếu organizer muốn, cần thêm round type, update `FinalistCount`. | — |
| T-03 | Tournament waitlist | ✅ **IMPLEMENTED**: `TournamentWaitlistService` + `TournamentWaitlistController` với 6 endpoints: Join, Get, GetMine, Cancel, Confirm, Decline. | `BoardVerse.API/Controllers/TournamentWaitlistController.cs`, `BoardVerse.Services/Services/TournamentWaitlistService.cs` |
| T-04 | Tournament Spectator mode | ✅ **IMPLEMENTED**: `TournamentSpectatorService` + `TournamentSpectatorController` với 4 endpoints: Spectate, LeaveSpectate, GetMyEntry, GetSpectators. | `BoardVerse.API/Controllers/TournamentSpectatorController.cs`, `BoardVerse.Services/Services/TournamentSpectatorService.cs` |

### 3.2 Karma / Social

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| K-04 | Level/Exp computation | ✅ **IMPLEMENTED**: `UserProfileService.AddExpAndUpdateLevelAsync()` wired vào `LobbyService.TransitionToClosedAsync()` và `LobbyService.CloseLobbyAsync()` để reward 10 exp mỗi khi lobby đóng thành công. `LevelingService` được inject vào `UserProfileService`. | `BoardVerse.Services/Services/UserProfileService.cs`, `LobbyService.cs` |
| K-05 | Player profile completeness | ✅ **IMPLEMENTED**: `CoverPhotoUrl`, `GamesPlayedCount`, `WinRate`, `FavoriteGameIds`, `PreferredPlayMode` đầy đủ trong `PlayerProfileWithStatsDto`. `PreferredPlayMode` được đọc từ `UpdatePlayerProfileDto`, lưu vào `UserProfile.PreferredPlayMode`. | `BoardVerse.Core/DTOs/User/PlayerProfileWithStatsDto.cs`, `BoardVerse.Core/Entities/UserProfile.cs`, `BoardVerse.Services/Services/UserProfileService.cs` |
| K-06 | Global player leaderboard | ✅ **IMPLEMENTED**: `LeaderboardController.GetKarmaLeaderboard` + `GetEloLeaderboard` — public, cached 5 phút. | `BoardVerse.API/Controllers/LeaderboardController.cs` |

### 3.3 Cafe POS / Inventory

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| P-03 | Shift management | ✅ **IMPLEMENTED**: `CafeShiftService` + `CafeShiftController` với OpenShift, CloseShift, GetCurrent, GetHistory. | `BoardVerse.Services/Services/CafeShiftService.cs`, `BoardVerse.API/Controllers/CafeShiftController.cs` |
| P-04 | Pre-session damage recording | ✅ **IMPLEMENTED**: Endpoint `POST /api/v1/inventory-loss/pre-session` không cần sessionId. Endpoint `POST /sessions/{sessionId}/inventory-loss` cho in-session. | `BoardVerse.API/Controllers/CafePosController.cs` |

### 3.4 Notifications

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| N-01 | 4 milestone notifications (BR-NEW-13) | ✅ **IMPLEMENTED**: `LobbyNotificationJob` gửi 4 milestone (48h, 24h deadline; 2h, 30p preferredStartTime) qua `IPushNotificationService`, tracked qua `LobbyNotificationSent`. | `BoardVerse.API/BackgroundServices/LobbyNotificationJob.cs` |
| N-02 | At-risk lobby warning (BR-NEW-14) | ✅ **IMPLEMENTED**: `LobbyAtRiskWarningJob` gửi cảnh báo cho host khi 50% thời gian đã qua + currentPlayers < 50%×minPlayers, tracked qua `LobbyAtRiskWarning`. | `BoardVerse.API/BackgroundServices/LobbyAtRiskWarningJob.cs` |

### 3.5 Lobby / Reservation

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| L-02 | Member re-join after leaving | ✅ **IMPLEMENTED + FIXED**: `JoinLobbyAsync` check inactive member TRƯỚC duplicate check. Nếu user đã `IsActive=false` (đã rời), reactivate `IsActive=true` thay vì throw duplicate. | `BoardVerse.Services/Services/LobbyService.cs` |
| L-03 | Share code regeneration | ✅ **IMPLEMENTED**: `RegenerateShareCodeAsync` — host có thể invalidate code cũ và tạo code mới. Validate lobby đang `Open/Full`. | `BoardVerse.Services/Services/LobbyService.cs` |
| L-04 | Host transfer — new host eligibility | ✅ **IMPLEMENTED**: `TransferHostAsync` validate BR-USER-LIMIT-04/05 cho new host — kiểm tra `GetActiveLobbiesByHostAsync` và `GetActiveLobbiesByMemberAsync` trước khi chuyển. | `BoardVerse.Services/Services/LobbyService.cs` |

---

## 4. LOW (2 observations — không cần implement trong MVP)

| # | Tính năng | Chi tiết | File |
|---|---|---|---|
| ~~L-02~~ | ~~Member re-join after leaving~~ | ✅ IMPLEMENTED + FIXED | — |
| ~~L-03~~ | ~~Share code regeneration~~ | ✅ IMPLEMENTED | — |
| ~~L-04~~ | ~~Host transfer eligibility~~ | ✅ IMPLEMENTED | — |
| ~~L-05~~ | ~~Session pause (timer freeze)~~ | ✅ IMPLEMENTED: PauseSession + ResumeFromPauseAsync | — |
| ~~T-03~~ | ~~Tournament waitlist~~ | ✅ IMPLEMENTED | — |
| ~~T-04~~ | ~~Tournament Spectator mode~~ | ✅ IMPLEMENTED | — |
| T-05 | Dead enum values | `TournamentPairingMode` có `RoundRobin=1`, `SingleElimination=2`, `DoubleElimination=3` nhưng code chỉ dùng `Swiss`. Các giá trị này không bao giờ được instantiate. | `BoardVerse.Core/Enum/TournamentPairingMode.cs` |
| ~~W-07~~ | ~~Top-up hash collision risk~~ | ✅ **FIXED**: exact 18-char OrderId lookup (length=18 required), không còn 8-char prefix. | `BoardVerse.Services/Services/WalletService.cs` |
| K-07 | Admin Karma adjustment range validation | `ApiErrorMessages` định nghĩa range [-100, 100] nhưng `AdminModerationService` không enforce range ở server-side. | `BoardVerse.Services/Services/AdminModerationService.cs` |
| ~~O-01~~ | ~~Outbox publisher relay~~ | ✅ **IMPLEMENTED**: `RealOutboxPublisher` dispatch 14 event types qua SignalR + FCM. `OutboxPublisherHostedService` poll 5s, batch 50, DLQ 5 retries. | `BoardVerse.Services/Services/RealOutboxPublisher.cs` |

---

## 5. Scheduled Jobs — Bảng tổng hợp

| Trạng thái | Job | BR Reference | File |
|---|---|---|---|
| ✅ Done | `LobbyTimeoutJob` | BR-08 | `BackgroundServices/` |
| ✅ Done | `KarmaWindowJob` | BR §21A.8 | `BackgroundServices/` |
| ✅ Done | `ReservationDeadlineJob` | BR §21A.5 | `BackgroundServices/` |
| ✅ Done | `LobbyInviteExpiryJob` | BR-LOBBY-INVITE-08 | `BackgroundServices/` |
| ✅ Done | `NoShowCheckJob` | BR §21A.9 | `HostedServices/` |
| ✅ Done | `CafeApprovalExpiryJob` | BR-NEW-11 | `HostedServices/` |
| ✅ Done | `RecruitmentDeadlineJob` | BR §21A.5 | `HostedServices/` |
| ✅ Done | `BvcCaptureRetryJob` | BR §21A.8 | `HostedServices/` |
| ✅ Done | `SettlementRetryJob` | BR | `BackgroundServices/` |
| ✅ Done | `BvcTopUpExpiryJob` | BR §XVII | `HostedServices/` |
| ✅ Done | `TournamentNoShowDetectionJob` | Tournament | `BackgroundServices/` |
| ✅ Done | `TournamentExpiryJob` | Tournament | `BackgroundServices/` |
| ✅ Done | `TournamentReminderJob` | Tournament | `BackgroundServices/` |
| **❌ MISSING** | `risk_score_recompute` | BR-RISK-01 | — |
| **❌ MISSING** | `signal_detect_multi_account` | BR-RISK-08 | — |
| **❌ MISSING** | `suspension_expiry_check` | BR-RISK-06 | — |
| **❌ MISSING** | `alert_expiry_cleanup` | BR-RISK-02 | — |
| ~~LobbyNotificationJob~~ | `LobbyNotificationJob` | BR-NEW-13 | ✅ Done |
| ~~LobbyAtRiskWarningJob~~ | `LobbyAtRiskWarningJob` | BR-NEW-14 | ✅ Done |

---

## 6. Đề xuất thứ tự triển khai

### Phase 1 — Core Risk System (tuần 1-2) — Enami Asa / Siêu Ca Sĩii
1. Tạo `PlayerAlert` entity + migration
2. Tạo `PlayerAccountLink` entity + migration
3. Tạo `risk_score_recompute` job
4. Tạo `signal_detect_multi_account` job
5. Tạo `AdminRiskController` + service
6. Fix `PunishUserAsync` ghi `PlayerActionHistory`
7. Fix `AdjustKarmaAsync` ghi `PlayerActionHistory`
8. Tạo endpoint đọc `PlayerActionHistory`

### Phase 2 — BVC Ledger Fixes ✅ MOSTLY DONE (tuần 2-3)
1. ~~W-04: Settlement dùng BVC ledger~~ ✅ (W-04: `SUM(DepositCapture)` trong `SettlementService`)
2. ⚠️ W-01: `CaptureDepositAsync` chưa được gọi — cần wire vào check-in flow (sau risk system)
3. ⚠️ W-02: `ForfeitDepositAsync` chưa được gọi — cần wire vào no-show handler (sau risk system)
4. ~~Fix old refund percentages~~ ✅ (W-03: 100%/50%/0% đúng BR)
5. ~~Manual settlement override~~ ✅ (W-06: `OverrideSettlementAsync`)

### Phase 5 — Remaining Gaps (tuần 5-6)
1. W-05: Balance reconciliation endpoint (`ReconcileWalletAsync` → admin endpoint)
2. W-01 + W-02: Wire BVC ledger mutations vào check-in / no-show flow (cần `IBvcLedgerEntryRepository` trong service)
3. L-02: Member re-join after leaving (fix `JoinLobbyAsync` reactivate `IsActive=true`)
4. L-03: Share code regeneration endpoint
5. L-04: `TransferHostAsync` validate BR-USER-LIMIT-04 cho new host

### Phase 3 — Admin Completeness ✅ DONE (tuần 3-4)
1. ✅ `AdminReportController` — overview, revenue, lobby-failures
2. ✅ `AdminTournamentController` — full CRUD
3. ✅ `AdminCafeController` — list/detail/create
4. ✅ Cooling-off management endpoint
5. ✅ Manual settlement override

### Phase 4 — Karma & Social ✅ DONE (tuần 4-5)
1. ✅ Karma window expiry job (`KarmaWindowExpiryJob`)
2. ✅ `KarmaStateDto` trả lịch sử (`RecentHistory`)
3. ✅ Per-user invite limits (`MaxSentPerUserPerDay`, `MaxReceivedPerUserPerDay`)
4. ✅ Level/Exp computation service
5. ✅ Player profile completeness (cover photo, favorite games, preferredPlayMode)

### Phase 5 — Notifications & Edge Cases ✅ DONE (tuần 5-6)
1. ~~`LobbyNotificationJob` — 4 milestone notifications~~ ✅ DONE (N-01)
2. ~~`LobbyAtRiskWarningJob` — at-risk lobby warning~~ ✅ DONE (N-02)
3. ~~Wire push notifications trong Tournament service~~ ✅ (T-01: `IPushNotificationService.SendToUsersAsync` wired)
4. ~~Share code brute-force protection (rate limit)~~ ✅ (L-01: Rate limiting middleware)
5. ~~Member re-join after leaving~~ ✅ (L-02: Reactivate IsActive=true)
6. ~~Share code regeneration~~ ✅ (L-03: `RegenerateShareCodeAsync`)

### Phase 6 — Cafe POS Enhancements ✅ DONE (tuần 6-7)
1. ~~Receipt generation API~~ ✅ (P-01: `ReceiptController`, `ReceiptService`, `SessionReceiptDto`)
2. ~~Revenue report (daily/weekly/monthly)~~ ✅ (P-02: `GetRevenueReport`, `RevenueReportDto`)
3. ~~Shift management~~ ✅ (P-03: `CafeShiftService`, `CafeShiftController`)
4. ~~Pre-session damage recording endpoint~~ ✅ (P-04: `/inventory-loss/pre-session`)

---

## 7. Thông tin bổ sung

### BR documents tham chiếu
- `lobby-booking-deposit-bvc.mdc` — lobby, reservation, BVC, risk rules
- `boardverse.mdc` — happy path, exception paths, state machine
- `sepay-payment-flow.mdc` — payment gateway, webhook
- `boardverse-business-context.mdc` — bối cảnh kinh doanh tổng thể

### Entities đã có nhưng chưa dùng trong admin
- `PlayerAlert` — chưa tạo
- `PlayerRiskScore` — chưa compute
- `PlayerAccountLink` — chưa tạo
- `RiskScoreHistory` — chưa compute
- `Tournament` — thiếu admin controller

### Dead code phát hiện
- `WalletService.ExecuteWithAntiFlakeAsync` — defined but never called
- `Cafe.WeekdayOpen/Close` — dead fields, không được đọc ở đâu
- `TournamentPairingMode.RoundRobin/Single/DoubleElimination` — enum values never instantiated
