# KẾ HOẠCH CẢI THIỆN & HOÀN THIỆN UNIT TEST - BOARDVERSE

> **Mục tiêu:** Nâng cấp test suite hiện có lên chuẩn production-ready trước khi viết báo cáo chi tiết.
> 
> **Ngày tạo:** 05/09/2026  
> **Trạng thái:** Draft — Đang phân tích

---

## I. HIỆN TRẠNG TEST SUITE

### 1.1. Số liệu tổng quan

| Chỉ số | Giá trị hiện tại |
|---|---|
| **Tổng số test files** | 155 files |
| **Test types** | Unit Tests (Services/, Helpers/, Filters/) + Integration Tests (Integration/) |
| **Framework** | xUnit.net 2.9.2 |
| **Mocking library** | Moq 4.20.72 |
| **Assertion library** | FluentAssertions 6.12.2 |
| **Database testing** | In-memory EF Core + Real Neon testing branch |
| **Test execution** | Đang chạy (background process) |

### 1.2. Phân bố test theo loại

```
BoardVerse.Tests/
├── Services/           (~89 files) - Unit tests cho business logic
├── Integration/        (~54 files) - API + DB integration tests  
├── Helpers/            (~12 files) - Helper/utility tests
└── Filters/            (~1 file)   - Middleware/filter tests
```

### 1.3. Test naming convention hiện tại

✅ **Chuẩn (đa số):** `MethodName_Should_ExpectedBehavior_WhenCondition`
- `CreateBookingAsync_Should_ReturnBooking_WhenValid`
- `GetAvailabilityAsync_Should_ThrowNotFound_WhenCafeMissing`
- `ValidateHostCanCreateAsync_Should_BypassAllLimits_When_DemoModeOn`

---

## II. CÁC VẤN ĐỀ CẦN KHẮC PHỤC

### 2.1. Test Coverage Gaps (từ TEST_REPORT.md draft)

| Gap ID | Mô tả | File cần fix | Priority |
|---|---|---|---|
| GAP-01 | **9 tests fail** trong `ReservationServiceCafeScheduleValidationTests.cs` — Time zone issue | `ReservationServiceCafeScheduleValidationTests.cs` | **P0 - Critical** |
| GAP-02 | **3 tests fail** trong `DiscoverableLobbyIntegrationTests.cs` — Race condition in test data | `DiscoverableLobbyIntegrationTests.cs` | **P0 - Critical** |
| GAP-03 | Repository layer coverage thấp (~70%) — chủ yếu CRUD không test edge case | `BoardVerse.Data/Repositories/*.cs` | P1 - High |
| GAP-04 | Thiếu test cho BR-REFUND-02 (host dissolve lobby sau grace) | Service tests | P1 - High |
| GAP-05 | Thiếu test cho multi-account detection (BR-RISK-08) | `PlayerRiskScoreServiceTests.cs` | P2 - Medium |
| GAP-06 | Thiếu test cho cooling-off escalation edge cases | `CoolingOffServiceTests.cs` | P2 - Medium |

### 2.2. Test Quality Issues

| Issue ID | Mô tả | Ví dụ | Action |
|---|---|---|---|
| Q-01 | **Mock overload** — Quá nhiều mock dependencies trong 1 test class | `ReservationServiceCafeScheduleValidationTests.cs` (18 mocks) | Refactor sang test helpers hoặc builder pattern |
| Q-02 | **Flaky tests** — Race condition trong integration tests | `DiscoverableLobbyIntegrationTests.cs` | Add proper test isolation + retry logic |
| Q-03 | **Hard-coded values** — Magic numbers trong test assertions | Multiple files | Extract to constants |
| Q-04 | **Duplicate setup** — Constructor setup lặp lại giữa các test classes | Service tests | Create `TestFixture` base class |
| Q-05 | **Incomplete assertions** — Chỉ assert 1-2 fields thay vì toàn bộ object state | Multiple files | Use FluentAssertions `.Should().BeEquivalentTo()` |

### 2.3. Missing Test Scenarios

