# BOARDVERSE - TEST REPORT

> **Ngày tạo:** 2026-09-05 23:25:16  
> **Phiên bản:** v1.0  
> **Người thực hiện:** Development Team  
> **Mục đích:** Báo cáo kết quả Unit Test cho Hội đồng đánh giá đồ án
> **Cập nhật build status gần nhất:** 2026-09-08 — Build `BoardVerse.API` / `BoardVerse.Services` đã về **0 Error** (20 Warning). Chi tiết ở `docs/CHANGELOG.md`.

---

## I. TỔNG QUAN TEST COVERAGE

### 1.1. Thống kê tổng quan

| Chỉ số | Giá trị |
|---|---|
| **Tổng số test files** | 115 files |
| **Tổng số test cases** | 1685 test cases |
| **Test loại** | Unit Tests (100%) — Services & Helpers |
| **Framework** | xUnit.net 2.4.2 |
| **Mocking library** | Moq 4.18.4 |
| **.NET version** | .NET 8.0 (LTS) |

### 1.2. Kết quả chạy test

| Metric | Count | Tỷ lệ |
|---|---|---|
| ✅ Passed | 1681 | 99.76% |
| ❌ Failed | 0 | 0% |
| ⏭️ Skipped | 4 | 0.24% |
| **Total** | **1685** | **100%** |

**Trạng thái:** PASS - Test run successful.

---

## II. CHI TIẾT THEO MODULE

### 2.1. Service Layer Tests

Kiểm thử logic nghiệp vụ của các service trong `BoardVerse.Services/Services/`.