| Scenario | BR liên quan | Cần thêm test ở đâu |
|---|---|---|
| **Reservation timeout → forfeit deposit → update Karma** | BR-REFUND-01, BR-KARMA-* | `ReservationServiceTests.cs` |
| **Host dissolve lobby sau grace → penalty Karma −10** | BR-REFUND-02, GAP-4 fix | `LobbyServiceTests.cs` |
| **Member rời lobby liên tục (spam join/leave)** | BR-LOBBY-03, BR-RISK-01 (SIG-05) | `LobbyMemberCleanupTests.cs` |
| **Cafe hủy lobby → hoàn 100% deposit → không trừ Karma** | BR-REFUND-04 | `LobbyServiceTests.cs` |
| **Multi-account detection (2 tín hiệu trùng)** | BR-RISK-08 | `PlayerRiskScoreServiceTests.cs` |
| **Admin reset risk score → audit log** | BR-RISK-05, BR-RISK-06 | `AdminModerationServiceTests.cs` |
| **Early checkout với playedRatio = 45%** | BR-REFUND-04 | `ActiveSessionServiceTests.cs` |
| **Private lobby + invite + friendship validation** | BR-LOBBY-PRIVACY-*, BR-LOBBY-INVITE-* | `LobbyInviteServiceTests.cs` |

---

## III. KẾ HOẠCH CẢI THIỆN (6 PHASES)

### Phase 1: Fix Critical Failures (P0) — **1-2 ngày**

**Mục tiêu:** Pass rate 100% trước khi tiếp tục.

#### Task 1.1: Fix time zone issues trong `ReservationServiceCafeScheduleValidationTests.cs`

**Root cause:** Test sử dụng `TimeProvider.System` mà không mock, dẫn đến mismatch giữa test data và runtime datetime.

**Action:**
```csharp
// ❌ TRƯỚC
private readonly TimeProvider _timeProvider = TimeProvider.System;

// ✅ SAU
private readonly Mock<TimeProvider> _mockTimeProvider;
// Setup trong constructor
_mockTimeProvider.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
```

**Files to fix:**
- `ReservationServiceCafeScheduleValidationTests.cs` (9 tests)
- `ReservationServiceTimeValidationTests.cs` (nếu có tương tự)

**Acceptance criteria:**
- [ ] 9 tests pass
- [ ] Mock `TimeProvider` cho tất cả datetime-sensitive tests
- [ ] Verify với `dotnet test --filter "ReservationServiceCafeScheduleValidationTests"`

---

#### Task 1.2: Fix race condition trong `DiscoverableLobbyIntegrationTests.cs`

**Root cause:** Multiple tests chạy song song, tạo overlapping test data trên DB testing branch.

**Action:**
1. Add `[Collection("Serial")]` attribute để force sequential execution
2. Seed unique test data cho mỗi test (dùng `Guid` prefix)
3. Cleanup test data sau mỗi test (transaction rollback hoặc delete explicit)

**Files to fix:**
- `DiscoverableLobbyIntegrationTests.cs` (3 tests)

**Acceptance criteria:**
- [ ] 3 tests pass stably (run 10 times, pass 10 times)
- [ ] Test data isolated per test
- [ ] Verify với `dotnet test --filter "DiscoverableLobbyIntegrationTests" -- --repeat 10`

---

### Phase 2: Improve Test Quality (Q-*) — **2-3 ngày**

#### Task 2.1: Refactor mock overload → Test builders

**Target files:**
- `ReservationServiceCafeScheduleValidationTests.cs` (18 mocks)
- `ActiveSessionServiceTests.cs` (15+ mocks)
- `LobbyServiceTests.cs` (12+ mocks)

**Action:** Tạo builder pattern cho test data.

```csharp
// BoardVerse.Tests/Builders/ReservationTestBuilder.cs
public class ReservationTestBuilder
{
    private Guid _id = Guid.NewGuid();
    private Guid _hostId = Guid.NewGuid();
    private ReservationStatus _status = ReservationStatus.Holding;
    // ... other fields

    public ReservationTestBuilder WithStatus(ReservationStatus status)
    {
        _status = status;
        return this;
    }

    public ReservationEntity Build()
    {
        return new ReservationEntity { Id = _id, HostUserId = _hostId, Status = _status, ... };
    }
}
```

**Acceptance criteria:**
- [ ] Tạo builders cho: `ReservationEntity`, `LobbyEntity`, `WalletEntity`, `CafeEntity`
- [ ] Refactor 5 test classes sử dụng builders
- [ ] Setup time giảm từ ~50 lines → ~10 lines/test

---

#### Task 2.2: Add test fixture base class

**Action:** Tạo `ServiceTestBase<TService>` để share common setup.

```csharp
// BoardVerse.Tests/Infrastructure/ServiceTestBase.cs
public abstract class ServiceTestBase<TService> where TService : class
{
    protected Mock<BoardVerseDbContext> MockDb { get; }
    protected Mock<ILogger<TService>> MockLogger { get; }
    protected Mock<TimeProvider> MockTimeProvider { get; }
    
    protected ServiceTestBase()
    {
        MockDb = new Mock<BoardVerseDbContext>(...);
        MockLogger = new Mock<ILogger<TService>>();
        MockTimeProvider = new Mock<TimeProvider>();
        
        // Setup common behavior
        MockTimeProvider.Setup(x => x.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
    }
}
```

**Target:** Refactor 10 service test classes sử dụng base class.

**Acceptance criteria:**
- [ ] Constructor setup giảm từ 20+ lines → 3 lines
- [ ] Time provider mock consistent across all tests

---

#### Task 2.3: Replace magic numbers with constants

**Action:** Extract test constants.

```csharp
// BoardVerse.Tests/TestConstants.cs
public static class TestConstants
{
    public static class Deposit
    {
        public const long DefaultAmount = 50_000; // BVC
        public const decimal RiskMultiplier = 1.5m;
        public const int MaxPlayers = 4;
    }
    
    public static class TimeWindow
    {
        public const int MinBufferMinutes = 60;
        public const int RecommendedBufferMinutes = 120;
    }
}
```

**Target:** Scan 50 test files, replace magic numbers.

**Acceptance criteria:**
- [ ] 0 magic numbers trong assertions
- [ ] Constants có XML doc giải thích

---

### Phase 3: Add Missing Scenarios (Scenario-*) — **3-4 ngày**

#### Task 3.1: Add BR-REFUND-02 test (GAP-4 fix)

**File:** `LobbyServiceTests.cs`

**Test cases cần thêm:**
```csharp
[Fact]
public async Task DissolveLobbyAsync_Should_PenaltyKarma_WhenHostDissolveAfterGrace()
{
    // Arrange: lobby có members, đã qua grace period 15 phút
    // Act: Host dissolve
    // Assert: 
    //   - Lobby.Status = HostCancelled
    //   - KarmaShortPlayRecord created với ViolationType = HostDissolve
    //   - Host Karma giảm −10
    //   - Deposit forfeit 100%
}

[Fact]
public async Task DissolveLobbyAsync_Should_RefundFull_WhenWithinGraceAndNoMembers()
{
    // BR-REFUND-03
}

[Fact]
public async Task DissolveLobbyAsync_Should_Throw_WhenHasActiveMembers()
{
    // Cannot dissolve if members joined after grace
}
```

**Acceptance criteria:**
- [ ] 3 tests added, all pass
- [ ] Cover BR-REFUND-02 edge cases (24h, 6h thresholds)
- [ ] Verify Karma integration

---

#### Task 3.2: Add multi-account detection test (BR-RISK-08)

**File:** `PlayerRiskScoreServiceTests.cs` hoặc tạo `MultiAccountDetectionServiceTests.cs`

**Test cases:**
```csharp
[Fact]
public async Task DetectMultiAccount_Should_CreateLink_When2SignalsMatch()
{
    // SIG-07: same IP + same device ID
}

[Fact]
public async Task DetectMultiAccount_Should_CreateAlert_WhenLinkConfirmed()
{
    // AlertType = multi_account_detected
}

[Fact]
public async Task DetectMultiAccount_Should_IncreaseRiskScore_By30()
{
    // Both accounts get +30 riskScore
}
```