| Test Class | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| `Services.ActiveSessionServiceTests` | 41 | 41 | 0 | 0 |
| `Services.AdminMasterCatalogServiceTests` | 23 | 23 | 0 | 0 |
| `Services.AdminModerationServiceTests` | 6 | 6 | 0 | 0 |
| `Services.AdminReportServiceTests` | 8 | 8 | 0 | 0 |
| `Services.ApiErrorMessagesValidationTests` | 16 | 16 | 0 | 0 |
| `Services.AuthServiceTests` | 26 | 26 | 0 | 0 |
| `Services.BackgroundJobRepositoryTests` | 10 | 10 | 0 | 0 |
| `Services.BoardGameServiceTests` | 14 | 14 | 0 | 0 |
| `Services.BookingDepositServiceTests` | 21 | 21 | 0 | 0 |
| `Services.BookingRatingServiceAggregationTests` | 12 | 12 | 0 | 0 |
| `Services.BvcRefundRequestServiceTests` | 22 | 22 | 0 | 0 |
| `Services.CafeBookingServiceTests` | 10 | 10 | 0 | 0 |
| `Services.CafeInventoryServiceTests` | 3 | 3 | 0 | 0 |
| `Services.CafePartnerApplicationServiceTests` | 2 | 2 | 0 | 0 |
| `Services.CafePosComponentCheckBaselineTests` | 3 | 3 | 0 | 0 |
| `Services.CafePosComponentCheckGapTests` | 6 | 6 | 0 | 0 |
| `Services.CafePosCreateCheckInTokenTests` | 9 | 9 | 0 | 0 |
| `Services.CafeRepositoryAvailableSeatsTests` | 7 | 7 | 0 | 0 |
| `Services.CafeScheduleResolverTests` | 5 | 5 | 0 | 0 |
| `Services.CafeScheduleServiceTests` | 20 | 20 | 0 | 0 |
| `Services.CafeScheduleTests` | 23 | 23 | 0 | 0 |
| `Services.CafeServiceTests` | 10 | 10 | 0 | 0 |
| `Services.CafeShiftServiceTests` | 15 | 15 | 0 | 0 |
| `Services.CoolingOffExtendTests` | 5 | 5 | 0 | 0 |
| `Services.CoolingOffServiceTests` | 15 | 15 | 0 | 0 |
| `Services.CurrentUserServiceTests` | 7 | 7 | 0 | 0 |
| `Services.DemoGuardTests` | 5 | 5 | 0 | 0 |
| `Services.DepositCalculatorTests` | 33 | 33 | 0 | 0 |
| `Services.DissolveLobbyAsyncTests` | 15 | 15 | 0 | 0 |
| `Services.EligibilityValidatorAdvancedLimitTests` | 13 | 13 | 0 | 0 |
| `Services.EligibilityValidatorTests` | 25 | 25 | 0 | 0 |
| `Services.FriendNoteServiceTests` | 9 | 9 | 0 | 0 |
| `Services.FriendReportServiceTests` | 11 | 11 | 0 | 0 |
| `Services.FriendServiceTests` | 80 | 79 | 0 | 1 |
| `Services.GameSeedServiceTests` | 8 | 8 | 0 | 0 |
| `Services.GameTemplateServiceTests` | 6 | 6 | 0 | 0 |
| `Services.HealthServiceTests` | 3 | 3 | 0 | 0 |
| `Services.KarmaConfigurationServiceTests` | 2 | 2 | 0 | 0 |
| `Services.KarmaPenaltyConfigurationServiceTests` | 4 | 4 | 0 | 0 |
| `Services.KarmaRatingServiceTests` | 4 | 4 | 0 | 0 |
| `Services.KarmaServiceTests` | 33 | 33 | 0 | 0 |
| `Services.LeaderboardServiceTests` | 13 | 13 | 0 | 0 |
| `Services.LegacyBookingCleanupServiceTests` | 16 | 16 | 0 | 0 |
| `Services.LevelingServiceTests` | 26 | 26 | 0 | 0 |
| `Services.LobbyHubServiceTests` | 7 | 7 | 0 | 0 |
| `Services.LobbyInviteServiceTests` | 34 | 34 | 0 | 0 |
| `Services.LobbyMemberCleanupTests` | 4 | 4 | 0 | 0 |
| `Services.LobbyMessageServiceTests` | 20 | 20 | 0 | 0 |
| `Services.LobbyServiceCafeScheduleValidationTests` | 7 | 7 | 0 | 0 |
| `Services.LobbyServiceTests` | 38 | 38 | 0 | 0 |
| `Services.ManualPaymentServiceTests` | 11 | 11 | 0 | 0 |
| `Services.MatchResultServiceTests` | 4 | 3 | 0 | 1 |
| `Services.PaymentServiceTests` | 28 | 28 | 0 | 0 |
| `Services.PlayerAlertServiceTests` | 20 | 20 | 0 | 0 |
| `Services.PlayerCheckInHostAssignmentTests` | 1 | 1 | 0 | 0 |
| `Services.PlayerCheckInServiceTests` | 9 | 9 | 0 | 0 |
| `Services.PlayerGeocodingServiceTests` | 9 | 9 | 0 | 0 |
| `Services.PlayerKarmaServiceTests` | 24 | 24 | 0 | 0 |
| `Services.PlayerRiskQueryServiceTests` | 5 | 5 | 0 | 0 |
| `Services.PlayerRiskScoreServiceTests` | 21 | 21 | 0 | 0 |
| `Services.PlayerSessionGapsTests` | 8 | 8 | 0 | 0 |
| `Services.RealOutboxPublisherTests` | 9 | 9 | 0 | 0 |
| `Services.ReceiptServiceTests` | 10 | 10 | 0 | 0 |
| `Services.RefundCalculationServiceTests` | 24 | 24 | 0 | 0 |
| `Services.ReservationCompleteCaptureFixTests` | 9 | 9 | 0 | 0 |
| `Services.ReservationExtensionServiceTests` | 13 | 13 | 0 | 0 |
| `Services.ReservationGetMyReservationsServiceTests` | 14 | 14 | 0 | 0 |
| `Services.ReservationListMyRepositoryTests` | 19 | 19 | 0 | 0 |
| `Services.ReservationRepositoryOverlapTests` | 8 | 8 | 0 | 0 |
| `Services.ReservationSerializationFailureDetectionTests` | 4 | 4 | 0 | 0 |
| `Services.ReservationServiceCafeScheduleValidationTests` | 9 | 9 | 0 | 0 |
| `Services.ReservationServiceKarmaTests` | 4 | 4 | 0 | 0 |
| `Services.ReservationServiceTimeValidationTests` | 28 | 28 | 0 | 0 |
| `Services.ReservationTimeOverrunHelperTests` | 7 | 7 | 0 | 0 |
| `Services.SePayAccountServiceTests` | 35 | 35 | 0 | 0 |
| `Services.SePayClientWebhookVerificationTests` | 11 | 11 | 0 | 0 |
| `Services.SettlementServiceTests` | 11 | 10 | 0 | 1 |
| `Services.SplitBillServiceTests` | 16 | 16 | 0 | 0 |
| `Services.SystemConfigurationServiceTests` | 20 | 19 | 0 | 1 |
| `Services.TimeSlotServiceTests` | 19 | 19 | 0 | 0 |
| `Services.TimeWindowGuardTests` | 10 | 10 | 0 | 0 |
| `Services.TournamentServiceTests` | 70 | 70 | 0 | 0 |
| `Services.TournamentSpectatorServiceTests` | 7 | 7 | 0 | 0 |
| `Services.TournamentWaitlistServiceTests` | 21 | 21 | 0 | 0 |
| `Services.UserManagementServiceTests` | 4 | 4 | 0 | 0 |
| `Services.UserProfileLocationServiceTests` | 8 | 8 | 0 | 0 |
| `Services.UserProfileServiceTests` | 25 | 25 | 0 | 0 |
| `Services.WalkInServiceTests` | 17 | 17 | 0 | 0 |
| `Services.WalletServiceTests` | 47 | 47 | 0 | 0 |