**Acceptance criteria:**
- [ ] 3 tests pass
- [ ] Cover BR-RISK-08 full flow

---

#### Task 3.3: Add private lobby + invite tests

**File:** `LobbyInviteServiceTests.cs`

**Test cases:**
```csharp
[Fact]
public async Task SendInviteAsync_Should_Throw_WhenPrivateLobbyAndNotFriend()
{
    // BR-LOBBY-INVITE-05
}

[Fact]
public async Task AcceptInviteAsync_Should_RecheckFriendship_WhenPrivateLobby()
{
    // BR-LOBBY-INVITE-07: unfriend before accept
}

[Fact]
public async Task JoinByShareCodeAsync_Should_Throw_WhenPrivateAndNotFriendOfAnyMember()
{
    // BR-LOBBY-PRIVACY-03
}
```

**Acceptance criteria:**
- [ ] 5+ tests added
- [ ] Cover BR-LOBBY-PRIVACY-* + BR-LOBBY-INVITE-* matrix

---

### Phase 4: Increase Repository Coverage (GAP-3) — **2 ngày**

**Target:** Tăng từ 70% → 85% cho `BoardVerse.Data/Repositories/`.

**Action:** Add tests cho edge cases.

```csharp
// ReservationRepositoryTests.cs
[Fact]
public async Task GetByIdAsync_Should_ReturnNull_WhenNotFound()

[Fact]
public async Task GetOverlappingReservationsAsync_Should_HandleOvernightSession()

[Fact]
public async Task GetActiveReservationCountAsync_Should_ExcludeCancelled()
```

**Target repos:**
- `ReservationRepository`
- `LobbyRepository`
- `WalletRepository`
- `SeatInventoryRepository`

**Acceptance criteria:**
- [ ] 30+ tests added across 4 repos
- [ ] Coverage tool report shows 85%+

---

### Phase 5: Add Performance & Load Tests — **2 ngày**

**New directory:** `BoardVerse.Tests/Performance/`

```csharp
// ConcurrentReservationTests.cs
[Fact]
public async Task CreateReservation_Should_HandleConcurrentRequests_Without_DoubleBooking()
{
    // Simulate 10 concurrent requests for last 2 seats
    // Assert: only 2 reservations succeed, 8 fail with proper error
}

// LobbyJoinStressTests.cs
[Fact]
public async Task JoinLobby_Should_Handle100ConcurrentJoins_WithoutRaceCondition()
```

**Acceptance criteria:**
- [ ] 5 concurrent/stress tests pass
- [ ] No race conditions detected

---

### Phase 6: Test Documentation & Maintenance — **1 ngày**

#### Task 6.1: Add XML doc cho mỗi test class

```csharp
/// <summary>
/// Unit tests cho ReservationService.ConfirmAsync.
/// Coverage: BR-05, BR-06, BR-09, BR-USER-LIMIT-01/02/03.
/// </summary>
public class ReservationServiceTests { }
```

#### Task 6.2: Create test catalog

**File:** `docs/TEST_CATALOG.md`

```markdown
# Test Catalog

## Service Layer Tests

### ReservationService
- **File:** `ReservationServiceTests.cs`
- **Coverage:** BR-05, BR-06, BR-09, BR-USER-LIMIT-*
- **Test count:** 45 tests
- **Last updated:** 2026-09-05
```

**Acceptance criteria:**
- [ ] Catalog covers all 155 test files
- [ ] Easy to find which test covers which BR

---

## IV. METRICS & KPI

### 4.1. Trước cải thiện

| Metric | Current |
|---|---|
| Total tests | ~2,500+ |
| Pass rate | ~99.5% (12 failed) |
| Coverage (Services) | ~85% |
| Coverage (Repositories) | ~70% |
| Coverage (Overall) | ~82% |
| Flaky tests | 3 (DiscoverableLobby) |
| Execution time | ~5-10 min |

### 4.2. Mục tiêu sau cải thiện

| Metric | Target |
|---|---|
| Total tests | ~2,700+ (+200 tests) |
| Pass rate | **100%** |
| Coverage (Services) | **90%** |
| Coverage (Repositories) | **85%** |
| Coverage (Overall) | **87%** |
| Flaky tests | **0** |
| Execution time | <12 min (acceptable trade-off) |

---

## V. LỊCH TRÌNH THỰC HIỆN

| Phase | Duration | Deliverable |
|---|---|---|
| **Phase 1** | 1-2 days | 12 failed tests → 0 failed |
| **Phase 2** | 2-3 days | Test quality improvements (builders, base class, constants) |
| **Phase 3** | 3-4 days | 50+ missing scenario tests added |
| **Phase 4** | 2 days | Repository coverage 70% → 85% |
| **Phase 5** | 2 days | 5 performance/stress tests added |
| **Phase 6** | 1 day | Test documentation complete |
| **Total** | **11-14 days** | Production-ready test suite |

---

## VI. CHECKLIST TỔNG QUAN

### Phase 1 (Critical Fixes)
- [ ] Fix 9 timezone tests in `ReservationServiceCafeScheduleValidationTests.cs`
- [ ] Fix 3 race condition tests in `DiscoverableLobbyIntegrationTests.cs`
- [ ] Verify pass rate = 100%

### Phase 2 (Quality)
- [ ] Create test builders for 4 core entities
- [ ] Create `ServiceTestBase<T>` base class
- [ ] Extract magic numbers to `TestConstants.cs`
- [ ] Refactor 10 test classes

### Phase 3 (Coverage)
- [ ] Add BR-REFUND-02 tests (GAP-4)
- [ ] Add BR-RISK-08 tests (multi-account)
- [ ] Add BR-LOBBY-PRIVACY-* + BR-LOBBY-INVITE-* tests
- [ ] Add early checkout tests (BR-REFUND-04)
- [ ] Add Karma aggregation tests

### Phase 4 (Repos)
- [ ] 30+ repository edge case tests
- [ ] Coverage report 85%+

### Phase 5 (Performance)
- [ ] 5 concurrent/stress tests
- [ ] No race conditions

### Phase 6 (Docs)
- [ ] XML doc for all test classes
- [ ] `TEST_CATALOG.md` complete
- [ ] Update `TEST_REPORT.md` with final results

---

## VII. SAU KHI HOÀN THÀNH

Khi tất cả 6 phases xong:
1. Run full test suite: `dotnet test --logger "trx;LogFileName=FinalTestResults.trx"`
2. Generate coverage report: `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover`
3. Viết `TEST_REPORT.md` chi tiết với số liệu thực tế
4. Commit toàn bộ test improvements
5. Tag release: `v1.0.0-test-complete`

---

## VIII. NGƯỜI THỰC HIỆN

| Phase | Assignee | Status |
|---|---|---|
| Phase 1 | AI Agent (Kiro) | ⏳ Đang chờ xác nhận |
| Phase 2 | AI Agent (Kiro) | ⏸️ Chưa bắt đầu |
| Phase 3 | AI Agent (Kiro) | ⏸️ Chưa bắt đầu |
| Phase 4 | AI Agent (Kiro) | ⏸️ Chưa bắt đầu |
| Phase 5 | AI Agent (Kiro) | ⏸️ Chưa bắt đầu |
| Phase 6 | AI Agent (Kiro) | ⏸️ Chưa bắt đầu |

---

**Next Action:** Xác nhận kế hoạch này trước khi bắt đầu Phase 1.

---

## IX. CHANGELOG LIÊN KẾT (2026-09-08)

Build fixes (round 3) — `ApiErrorMessages.cs` brace structure (nested `Discovery`/`Session`/`System`), `BoardGameDiscoveryService.cs` (`PaginationParams` ctor + `cafeResult.Cafes.Data`). Build đã về **0 Error**. Chi tiết xem `docs/CHANGELOG.md`.