### 2.2. Helper Layer Tests

Kiểm thử các static helper trong `BoardVerse.Core/Helpers/` và `BoardVerse.Services/Helpers/`.

| Test Class | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| `Helpers.ActiveSessionBillingCalculatorTests` | 16 | 16 | 0 | 0 |
| `Helpers.BggCategoryMapperTests` | 5 | 5 | 0 | 0 |
| `Helpers.BusinessRulesUnitTests` | 29 | 29 | 0 | 0 |
| `Helpers.CafeGameWaitTimeHelperTests` | 3 | 3 | 0 | 0 |
| `Helpers.CafeInventoryBoxSyncHelperTests` | 3 | 3 | 0 | 0 |
| `Helpers.CafePartnerOperationalStatusHelperTests` | 29 | 29 | 0 | 0 |
| `Helpers.CafePartnerStatusMapperTests` | 18 | 18 | 0 | 0 |
| `Helpers.CafePartnerTableLayoutHelperTests` | 10 | 10 | 0 | 0 |
| `Helpers.CafeTableSyncHelperTests` | 29 | 29 | 0 | 0 |
| `Helpers.CafeTableUpdateHelperTests` | 11 | 11 | 0 | 0 |
| `Helpers.EloRatingHelperTests` | 6 | 6 | 0 | 0 |
| `Helpers.GamePlayRoutingHelperTests` | 6 | 6 | 0 | 0 |
| `Helpers.GeoLocationHelperTests` | 7 | 7 | 0 | 0 |
| `Helpers.KarmaRatingHelperTests` | 18 | 18 | 0 | 0 |
| `Helpers.LateCancelRefundCalculatorTests` | 10 | 10 | 0 | 0 |
| `Helpers.MatchConsensusHelperTests` | 4 | 4 | 0 | 0 |
| `Helpers.NominatimResponseParserTests` | 6 | 6 | 0 | 0 |
| `Helpers.PhotonResponseParserTests` | 6 | 6 | 0 | 0 |
| `Helpers.ProfileCompletionRulesTests` | 9 | 9 | 0 | 0 |
| `Helpers.SwissPairingAutoSizeTests` | 5 | 5 | 0 | 0 |
| `Helpers.SwissPairingHelperTests` | 15 | 15 | 0 | 0 |
| `Helpers.TableSizeOptimizerTests` | 33 | 33 | 0 | 0 |
| `Helpers.TournamentEloCalculatorTests` | 6 | 6 | 0 | 0 |
| `Helpers.TournamentRoundsCalculatorTests` | 16 | 16 | 0 | 0 |
| `Helpers.UserAccessHelperTests` | 4 | 4 | 0 | 0 |
| `Helpers.VietnameseTextNormalizerTests` | 7 | 7 | 0 | 0 |


---

## III. DANH SÁCH TEST CASES ĐƯỢC SKIP

4 test bị skip. Lý do: test cần môi trường đặc biệt (DB state sẵn, mock consensus ELO, v.v.) hoặc đã được thay thế bằng test tương đương ở class khác.

| Test Class | Method |
|---|---|
| `Services.MatchResultServiceTests` | `SubmitMatchResultAsync_ConsensusReached_FinalizesAndUpdatesElo` |
| `Services.FriendServiceTests` | `SearchUsersAsync_FiltersBlockedUsers` |
| `Services.SettlementServiceTests` | `ReleaseSessionDepositAsync_TransferFails_StatusFailedDepositStillPaid` |
| `Services.SystemConfigurationServiceTests` | `BulkUpdateConfigsAsync_UpsertsAndInvalidatesCache` |


---

## IV. CÁCH CHẠY TEST

### 4.1. Chạy toàn bộ unit tests

```powershell
cd C:\Users\ASUS\source\repos\BoardVerse
dotnet build BoardVerse.Tests/BoardVerse.Tests.csproj
dotnet test BoardVerse.Tests/BoardVerse.Tests.csproj `
    --filter "FullyQualifiedName~Services|FullyQualifiedName~Helpers" `
    --logger "trx;LogFileName=TestResults\UnitTestReport\result.trx"
```

### 4.2. Chạy 1 test class cụ thể

```powershell
dotnet test BoardVerse.Tests/BoardVerse.Tests.csproj `
    --filter "FullyQualifiedName~BoardGameServiceTests"
```

### 4.3. Xem report HTML từ TRX

Mở file `.trx` trong Visual Studio (Test Explorer → Run → Analyze All) hoặc convert sang HTML bằng `trx2html`.


---

## V. CẤU TRÚC TEST

```
BoardVerse.Tests/
├── Services/                    # Unit tests cho Service layer
│   ├── ActiveSessionServiceTests.cs
│   ├── BoardGameServiceTests.cs
│   ├── LobbyServiceTests.cs
│   ├── ReservationServiceTests.cs
│   ├── WalletServiceTests.cs
│   └── ... (80+ files)
└── Helpers/                     # Unit tests cho static helpers
    ├── ActiveSessionBillingCalculatorTests.cs
    ├── CafeTableSyncHelperTests.cs
    └── ... (40+ files)
```

---

## VI. KẾT LUẬN

- ✅ **1681/1685 tests PASSED** (tỷ lệ 99.76%)
- ✅ Service layer được test với mock đầy đủ (Moq) cho repository dependencies
- ✅ Helper layer được test với pure function validation (không cần mock)
- ✅ Business rules (BR-01 đến BR-22, BR-DEPOSIT-*, BR-LOBBY-*, BR-RISK-*) đều có test cases
- ⚠️ 4 tests skip (cần môi trường test phức tạp hoặc đã được cover bởi test khác)

---

*Report được tạo tự động bằng `scripts/generate-unit-test-report.ps1` từ file TRX output của `dotnet test`.*
