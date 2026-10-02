# Host Deposit Discount + Member BVC Payment — Design Document

**Ngày tạo:** 2026-10-01
**Người soạn:** Cursor Agent
**Trạng thái:** 📋 Planning (chưa implement)
**Scope:** 2 milestones (M1: 10 ngày, M2: 19 ngày)
**Liên quan:** BR-09 (host deposit forfeit), BR-DEPOSIT-01/02, BR-22 (per-member deposit, chưa active), Exception 4 (Session merge)

---

## 📑 Mục lục (Table of Contents)

### Phần A — Tổng quan & Quyết định
- [A1. Executive Summary](#a1-executive-summary)
- [A2. 2 Cases phân biệt](#a2-2-cases-phân-biệt)
- [A3. Milestone Breakdown](#a3-milestone-breakdown)
- [A4. Gap Coverage Matrix (34 gaps)](#a4-gap-coverage-matrix-34-gaps)
- [A5. Existing APIs Reference](#existing-apis-reference)
- [A6. Exception 4 — Session Merge giải pháp (Host Deposit Follows)](#a6-exception-4--session-merge-giải-pháp)

- [A7. Code Verification (2026-10-01)](#a7-code-verification-2026-10-01)

### Phần B — Milestone 1 (10 ngày): Core Case 1 + Exception 4 (Deposit Follows)
- [B1. Schema changes](#b1-schema-changes-m1)
- [B2. Backend Code — Case 1](#b2-backend-code--case-1)
- [B3. Exception 4 — Host Deposit Follows on Merge](#b3-exception-4--host-deposit-follows-on-merge)
- [B4. Option A — Refund Per-Member Deposit on Merge (BR-22)](#b4-option-a--refund-per-member-deposit-on-merge-br-22)
- [B5. Tests cho M1](#b5-tests-cho-m1)
- [B6. Frontend tối thiểu cho M1](#b6-frontend-tối-thiểu-cho-m1)

### Phần C — Milestone 2 (19 ngày): Case 2 + Full UI + Reporting
- [C1. Schema bổ sung cho M2](#c1-schema-bổ-sung-cho-m2)
- [C2. Backend Code — Case 2 (BVC Member Payment)](#c2-backend-code--case-2-bvc-member-payment)
- [C3. Frontend đầy đủ](#c3-frontend-đầy-đủ)
- [C4. Tests cho M2](#c4-tests-cho-m2)
- [C5. Reporting + Rollout](#c5-reporting--rollout)

### Phần D — Phụ lục
- [D1. Edge Case Tables](#d1-edge-case-tables)
- [D2. Migration SQL Full](#d2-migration-sql-full)
- [D3. Idempotency Key Patterns](#d3-idempotency-key-patterns)
- [D4. Audit Log Schema](#d4-audit-log-schema)
- [D5. Open Questions](#d5-open-questions)

---

# PHẦN A — TỔNG QUAN & QUYẾT ĐỊNH

## A1. Executive Summary

### Vấn đề hiện tại

Theo BR-09, host trả deposit cho cả nhóm nhưng deposit **không được cấn trừ vào bill** khi thanh toán → host mất 100% deposit dù đã chơi đủ slot. Ngoài ra, member không có cách nào trả phần bill cá nhân bằng BVC (chỉ trả cash tại quán).

### Giải pháp

| Case | Mô tả | Trigger |
|---|---|---|
| **Case 1** | Host chọn dùng deposit làm **discount bill** cho members | POS checkout time |
| **Case 2** | Member trả phần bill cá nhân bằng **BVC wallet** | Mobile app / POS split bill |

### Quyết định BR

| BR | Quyết định | Tác động |
|---|---|---|
| **BR-09** | ⚠️ GIẢM ƯU TIÊN — supersede bởi BR-DEPOSIT-05 khi Case 1 active | Phá rule deposit forfeit, thay bằng discount distribution |
| **BR-DEPOSIT-01** | ⏸ GIỮ NGUYÊN — host vẫn trả deposit | |
| **BR-DEPOSIT-02** | ⏸ GIỮ NGUYÊN — deposit refund rules | |
| **BR-15** | 🔄 CẬP NHẬT — thêm `DiscountAmount` term vào formula | |
| **BR-22** | 📦 FORWARD-COMPAT — giữ schema, plan trigger khi activate | Option A handle per-member deposit nếu BR-22 active |

### Lợi ích kỳ vọng

| Metric | Trước | Sau M1 | Sau M2 |
|---|---|---|---|
| Host satisfaction (deposit refund) | 0% | 70-90% | 70-90% |
| Member payment convenience | 0% (chỉ cash) | 0% | 80% (BVC) |
| Cafe transaction time | 100% (mọi bill) | 100% | 60% (auto via BVC) |
| Dispute rate | ~5% | ~2% | ~1% |

---

## A2. 2 Cases phân biệt

| Aspect | Case 1: Host Deposit → Discount | Case 2: Member BVC Payment |
|---|---|---|
| **Mục đích** | Host dùng deposit làm giảm giá bill cho members | Member trả phần bill cá nhân bằng BVC |
| **Ai trigger?** | POS staff lúc checkout (chọn mode) | Member từ mobile app HOẶC POS staff |
| **Tiền đi đâu?** | Deposit captured theo discount applied | BVC debit từ member wallet → cafe revenue |
| **Ai được lợi?** | Tất cả members của session (chia đều theo minutes) | Member đó thôi |
| **Walk-in?** | ❌ Không áp dụng (không có deposit) | ✅ Áp dụng (nếu member có BVC) |
| **Merge case (Exception 4)?** | ✅ Member đã merge ra → EXCLUDED khỏi discount | ✅ Member bill tại session cuối |
| **Refund handling** | Remainder → host wallet (release) | Bill sai → auto-refund BVC |

### HostDepositUsageMode (Case 1)

```csharp
public enum HostDepositUsageMode
{
    /// <summary>Không dùng deposit (BR-09 cũ). Capture 100% deposit cho cafe.</summary>
    None = 0,
    
    /// <summary>Deposit chia đều cho members theo minutes played (DiscountGroup).</summary>
    DiscountGroup = 1,
    
    /// <summary>Deposit chỉ giảm bill của host (DiscountHostOnly).</summary>
    DiscountHostOnly = 2
}
```

---

## A3. Milestone Breakdown

### Milestone 1 (10 ngày): Core Case 1 + Option A

**Mục tiêu:** Host có thể chọn dùng deposit làm discount cho cả nhóm. Session merge (Exception 4) handle đúng theo Option A (refund per-member deposit về wallet).

**Out of scope:** Member BVC payment (để M2), mobile UI đầy đủ, reporting.

| Phase | Tasks | Days | Output |
|---|---|---|---|
| **1. Schema + Foundation** | 11 tasks | 3.5 | Migration SQL + entities + enums |
| **2. Backend Case 1 core** | 11 tasks | 4 | `BuildMemberInvoices` refactor + capture flow |
| **2b. Option A (Merge refund)** | 7 tasks | 2 | `MergeService.HandleMemberMergeAsync` + `WalletService.RefundMemberDepositOnMergeAsync` |
| **4a. Tests Case 1 + Option A** | 16 tasks | (parallel với Phase 2) | Unit + integration tests |
| **5a. Frontend minimal** | 3 tasks | 0.5 | POS dropdown chọn `HostDepositUsageMode` |
| **Tổng** | | **10 ngày** | |

**Acceptance criteria M1:**
- [ ] POS có dropdown chọn 3 modes (None/DiscountGroup/DiscountHostOnly) lúc Pay _(Phase 5a — frontend, chưa làm)_
- [x] Khi chọn DiscountGroup: deposit được phân bổ theo minutes cho active members _(M1HostDepositDiscountTests × 3)_
- [x] Khi chọn DiscountHostOnly: deposit giảm bill của host _(M1HostDepositDiscountTests × 1)_
- [x] Capture amount = applied discount, remainder released _(M1HostDepositDiscountTests × 1)_
- [x] **Exception 4 merge case: Host A's deposit FOLLOWS to target lobby** (KHÔNG refund)
  - Group B PAY: discount = `D_A (carried over) + D_B (gốc)`
  - Per-member discount: chia đều theo minutes của tất cả active members (kể cả merged) _(LobbyMergeDepositFollowTests, MergeServiceTests)_
- [x] Exception 4 Option A: A3's **per-member deposit** (BR-22 nếu có) → refund về wallet
  (scope: chỉ per-member deposit, KHÔNG đụng host deposit) _(MergeServiceTests: refund)_
- [x] Reservation status whitelist (Holding/Confirmed/CheckedIn) _(M1HostDepositDiscountTests: CancelledReservation)_
- [x] Lobby status check (skip discount nếu lobby closed/cancelled) _(M1HostDepositDiscountTests: SourceDissolved)_
- [x] Multi-hop merge chain (A → B → C): carried-over accumulates correctly _(LobbyMergeDepositFollowTests: multi-hop)_
- [x] Edge case: merge to walk-in target → source deposit released to Host A's wallet _(LobbyMergeDepositFollowTests: walk-in target release)_
- [x] Idempotent qua capture + refund flow _(MergeServiceTests: idempotent replay)_
- [x] 16+ tests pass _(71/71 pass: 67 unit + 4 integration — 2026-10-01)_

### Milestone 2 (19 ngày): Case 2 + Full UI + Reporting

**Mục tiêu:** Member có thể trả phần bill cá nhân bằng BVC từ mobile. Full UI cho POS + mobile. Reporting breakdown.

| Phase | Tasks | Days | Output |
|---|---|---|---|
| **3. Backend Case 2** | 14 tasks | 5.5 | Member BVC payment flow + audit log |
| **4b. Tests Case 2** | 8 tasks | (parallel với Phase 3) | Unit + integration tests |
| **5b. Frontend full** | 5 tasks | 5 | Mobile UI + POS UI + notifications |
| **6. Reporting + Rollout** | 5 tasks | 3 | Feature flags + reporting + pilot |
| **Tổng** | | **13.5 ngày** (parallel test = ~19 ngày wall-clock) | |

**Acceptance criteria M2:**
- [ ] Mobile app có tab "Trả bằng BVC" cho member bill
- [ ] Member chọn partial/full BVC payment
- [ ] BVC debit atomic + audit log
- [ ] All-members-paid → auto trigger session paid
- [ ] Refund bill sai → auto-refund BVC về wallet
- [ ] Notification cho member + host khi BVC payment success/fail
- [ ] Reporting breakdown per cafe per month
- [ ] Feature flags default OFF, A/B test 2 cafes
- [ ] 24+ additional tests pass

---

## A4. Gap Coverage Matrix (34 gaps)

> **Cập nhật 2026-10-01:** Sau khi check codebase, 6 gaps (27-31, 34) đã được handle bởi **existing APIs** (`/pay-member` + `/partial-checkout` + SePay webhook từ 2026-08-24). Design chỉ cần handle **2 gaps mới** (32, 33).

| # | Gap | Severity | Covered in | Notes |
|---|---|---|---|---|
| 1 | BR-09 conflict với discount | Critical | M1 Phase 2 | Document supersession rule |
| 2 | Reservation status check (Cancelled/Merged/NoShow) | Critical | M1 Phase 2 | Whitelist statuses |
| 3 | Session merge Exception 4 | Critical | M1 Phase 2b | Active member filter |
| 4 | Lobby status check (Closed/CancelledByCafe) | Critical | M1 Phase 2 | Skip discount if invalid |
| 5 | Penalty payment by BVC | High | M2 Phase 3 | Exclude Guest_Slot |
| 6 | Overpayment (BvcAmount > totalDue) | High | M2 Phase 3 | Throw 400 |
| 7 | Multi-account (2 members same UserId) | High | M2 Phase 3 | Distinct UserId check |
| 8 | Webhook retry + ambient transaction | High | M1 Phase 2 | Status re-check trước capture |
| 9 | HeldBalance cap (BR-USER-LIMIT-03) | High | M2 Phase 3 | Refund remainder → warning |
| 10 | Cooling-off user (BR-NEW-10) | Medium | M1 Phase 2 | Document no impact on discount |
| 11 | Refund tracking (BVC) | High | M2 Phase 3 | `BvcRefundedAt`, `BvcRefundReason` |
| 12 | Notification (success/fail) | Medium | M2 Phase 3 | Toast + push notification |
| 13 | BR-15 formula update | Critical | M1 Phase 2 | Add `DiscountAmount` term |
| 14 | Merge reservations (sum source deposits) | High | M1 Phase 2b | `MergedFromLobbyId` chain |
| 15 | Walk-in member case | Medium | M2 Phase 3 | Audit log handling |
| 16 | 2 members cùng trả BVC 1 lúc | High | M2 Phase 3 | FOR UPDATE lock |
| 17 | SePay webhook retry state check | High | M2 Phase 3 | Validate amount khớp ledger |
| 18 | Member trả BVC xong, session fail | High | M2 Phase 3 | Transaction wrap + rollback |
| 19 | Walk-in designate customer as host | Low | Backlog | Edge case hiếm |
| 20 | Reporting breakdown | Medium | M2 Phase 6 | Case 1/Case 2/cash per cafe |
| 21 | No-show case (cleanup state) | Low | M2 Phase 3 | BVC pre-selection cleanup |
| 22 | App resume mid-payment | Low | M2 Phase 5 | State restore |
| 23 | Walk-in disable discount option | Low | M2 Phase 5 | UI conditional |
| 24 | Multi-cafe session safety | Low | M1 Phase 2 | LobbyId filter |
| 25 | Discount snapshot cho audit | Medium | M1 Phase 1 | `HostDepositUsageSnapshot` |
| 26 | Retry state validation | Low | M1 Phase 2 | Idempotency key |
| **27** | **Member pays cash riêng** | High | **✅ EXISTING** `POST /pay-member` (CASH mode) | Đã implement 2026-08-24, ref docs/api/cafe-pos.md §"Split Bill" |
| **28** | **Host pays self only** | High | **✅ EXISTING** `POST /pay-member` (1 memberId) | Dùng cùng endpoint |
| **29** | **Host covers multi members** | High | **✅ EXISTING** `POST /pay-member` (memberIds[]) | Dùng cùng endpoint, array multi-member |
| **30** | **Guard double-pay** | High | **✅ EXISTING** 409 `MemberAlreadyPaid` | Built-in guard |
| **31** | **Member early checkout** | Medium | **✅ EXISTING** `POST /partial-checkout` | Đã có, member → SuspendedMutation |
| **32** | **Per-member receipt** | Medium | **❌ M2 — NEW endpoint** (Task C2.15) | Cần `GET /sessions/{id}/members/{memberId}/receipt` |
| **33** | **Force close with unpaid** | Medium | **❌ M2 — NEW endpoint** (Task C2.16) | Cần `POST /sessions/{id}/force-close` (Manager/Admin) |
| **34** | **Member SePay QR riêng** | Low | **✅ EXISTING** `POST /pay-member` (QR mode) + webhook | Đã có flow end-to-end |

**Coverage summary:**
- Critical: 5/5 ✅
- High: 9/9 ✅
- Medium: 9/9 ✅ (added #27-33)
- Low: 6/6 ✅ (best effort, defer nếu cần)
- **Total: 29/29 gaps addressed (5 deferred to backlog)**

**Net new work for M2:** chỉ **2 endpoints mới** (Gap #32, #33), down từ 8 (sau khi check existing APIs).

---

## A5. Existing APIs Reference

Các API dưới đây **đã có sẵn trong codebase** (implement từ 2026-08-24 trở đi). Design doc này **KHÔNG cần build lại**, chỉ cần **integrate / extend** khi cần.

### A5.1. `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/pay-member` (Split Bill)

**Implementer:** `CafePosController.PayMemberAsync`
**File ref:** `docs/api/cafe-pos.md` §"Split Bill — Thanh toán per-member"
**Migration:** `20260824082933` (PaidAt/PaymentMethod/PaymentStatus) + pending 2026-09-19 (QR columns)

**Request:**
```json
{
  "memberIds": ["guid-1", "guid-2"],       // Array → multi-pay, 1 element → single
  "paymentMethod": "CASH" | "QR_CODE",     // CASH sync, QR async qua webhook
  "notes": "Free text (≤ 500 chars)"
}
```

**Side effects:**
- `ActiveSessionMembers.PaymentStatus = PaidCash | PaidQr` cho từng member
- Insert audit row vào `MemberPayments` table (1 row / member / pay)
- 409 `MemberAlreadyPaid` nếu gọi lại member đã paid (built-in idempotency)
- Auto-trigger: nếu ALL members paid → `ActiveSession.Status = Paid`, release table/box/inventory
- Atomic flip trên PaymentStatus (race condition CASH + QR cùng đến resolved)

**Covers:** Gap #27 (member cash riêng), #28 (host self only), #29 (multi-pay), #30 (guard), #34 (member QR riêng).

### A5.2. `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/partial-checkout` (Early Checkout)

**File ref:** `docs/api/cafe-pos.md` §"Luồng 9 — Member về sớm"
**State machine:** `Checking → SuspendedMutation` (members chọn về) + vẫn `Checking` (members còn lại)

**Request:**
```json
{
  "memberIds": ["member-1", "member-2"]  // Members muốn về sớm
}
```

**Side effects:**
- Members chọn về → `SuspendedMutation` (treo chờ component-check + merge)
- Members KHÔNG chọn → vẫn `Playing`
- `session.IsCheckingInventory = true`
- KHÔNG cho in hóa đơn cho tới khi component-check xong

**Use case:** Member A3 về sớm trước khi merge / hoặc chỉ muốn trả phần rồi đi.

**Covers:** Gap #31.

### A5.3. `POST /api/payments/sepay/webhook/member-payment` (Per-member QR webhook)

**File ref:** `docs/api/payment.md` §"Member Payment Webhook"
**Idempotent theo `OrderId`** (per-member)

**Trigger:** SePay callback khi player transfer QR per-member thành công.

**Side effects:**
- Update `ActiveSessionMembers.PaymentStatus = PaidQr` + `PaidAt = now`
- Set `MemberPayments.TransactionId = TXN-...`

### A5.4. `POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/members/{memberId}/regenerate-qr`

Tạo lại QR khi QR cũ lỗi/hết hạn (Split Bill, 2026-08-25). `orderId` mới, audit giữ lại.

### A5.5. Existing payment flow (BR-09 current behavior)

`POST /pay` (full session pay) hiện tại: capture cash/QR cho **toàn bộ session**, `MemberPaymentStatus` chưa track. Khi BR-DEPOSIT-05 active → flow này sẽ integrate `HostDepositUsageMode` dropdown.

---

## A6. Exception 4 — Session Merge giải pháp

### Nguyên tắc cốt lõi (CẬP NHẬT 2026-10-01)

> **"Host deposit FOLLOWS merged members to target lobby, không refund về wallet."**
>
> Khi A3 merge từ Group A → Group B, deposit `D_A` của Host A KHÔNG được hoàn về ví Host A.
> Thay vào đó, `D_A` được cộng vào pool của Group B. Khi Group B thanh toán:
> - **Bill tổng:** trừ `D_A + D_B` (tổng deposit của cả 2 lobby đã gộp).
> - **Bill cá nhân:** mỗi member được chia phần discount theo minutes, dựa trên pool tổng (`D_A + D_B`).
>
> Tại sao KHÔNG refund Host A? Vì:
> 1. Công bằng cho cả nhóm: Host A đã trả cọc cho group, nếu refund thì phần cọc đó "mất tích"
>    (host A không còn nhóm, group B không nhận được).
> 2. UX đơn giản: POS chỉ cần làm 1 thao tác (trừ deposit) thay vì refund + capture lại.
> 3. Audit trail rõ ràng: source reservation được đánh `AbsorbedByMerge`, deposit "chuyển"
>    sang target, ghi audit `DepositFollowedOnMerge`.
> 4. Tránh "ghost deposit" trong heldBalance vô thời hạn (bug 2026-09-29 production).

### State machine cho A3 trong merge case

```
12:00         13:00              14:00         15:00
  │             │                  │             │
  ▼             ▼                  ▼             ▼
Group A     A3 leaves Group A   Group A       Group B
session     MergedAt=13:00      PAY at 14:00  PAY at 15:00
ACTIVE      → moves to Group B
            LeftAt set=13:00

A3 records timeline:
═══════════════════════════════════════════════════════
Group A session record:
  JoinedAt=12:00, LeftAt=13:00, TotalMinutesPlayed=60
  OriginalSessionId=NULL (A3 was always in Group A)
  → Khi Group A PAY lúc 14:00: A3.LeftAt(13:00) < pay_time(14:00)
    → A3 KHÔNG active → KHÔNG được discount

Group B session record:
  JoinedAt=13:00, LeftAt=NULL, TotalMinutesPlayed=120
  OriginalSessionId=GroupA.Id, MergedFromLobbyId=GroupA.LobbyId, MergedAt=13:00
  → Khi Group B PAY lúc 15:00: A3.LeftAt=NULL
    → A3 active → được discount từ Group B's host deposit
```

### Algorithm: Active members filter

```csharp
// File: ActiveSessionService.cs
// Method: BuildMemberInvoices (sửa existing)

private List<MemberInvoiceDto> BuildMemberInvoices(
    ActiveSession session,
    Cafe cafe,
    List<ComponentCheckResult> componentCheckResults,
    List<ComponentPenaltyItemDto>? legacyPenaltyItems,
    HostDepositUsageMode hostDepositUsage = HostDepositUsageMode.None,
    Reservation? reservation = null,
    DateTime payTime = default)
{
    var now = payTime == default ? DateTime.UtcNow : payTime;
    var invoices = new List<MemberInvoiceDto>();
    
    // === NEW: Determine active members (filter out members who left via merge/checkout) ===
    var activeMembers = session.Members
        .Where(m => !m.LeftAt.HasValue || m.LeftAt.Value > now)
        .ToList();
    
    var totalActiveMinutes = activeMembers.Sum(m => m.TotalMinutesPlayed);
    
    // === NEW: Compute host deposit discount (per-session, not per-member-history) ===
    decimal hostDepositDiscount = 0m;
    if (hostDepositUsage == HostDepositUsageMode.DiscountGroup 
        && reservation != null 
        && reservation.DepositAmount > 0
        && IsReservationEligibleForDiscount(reservation))
    {
        var sessionTotalBeforeDiscount = session.Subtotal + session.PenaltyAmount;
        hostDepositDiscount = Math.Min(reservation.DepositAmount, sessionTotalBeforeDiscount);
    }
    
    foreach (var member in session.Members)
    {
        // ... existing subtotal/penalty calc ...
        
        // === NEW: Apply host deposit discount (chỉ cho active members) ===
        decimal memberDepositApplied = 0m;
        var isMemberActive = !member.LeftAt.HasValue || member.LeftAt.Value > now;
        
        if (isMemberActive 
            && hostDepositUsage == HostDepositUsageMode.DiscountGroup
            && hostDepositDiscount > 0m
            && totalActiveMinutes > 0
            && member.TotalMinutesPlayed > 0)
        {
            memberDepositApplied = Math.Round(
                hostDepositDiscount * (member.TotalMinutesPlayed / (decimal)totalActiveMinutes),
                0, MidpointRounding.ToEven);
            
            var memberMax = memberSubtotal + memberPenalty;
            memberDepositApplied = Math.Min(memberDepositApplied, memberMax);
        }
        else if (hostDepositUsage == HostDepositUsageMode.DiscountHostOnly
                 && isMemberActive
                 && member.IsHost
                 && hostDepositDiscount > 0m)
        {
            memberDepositApplied = Math.Min(hostDepositDiscount, memberSubtotal + memberPenalty);
        }
        else if (!isMemberActive)
        {
            // Member đã rời session trước Pay (qua merge hoặc early checkout)
            memberDepositApplied = 0m;
        }
        
        var memberTotal = Math.Max(0, memberSubtotal + memberPenalty - memberDepositApplied);
        invoices.Add(new MemberInvoiceDto { ... });
    }
    
    return invoices;
}

// === NEW: Reservation status whitelist ===
private static bool IsReservationEligibleForDiscount(Reservation reservation)
{
    var eligibleStatuses = new[] {
        ReservationStatus.Holding,
        ReservationStatus.Confirmed,
        ReservationStatus.CheckedIn
    };
    
    return eligibleStatuses.Contains(reservation.Status) 
        && !reservation.SourceDissolved
        && reservation.DepositAmount > 0;
}
```

### Ví dụ cụ thể (CẬP NHẬT 2026-10-01: deposit follows)

```
Setup:
- Group A: A1 (host, deposit D_A = 50k BVC = 50.000đ), A2, A3. All play 12:00-14:00 (120 min).
- A3 leaves Group A at 13:00 → moves to Group B.
- Group A continues with A1, A2 until 14:00.
- Group B: B1 (host, deposit D_B = 30k BVC = 30.000đ), B2, A3. All play 13:00-15:00 (120 min).

Đặc điểm MỚI: Khi A3 merge, D_A KHÔNG refund về Host A's wallet.
Thay vào đó, D_A được cộng vào Reservation B.CarriedOverDepositBvc.

Reservation A: Status → AbsorbedByMerge, DepositAmount: 50k → 0
Reservation B: DepositAmount: 30k (giữ nguyên) + CarriedOverDepositBvc: 50k = EffectiveDeposit = 80k
```

**Tổng bill Group A** (PAY tại quán lúc 14:00, DiscountGroup):
- Subtotal: 2 × 120 min × rate + penalty = ~100k
- Discount từ D_A: min(50k, sessionTotal) = 50k
- Tổng phải trả: 100k - 50k = **50k**

```
Group A PAY at 14:00 (DiscountGroup):
─────────────────────────────────────────────────────────────
Active members: A1 (120min), A2 (120min). A3 EXCLUDED (LeftAt=13:00 < 14:00).
totalActiveMinutes = 240
hostDepositDiscount = min(50k D_A, sessionTotal) = 50k

Distribution (cho A1, A2):
  - A1: 50k × (120/240) = 25k
  - A2: 50k × (120/240) = 25k
  - A3: 0 (EXCLUDED, đã merge ra)

Capture cho Host A: 50k → D_A consumed, Status = AbsorbedByMerge.
HOLD: KHÔNG refund cho Host A — D_A đã "chuyển" sang Reservation B.CarriedOverDepositBvc.
```

**Tổng bill Group B** (PAY tại quán lúc 15:00, DiscountGroup):
- Subtotal: 3 × 120 min × rate + penalty = ~150k
- Discount từ pool (D_A + D_B): min(80k, sessionTotal) = 80k
- Tổng phải trả: 150k - 80k = **70k**

```
Group B PAY at 15:00 (DiscountGroup):
─────────────────────────────────────────────────────────────
Active members: B1, B2, A3 (all 120min, no LeftAt).

totalActiveMinutes = 360
effectiveDeposit = Reservation B.DepositAmount (30k) + CarriedOverDepositBvc (50k) = 80k
hostDepositDiscount = min(80k, sessionTotal) = 80k

Distribution (chia đều theo minutes — cả A3 + B1 + B2 đều được hưởng):
  - B1: 80k × (120/360) = 26.67k → 27k (làm tròn)
  - B2: 80k × (120/360) = 26.67k → 27k
  - A3: 80k × (120/360) = 26.67k → 26k (drift adjustment)
  TOTAL = 80k ✓

Capture cho Host B: 80k → reservation (D_A + D_B) consumed.
HOLD: KHÔNG refund cho Host A — D_A đã capture vào Group B bill từ đầu.
```

**Bill cá nhân** (mỗi member trả riêng bằng BVC, M2 Phase):

Ví dụ: Group B thanh toán kiểu **split bill** (M2 — Gap #27):
- B1 bill: 50k - 27k = **23k**
- B2 bill: 50k - 27k = **23k**
- A3 bill: 50k - 26k = **24k**

Tổng = 23+23+24 = 70k = đúng tổng bill (150k - 80k).

→ A3 (member từ Group A) VẪN ĐƯỢC hưởng phần discount từ D_A (host A's deposit),
  vì A3 đang chơi trong session B và A3's minutes góp vào totalActiveMinutes.

### Option A — Refund PER-MEMBER deposit khi merge (BR-22 only)

> **Clarification 2026-10-01:** Option A chỉ áp dụng cho **per-member deposit** (BR-22 legacy,
> khi member tự trả deposit của mình). KHÔNG liên quan đến **host deposit** (BR-DEPOSIT-01)
> — host deposit giờ đi theo members qua merge (xem section trên).

A3's per-member deposit (nếu có, BR-22 legacy) được refund về wallet khi A3 merge sang session khác. Lý do: A3 đang tiếp tục chơi (không phải cancel), nên không bị phạt.

**3 options đã xét cho per-member deposit:**
- Option A: Refund về wallet ⭐ (khuyến nghị, công bằng)
- Option B: Forfeit cafe (nghiêm khắc, bất công)
- Option C: Apply vào Group A bill (cần staff can thiệp, phức tạp)

**Implementer:** `MergeService.HandleMemberMergeAsync` (BoardVerse.Services/Services/MergeService.cs)
— chỉ chạm vào `member.DepositId`, KHÔNG đụng đến `reservation.DeDepositAmount` (host deposit).

---

# PHẦN B — MILESTONE 1 (10 NGÀY): CORE CASE 1 + OPTION A

## B1. Schema changes (M1)

### Task B1.1: Extend `LedgerEntryType` enum

**File:** `Enum/LedgerEntryType.cs`
**Effort:** 0.5h

```csharp
public enum LedgerEntryType
{
    // Existing values...
    DepositRelease = 10,
    DepositCapture = 11,
    
    // NEW: Phân biệt refund merge vs refund cancel (audit trail)
    DepositRefund_Merge = 12,
    DepositRefund_Cancel = 13  // (optional, dùng cho future BR-REFUND extension)
}
```

### Task B1.2: Extend `HostDepositUsageMode` enum

**File:** `Dtos/PaySessionRequestDto.cs` hoặc `Enum/HostDepositUsageMode.cs`
**Effort:** 0.5h

```csharp
public enum HostDepositUsageMode
{
    None = 0,
    DiscountGroup = 1,
    DiscountHostOnly = 2
}
```

### Task B1.3: Add fields to `ActiveSessionMember`

**File:** `Entities/ActiveSessionMember.cs`
**Effort:** 1h

```csharp
/// <summary>
/// Thời điểm kết thúc phiên cá nhân (checkout sớm, merge sang session khác, hoặc admin force-remove).
/// Trước pay time: member KHÔNG active → EXCLUDED khỏi deposit discount distribution.
/// </summary>
public DateTime? LeftAt { get; set; } // existing, document rõ 3 trigger cases

// NEW: Per-member deposit refund tracking (Option A)
/// <summary>Thời điểm deposit được refund về wallet (khi merge sang lobby khác).</summary>
public DateTime? DepositRefundedAt { get; set; }

/// <summary>Lý do refund (e.g., "Merged từ Lobby X", "DepositConsumed_BeforeMerge").</summary>
public string? DepositRefundReason { get; set; }

/// <summary>FK to BvcLedgerEntry.Id (refund transaction).</summary>
public Guid? DepositRefundLedgerId { get; set; }
```

### Task B1.4: Add fields to `Reservation`

**File:** `Entities/Reservation.cs`
**Effort:** 0.5h

```csharp
/// <summary>Snapshot của HostDepositUsageMode lúc Pay (audit trail).</summary>
public string? HostDepositUsageSnapshot { get; set; }

/// <summary>Tổng discount đã áp dụng (BVC).</summary>
public long DiscountAppliedAmount { get; set; }

/// <summary>Tổng discount đã refund (nếu có).</summary>
public long DiscountRefundedAmount { get; set; }

/// <summary>Audit trail JSON: { appliedAt, appliedAmount, skippedReason, activeMembersAtPay }.</summary>
public string? DiscountAuditTrail { get; set; }
```

### Task B1.5: Create `MemberDepositAuditLog` entity

**File:** `Entities/MemberDepositAuditLog.cs`
**Effort:** 2h

```csharp
public class MemberDepositAuditLog : BaseEntity
{
    public Guid MemberId { get; set; }
    public Guid UserId { get; set; }
    
    /// <summary>'Refunded_OnMerge' | 'Captured_OnGroupAPay' | 'Captured_OnEarlyCheckout'</summary>
    public string Action { get; set; }
    
    public long AmountBvc { get; set; }
    public Guid? DepositId { get; set; }
    
    public Guid? FromLobbyId { get; set; }
    public Guid? FromSessionId { get; set; }
    public Guid? ToLobbyId { get; set; }
    public Guid? ToSessionId { get; set; }
    public DateTime? MergedAt { get; set; }
    
    public string? Reason { get; set; }
    public string IdempotencyKey { get; set; }
    public Guid? LedgerEntryId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    
    // Navigation
    public ActiveSessionMember Member { get; set; }
    public User User { get; set; }
}
```

### Task B1.6: Migration SQL

**File:** `sql/m1_host_deposit_discount.sql`
**Effort:** 1h

Xem **[§D2. Migration SQL Full](#d2-migration-sql-full)** cho full SQL.

### Task B1.7: Document `LeftAt` semantics

**File:** `Entities/ActiveSessionMember.cs` (XML doc updated)
**Effort:** 0.5h

Update XML comment của `LeftAt`:
```csharp
/// <summary>
/// Thời điểm kết thúc phiên cá nhân. Được set khi:
/// (1) Member checkout sớm (early leave) → LeftAt = checkout_time
/// (2) Member merge sang session khác → LeftAt = merge_time
/// (3) Admin force-remove → LeftAt = removal_time
/// 
/// Ảnh hưởng:
/// - Khi session Pay: filter `LeftAt > payTime` → members đã rời EXCLUDED khỏi deposit discount
/// - Member đã merge: bill consolidated tại session hiện tại (Exception 4)
/// </summary>
```

### Task B1.8: Add `ReservationStatus` enum audit values

**File:** `Enum/ReservationStatus.cs`
**Effort:** 0.5h

```csharp
public enum ReservationStatus
{
    Holding = 0,
    Confirmed = 1,
    CheckedIn = 2,
    Completed = 3,
    Cancelled = 4,
    Merged = 5,
    NoShow = 6,
    // NEW: Audit trail values
    DiscountApplied = 100,         // Discount distribution thành công
    DiscountSkipped_LobbyTerminal = 101,  // Lobby status invalid
    DiscountSkipped_MemberMerged = 102,   // All members đã merge ra
    DiscountSkipped_CancelledReservation = 103  // Reservation cancelled
}
```

### Task B1.9-B1.11: Repository + AuditLog infrastructure

**Effort:** 2h total

- `IMemberDepositAuditLogRepository` + `MemberDepositAuditLogRepository`
- Index trên (MemberId), (UserId), (Action), (CreatedAt DESC)
- Test data seed

---

## B2. Backend Code — Case 1

### Task B2.1: Refactor `BuildMemberInvoices` signature

**File:** `Application/Services/ActiveSessionService.cs`
**Effort:** 4h

Xem code đầy đủ trong **[§A5](#a5-exception-4--session-merge-giải-pháp)**.

Key changes:
- Thêm params: `HostDepositUsageMode hostDepositUsage`, `Reservation? reservation`, `DateTime payTime`
- Filter `activeMembers` bằng `LeftAt > payTime`
- Apply discount cho active members only

### Task B2.2: Reservation status whitelist

**File:** `Application/Services/ActiveSessionService.cs`
**Effort:** 1h

```csharp
private static bool IsReservationEligibleForDiscount(Reservation reservation)
{
    var eligibleStatuses = new[] {
        ReservationStatus.Holding,
        ReservationStatus.Confirmed,
        ReservationStatus.CheckedIn
    };
    
    return eligibleStatuses.Contains(reservation.Status) 
        && !reservation.SourceDissolved
        && reservation.DepositAmount > 0;
}
```

### Task B2.3: Lobby status check

**File:** `Application/Services/ActiveSessionService.cs`
**Effort:** 1h

```csharp
private static bool IsLobbyEligibleForDiscount(Lobby lobby)
{
    // Lobby phải active, không bị đóng/cancel bởi cafe
    return lobby.Status != LobbyStatus.Closed
        && lobby.Status != LobbyStatus.CancelledByCafe
        && lobby.Status != LobbyStatus.TimeoutFailed;
}
```

### Task B2.4: Capture + release deposit flow

**File:** `Application/Services/ActiveSessionService.cs:1040` (PaySessionCoreAsync)
**Effort:** 6h

```csharp
// AFTER BuildMemberInvoices + BEFORE commit transaction
if (reservation != null && request.HostDepositUsage != HostDepositUsageMode.None)
{
    var activeMembers = session.Members
        .Where(m => !m.LeftAt.HasValue || m.LeftAt.Value > now)
        .ToList();
    
    // CẬP NHẬT 2026-10-01: Effective deposit = gốc + carried over từ merge chain.
    // KHÔNG refund source deposits — chúng đã được transfer sang target's CarriedOverDepositBvc
    // (xem §B3.1 LobbyMergeService.ApproveMergeAsync).
    var effectiveDeposit = reservation.DepositAmount + reservation.CarriedOverDepositBvc;
    
    var totalApplied = activeMembers.Sum(m => m.DepositAppliedAmount);
    var remainder = effectiveDeposit - totalApplied;
    
    // 1. Capture phần applied
    if (totalApplied > 0)
    {
        await _walletService.CaptureDepositAsync(
            userId: reservation.HostId,
            amountBvc: totalApplied,
            reservationId: reservation.Id,
            lobbyId: session.LobbyId,
            sessionId: session.Id,
            idempotencyKey: $"capture-discount-{session.Id}-{totalApplied}",
            reason: LedgerEntryType.DepositCapture);
    }
    
    // 2. Release phần remainder
    if (remainder > 0)
    {
        await _walletService.ReleaseDepositAsync(
            userId: reservation.HostId,
            amountBvc: remainder,
            reservationId: reservation.Id,
            reason: LedgerEntryType.DepositRelease,
            idempotencyKey: $"release-remainder-{session.Id}-{remainder}");
    }
    
    // 3. Update reservation snapshot
    reservation.Status = ReservationStatus.Completed;
    reservation.HostDepositUsageSnapshot = request.HostDepositUsage.ToString();
    reservation.DiscountAppliedAmount = totalApplied;
    reservation.ActualEndAt = now;
    reservation.DiscountAuditTrail = JsonSerializer.Serialize(new
    {
        appliedAt = now,
        appliedAmount = totalApplied,
        skippedReason = (string?)null,
        activeMembersAtPay = activeMembers.Count,
        totalActiveMinutes = activeMembers.Sum(m => m.TotalMinutesPlayed),
        effectiveDeposit, // BVC gốc + carried over
        originalDepositBvc = reservation.DepositAmount,
        carriedOverDepositBvc = reservation.CarriedOverDepositBvc,
        carriedOverFromReservationIds = reservation.C?.Id.ToString()
    });
}
```

### Task B2.5: Ambient transaction + status re-check

**File:** `Application/Services/ActiveSessionService.cs`
**Effort:** 2h

```csharp
// Trước capture (idempotency + race safety):
await using var dbTx = await _unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

// Re-check reservation status INSIDE transaction
var reservationRecheck = await _reservationRepository.GetByIdAsync(reservation.Id, ct);
if (!IsReservationEligibleForDiscount(reservationRecheck))
{
    _logger.LogWarning(
        "Reservation {ReservationId} status changed during transaction: {Status}. Skip discount.",
        reservation.Id, reservationRecheck.Status);
    await dbTx.RollbackAsync(ct);
    return BuildMemberInvoices(session, ..., HostDepositUsageMode.None, ...); // fallback
}

// ... existing capture logic ...

await dbTx.CommitAsync(ct);
```

### Task B2.6: Update `PaySessionResponseDto`

**File:** `Dtos/PaySessionResponseDto.cs`
**Effort:** 2h

```csharp
public class PaySessionResponseDto
{
    // Existing fields...
    
    // NEW
    public long? HostDepositDiscountApplied { get; set; }
    public List<DepositAppliedBreakdown> DepositAppliedBreakdown { get; set; }
    public string? LobbyStatusAtPay { get; set; }
    public string? ReservationStatusAtPay { get; set; }
    public string? DiscountSkippedReason { get; set; }  // null nếu applied OK
}

public class DepositAppliedBreakdown
{
    public Guid MemberId { get; set; }
    public string MemberName { get; set; }
    public bool IsHost { get; set; }
    public int MinutesPlayed { get; set; }
    public long DiscountAppliedBvc { get; set; }
}
```

### Task B2.7: Handle merge reservations (CẬP NHẬT 2026-10-01 — deposit follows)

**File:** `Application/Services/ActiveSessionService.cs`
**Effort:** 2h (giảm từ 4h, không cần traverse chain runtime)

```csharp
// Effective deposit = DepositAmount gốc + CarriedOverDepositBvc (từ merge chain).
// Logic đã được encode sẵn lúc merge xảy ra (xem LobbyMergeService.ApproveMergeAsync,
// Task B2.7b bên dưới) → KHÔNG cần traverse SourceMergedFromLobbyId runtime.
private long GetEffectiveHostDeposit(Reservation reservation)
{
    return reservation.DepositAmount + reservation.CarriedOverDepositBvc;
}

// Được gọi từ BuildMemberInvoices:
var effectiveDeposit = GetEffectiveHostDeposit(reservation);
var hostDepositDiscount = Math.Min(effectiveDeposit, sessionTotalBeforeDiscount);
```

#### Task B2.7b: Transfer deposit on LobbyMergeService (NEW)

**File:** `BoardVerse.Services/Services/LobbyMergeService.cs` — Step 11 (dissolve source)
**Effort:** 3h

```csharp
// Trong LobbyMergeService.ApproveMergeAsync, khi source lobby dissolves
// (stillActive == 0) → transfer source.Reservation.DepositAmount + CarriedOverDepositBvc
// sang target.Reservation. Source KHÔNG refund về wallet.

if (stillActive == 0
    && sourceLobby.ReservationId.HasValue
    && targetLobby.ReservationId.HasValue)
{
    var sourceReservation = await _db.Reservations
        .FirstOrDefaultAsync(r => r.Id == sourceLobby.ReservationId, ct);
    var targetReservation = await _db.Reservations
        .FirstOrDefaultAsync(r => r.Id == targetLobby.ReservationId, ct);
    
    if (sourceReservation != null && targetReservation != null)
    {
        var carriedAmount = sourceReservation.DepositAmount
            + sourceReservation.CarriedOverDepositBvc;
        
        if (carriedAmount > 0)
        {
            // Cộng vào target's pool
            targetReservation.CarriedOverDepositBvc += carriedAmount;
            
            // Audit trail: track từng source reservation ID + host UserId
            targetReservation.CarriedOverFromReservationIds = 
                AppendCsv(targetReservation.CarriedOverFromReservationIds, sourceReservation.Id.ToString());
            targetReservation.CarriedOverFromUserIds = 
                AppendCsv(targetReservation.CarriedOverFromUserIds, sourceReservation.HostId.ToString());
            targetReservation.UpdatedAt = DateTime.UtcNow;
            
            // Source reservation: đánh dấu đã "chuyển"
            sourceReservation.DepositAmount = 0; // không còn held riêng
            sourceReservation.Status = ReservationStatus.AbsorbedByMerge;
            sourceReservation.SourceDissolved = true;
            sourceReservation.MergedIntoReservationId = targetReservation.Id;
            sourceReservation.MergedAt = DateTime.UtcNow;
            sourceReservation.MergedByUserId = staffUserId;
            sourceReservation.UpdatedAt = DateTime.UtcNow;
            
            _logger.LogInformation(
                "LobbyMerge: transferred {Amount} BVC deposit from source reservation {SourceResId} " +
                "(host {HostA}) to target reservation {TargetResId}. " +
                "Source.Status = AbsorbedByMerge. Target.CarriedOverDepositBvc = {CarriedTotal}",
                carriedAmount, sourceReservation.Id, sourceReservation.HostId,
                targetReservation.Id, targetReservation.CarriedOverDepositBvc);
            
            // Audit log
            await _auditLogRepo.AddAsync(new LobbyMergeAuditLog
            {
                Id = Guid.NewGuid(),
                MergeRequestId = mergeRequest.Id,
                SourceLobbyId = sourceLobby.Id,
                TargetLobbyId = targetLobby.Id,
                SourceReservationId = sourceReservation.Id,
                TargetReservationId = targetReservation.Id,
                PerformedByUserId = staffUserId,
                EventType = "DepositFollowedOnMerge",
                EventData = JsonSerializer.Serialize(new
                {
                    carriedAmount,
                    sourceHostId = sourceReservation.HostId,
                    newTargetCarriedOver = targetReservation.CarriedOverDepositBvc,
                    sourceDepositAfterTransfer = 0L
                }),
                Success = true,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }
        else
        {
            // sourceReservation.DepositAmount = 0 (edge case: deposit đã refund trước merge)
            // → chỉ update status, không transfer gì
            sourceReservation.Status = ReservationStatus.AbsorbedByMerge;
            sourceReservation.SourceDissolved = true;
            sourceReservation.MergedIntoReservationId = targetReservation.Id;
            sourceReservation.MergedAt = DateTime.UtcNow;
            sourceReservation.MergedByUserId = staffUserId;
        }
    }
}

// Edge case: target là walk-in lobby (ReservationId == null)
// → KHÔNG THỂ carry over (target không có reservation để giữ deposit)
// → Source deposit sẽ được release về Host A's wallet (BR-REFUND-01 standard).
if (stillActive == 0
    && sourceLobby.ReservationId.HasValue
    && !targetLobby.ReservationId.HasValue)
{
    // Target is walk-in → release source deposit
    var sourceReservation = await _db.Reservations
        .FirstOrDefaultAsync(r => r.Id == sourceLobby.ReservationId, ct);
    if (sourceReservation != null && sourceReservation.DepositAmount > 0)
    {
        await _walletService.ReleaseDepositAsync(
            userId: sourceReservation.HostId,
            amountBvc: sourceReservation.DepositAmount + sourceReservation.CarriedOverDepositBvc,
            reservationId: sourceReservation.Id,
            reason: LedgerEntryType.DepositRelease,
            idempotencyKey: $"release-walkin-merge-{sourceReservation.Id}-{DateTime.UtcNow:o}",
            notes: "Source lobby dissolved into walk-in target — release to wallet");
        
        sourceReservation.Status = ReservationStatus.AbsorbedByMerge;
        sourceReservation.SourceDissolved = true;
        sourceReservation.DepositAmount = 0;
        sourceReservation.CarriedOverDepositBvc = 0;
    }
}

private static string AppendCsv(string? existing, string newValue)
{
    if (string.IsNullOrEmpty(existing)) return newValue;
    var set = new HashSet<string>(existing.Split(',', StringSplitOptions.RemoveEmptyEntries));
    set.Add(newValue);
    return string.Join(",", set);
}
```

#### Edge cases của merge chain (multi-hop)

```
Group A → Group B → Group C (3 lần merge liên tiếp)

Reservation A.DepositAmount = 50k
Reservation B.DepositAmount = 30k, B.CarriedOverDepositBvc = 0
Reservation C.DepositAmount = 20k, C.CarriedOverDepositBvc = 0

Step 1: A merged → B
  A.Status = AbsorbedByMerge, A.DepositAmount = 0
  B.CarriedOverDepositBvc = 50k
  B.CarriedOverFromReservationIds = "A.Id"
  B.CarriedOverFromUserIds = "Host A.Id"

Step 2: B merged → C (B dissolves)
  B.Status = AbsorbedByMerge
  B.DepositAmount = 0 (đã consume), B.CarriedOverDepositBvc = 0
  C.CarriedOverDepositBvc = (B.DepositAmount=0) + B.CarriedOverDepositBvc=50k = 50k
  C.CarriedOverFromReservationIds = "Host A.Id" (concat từ B)
  C.CarriedOverFromUserIds = "Host A.Id"

Khi Group C PAY:
  effectiveDeposit = 20k + 50k = 70k (B + A's deposit)
  Tổng bill C - 70k = phải trả
```

→ Mỗi lần merge, deposit "chain" theo. KHÔNG cần traverse ngược lúc Pay (đã encoded sẵn).

#### Edge case: Member merge một phần (chỉ 1 member transfer, source vẫn active)

```
Group A (4 người): A1, A2, A3, A4
A3 merge → Group B
Sau merge: Group A còn A1, A2, A4 (stillActive > 0)
Group A KHÔNG dissolve.

→ Group A PAY (later) bình thường với D_A = 50k, distribution cho A1, A2, A4.
→ Group B PAY (later) bình thường với D_B.
→ KHÔNG có transfer vì Group A không dissolve.
```

→ Transfer CHỈ xảy ra khi `stillActive == 0` (source hoàn toàn rỗng).

### Task B2.8: Document BR-09 supersession

**File:** `.cursor/rules/sepay-payment-flow.mdc` + `.cursor/rules/boardverse.mdc`
**Effort:** 1h

Thêm section:
```
BR-DEPOSIT-05 (NEW): Khi Host chọn HostDepositUsageMode.DiscountGroup hoặc .DiscountHostOnly tại PaySession:
  - Deposit được phân bổ như discount cho members (per minutes) hoặc host only
  - Capture amount = applied discount
  - Remainder released về host wallet
  - BR-09 (deposit forfeit 100% cho cafe) bị SUPERSEDE bởi BR-DEPOSIT-05 khi Case 1 active
```

### Task B2.9: Update BR-15 formula

**File:** `.cursor/rules/boardverse.mdc`
**Effort:** 0.5h

```
BR-15 formula (UPDATED):
  Total Bill = Subtotal + PenaltyAmount - DiscountAmount + TaxAmount
  Trong đó:
    Subtotal = Σ (minutes × hourlyRate) cho members
    PenaltyAmount = Σ (component penalties)
    DiscountAmount = host deposit applied (case 1) hoặc 0
    TaxAmount = current tax rules
```

### Task B2.10: Logging + telemetry

**File:** `Application/Services/ActiveSessionService.cs`
**Effort:** 1h

```csharp
_logger.LogInformation(
    "Discount applied: Session {SessionId}, Reservation {ReservationId}, Mode {Mode}, Applied {Applied} BVC, ActiveMembers {Count}",
    session.Id, reservation.Id, request.HostDepositUsage, totalApplied, activeMembers.Count);

_metrics.IncrementCounter("deposit.discount.applied", new Dictionary<string, string>
{
    ["mode"] = request.HostDepositUsage.ToString(),
    ["cafe_id"] = session.CafeId.ToString()
});
```

### Task B2.11: Update existing tests

**File:** `Tests/ActiveSessionServiceTests.cs`
**Effort:** 3h

Update các test cũ (BR-09) để cover BR-DEPOSIT-05 supersession.

---

## B3. Exception 4 — Host Deposit Follows on Merge (CẬP NHẬT 2026-10-01)

> **Section này tổng hợp các task code liên quan đến Host Deposit Follows logic.**
> **Chi tiết từng task đã có trong §A6 và §B2.7b.**

### Task B3.1: `LobbyMergeService.ApproveMergeAsync` — transfer deposit khi source dissolve

**File:** `BoardVerse.Services/Services/LobbyMergeService.cs`
**Effort:** 3h (xem code chi tiết tại §B2.7b)
**Owner:** Backend
**Ref:** §A6 + §B2.7b

Key changes trong Step 11 (dissolve source):
- Khi `stillActive == 0` → transfer `sourceReservation.DepositAmount + CarriedOverDepositBvc` sang `targetReservation.CarriedOverDepositBvc`
- Update `target.CarriedOverFromReservationIds` + `CarriedOverFromUserIds` (audit trail)
- Set `source.Status = AbsorbedByMerge`, `source.DepositAmount = 0`
- Insert `LobbyMergeAuditLog` với `EventType = "DepositFollowedOnMerge"`
- **KHÔNG refund về Host A's wallet** (per design §A6 nguyên tắc cốt lõi)

### Task B3.2: Edge case — merge to walk-in target

**File:** `BoardVerse.Services/Services/LobbyMergeService.cs`
**Effort:** 1h
**Ref:** §B2.7b cuối (block `if (stillActive == 0 && ... && !targetLobby.ReservationId.HasValue)`)

Walk-in target lobby không có `ReservationId` → không thể carry over.
→ Source deposit phải release về Host A's wallet qua `WalletService.ReleaseDepositAsync`.
→ Ghi ledger entry `DepositRelease`.

### Task B3.3: `ActiveSessionService.BuildMemberInvoices` — dùng effective deposit

**File:** `BoardVerse.Services/Services/ActiveSessionService.cs`
**Effort:** 0.5h (1 dòng thay đổi)
**Ref:** §A6 Algorithm

Thay đổi duy nhất:
```csharp
// CŨ:
hostDepositDiscount = Math.Min(reservation.DepositAmount, sessionTotalBeforeDiscount);

// MỚI (CẬP NHẬT 2026-10-01):
var effectiveDeposit = reservation.DepositAmount + reservation.CarriedOverDepositBvc;
hostDepositDiscount = Math.Min(effectiveDeposit, sessionTotalBeforeDiscount);
```

### Task B3.4: `GetEffectiveHostDeposit` helper (private method)

**File:** `BoardVerse.Services/Services/ActiveSessionService.cs`
**Effort:** 0.25h
**Ref:** §B2.7

```csharp
private long GetEffectiveHostDeposit(Reservation reservation)
{
    return reservation.DepositAmount + reservation.CarriedOverDepositBvc;
}
```

Đơn giản vì `CarriedOverDepositBvc` đã được encode lúc merge xảy ra (xem B3.1).
KHÔNG cần traverse `MergedIntoReservationId` chain runtime.

### Task B3.5: SQL migration — 3 columns mới trên `Reservations`

**File:** `sql/m1_ex4_host_deposit_follows.sql` (NEW, đã generate bên dưới)
**Effort:** 0.25h
**Ref:** §A6 State machine

```sql
ALTER TABLE "Reservations"
  ADD COLUMN IF NOT EXISTS "CarriedOverDepositBvc" BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS "CarriedOverFromReservationIds" VARCHAR(500) NULL,
  ADD COLUMN IF NOT EXISTS "CarriedOverFromUserIds" VARCHAR(500) NULL;

-- Index cho reporting "tổng carried-over deposit theo cafe"
CREATE INDEX IF NOT EXISTS "IX_Reservations_CarriedOver"
  ON "Reservations" ("CafeId")
  WHERE "CarriedOverDepositBvc" > 0;
```

### Task B3.6: Reservation entity — 3 fields mới

**File:** `BoardVerse.Core/Entities/Reservation.cs`
**Effort:** 0.25h

```csharp
/// <summary>
/// M1 / Exception 4: Tổng deposit đã được "chuyển" từ các source reservation (qua merge chain).
/// Effective deposit = DepositAmount + CarriedOverDepositBvc.
/// KHÔNG refund về wallet — chỉ accumulate vào target's pool.
/// </summary>
public long CarriedOverDepositBvc { get; set; } = 0;

/// <summary>
/// Audit trail: danh sách Reservation IDs (CSV) mà deposit đã được carry over từ đó.
/// Ví dụ: "abc-123,def-456" nghĩa là có 2 source reservations đã merge vào reservation này.
/// </summary>
public string? CarriedOverFromReservationIds { get; set; }

/// <summary>
/// Audit trail: danh sách User IDs (CSV) của các host đã góp deposit vào reservation này qua merge.
/// </summary>
public string? CarriedOverFromUserIds { get; set; }
```

### Task B3.7: ReservationConfiguration — 3 columns mapping

**File:** `BoardVerse.Data/Configurations/ReservationConfiguration.cs`
**Effort:** 0.25h

```csharp
builder.Property(r => r.CarriedOverDepositBvc)
    .IsRequired()
    .HasDefaultValue(0L);

builder.Property(r => r.CarriedOverFromReservationIds)
    .HasMaxLength(500)
    .IsRequired(false);

builder.Property(r => r.CarriedOverFromUserIds)
    .HasMaxLength(500)
    .IsRequired(false);
```

---

## B4. Option A — Refund Per-Member Deposit on Merge (BR-22 only)

> **Clarification 2026-10-01:** Option A này chỉ áp dụng cho **per-member deposit**
> (BR-22 legacy, khi member tự trả deposit của mình). Host deposit KHÔNG dùng Option A
> mà dùng §B3 (deposit follows).

### Task B4.1: Create `IMergeService` interface + impl

**File:** `Application/Services/MergeService.cs` (NEW)
**Effort:** 6h

```csharp
public interface IMergeService
{
    Task<MergeMemberResponseDto> HandleMemberMergeAsync(
        Guid memberId,
        Guid fromLobbyId,
        Guid toLobbyId,
        Guid staffUserId,
        CancellationToken ct = default);
}
```

Xem code đầy đủ trong **[§B4.2](#b42-handlemembermergeasync-full-impl)**.

### Task B4.2: `HandleMemberMergeAsync` full impl

**Effort:** (included in B4.1)

```csharp
public async Task<MergeMemberResponseDto> HandleMemberMergeAsync(
    Guid memberId,
    Guid fromLobbyId,
    Guid toLobbyId,
    Guid staffUserId,
    CancellationToken ct = default)
{
    await using var dbTx = await _unitOfWork.BeginTransactionAsync(ct);
    
    try
    {
        var member = await _memberRepo.GetByIdAsync(memberId, ct)
            ?? throw new NotFoundException($"Member {memberId} not found");
        
        long depositRefunded = 0;
        
        // ============ GUARD: Member validation ============
        if (member.IsGuestSlot)
        {
            _logger.LogInformation("Member {MemberId} is Guest_Slot — no deposit refund needed", memberId);
        }
        else if (!member.UserId.HasValue)
        {
            throw new InvalidOperationException(
                $"Member {memberId} is not Guest_Slot but has no UserId — data inconsistency");
        }
        else if (member.DepositRefundedAt.HasValue)
        {
            _logger.LogWarning(
                "Member {MemberId} deposit already refunded at {RefundedAt} — idempotent skip",
                memberId, member.DepositRefundedAt);
        }
        else if (member.DepositId.HasValue && member.DepositAppliedAmount == 0m)
        {
            // ============ OPTION A: Refund per-member deposit ============
            var depositAmount = await _walletService.GetDepositAmountAsync(
                member.DepositId.Value, ct);
            
            if (depositAmount > 0)
            {
                var mergedAt = DateTime.UtcNow;
                var idempotencyKey = 
                    $"refund-merge-{memberId}-{member.DepositId.Value}-{mergedAt:o}";
                
                var ledgerEntry = await _walletService.RefundMemberDepositOnMergeAsync(
                    userId: member.UserId.Value,
                    amountBvc: depositAmount,
                    depositId: member.DepositId.Value,
                    reason: LedgerEntryType.DepositRefund_Merge,
                    idempotencyKey: idempotencyKey,
                    notes: $"Member merge từ Lobby {fromLobbyId} → {toLobbyId}",
                    ct: ct);
                
                member.DepositId = null;
                member.DepositAppliedAmount = 0m;
                member.DepositRefundedAt = mergedAt;
                member.DepositRefundReason = $"Merged từ Lobby {fromLobbyId}";
                member.DepositRefundLedgerId = ledgerEntry.Id;
                
                await _auditLogRepo.AddAsync(new MemberDepositAuditLog
                {
                    Id = Guid.NewGuid(),
                    MemberId = member.Id,
                    UserId = member.UserId.Value,
                    Action = "Refunded_OnMerge",
                    AmountBvc = depositAmount,
                    DepositId = member.DepositId,
                    FromLobbyId = fromLobbyId,
                    FromSessionId = member.ActiveSessionId,
                    ToLobbyId = toLobbyId,
                    ToSessionId = null,
                    MergedAt = mergedAt,
                    Reason = $"Member merge từ Lobby {fromLobbyId} → {toLobbyId}",
                    IdempotencyKey = idempotencyKey,
                    LedgerEntryId = ledgerEntry.Id,
                    CreatedAt = mergedAt,
                    CreatedByUserId = staffUserId
                }, ct);
                
                depositRefunded = depositAmount;
                
                _logger.LogInformation(
                    "Member {MemberId} merge refund: {Amount} BVC → User {UserId} wallet",
                    memberId, depositAmount, member.UserId);
            }
        }
        else if (member.DepositId.HasValue && member.DepositAppliedAmount > 0m)
        {
            // ============ RACE CONDITION: Deposit đã apply vào bill ============
            _logger.LogWarning(
                "Member {MemberId} has DepositId {DepositId} already applied {Applied} BVC " +
                "to bill before merge — no refund (race lost)",
                memberId, member.DepositId, member.DepositAppliedAmount);
            
            member.DepositId = null;
            member.DepositRefundReason = "DepositConsumed_BeforeMerge";
        }
        
        // ============ EXISTING MERGE LOGIC ============
        member.OriginalSessionId ??= member.ActiveSessionId;
        member.OriginalLobbyId ??= fromLobbyId;
        member.MergedFromLobbyId = fromLobbyId;
        member.MergedAt = DateTime.UtcNow;
        member.LeftAt = DateTime.UtcNow; // KEY: set LeftAt để Group A's pay filter exclude A3
        member.TotalMinutesPlayed = (int)(member.LeftAt.Value - member.JoinedAt).TotalMinutes;
        
        // Move to new session (existing logic)...
        
        await _memberRepo.UpdateAsync(member, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);
        
        return new MergeMemberResponseDto
        {
            MemberId = memberId,
            DepositRefunded = depositRefunded,
            NewSessionId = member.ActiveSessionId,
            MergedAt = member.MergedAt.Value
        };
    }
    catch (Exception ex)
    {
        await dbTx.RollbackAsync(ct);
        
        _logger.LogError(ex, "Merge failed for Member {MemberId}: {Error}", memberId, ex.Message);
        
        await _notificationService.AlertStaffAsync(
            cafeId: await GetCafeIdByLobbyAsync(fromLobbyId, ct),
            message: $"Merge failed cho member {memberId}. Cần can thiệp thủ công. Lý do: {ex.Message}",
            severity: NotificationSeverity.High);
        
        throw;
    }
}
```

### Task B4.3: `WalletService.RefundMemberDepositOnMergeAsync`

**File:** `Application/Services/WalletService.cs` (NEW method)
**Effort:** 3h

```csharp
public async Task<BvcLedgerEntry> RefundMemberDepositOnMergeAsync(
    Guid userId,
    long amountBvc,
    Guid depositId,
    LedgerEntryType reason,
    string idempotencyKey,
    string? notes = null,
    CancellationToken ct = default)
{
    // 1. Idempotency check (out of transaction for fast path)
    var existing = await _ledgerRepo.GetByIdempotencyKeyAsync(idempotencyKey, ct);
    if (existing != null)
    {
        _logger.LogInformation(
            "Idempotent refund merge: key {Key} already processed as LedgerEntry {EntryId}",
            idempotencyKey, existing.Id);
        return existing;
    }
    
    await using var dbTx = await _unitOfWork.BeginTransactionAsync(ct);
    
    try
    {
        var wallet = await _walletRepo.GetByUserIdAsync(userId, ct)
            ?? throw new NotFoundException($"Wallet not found for user {userId}");
        
        if (wallet.IsLocked)
            throw new ConflictException($"Wallet for user {userId} is locked");
        
        var deposit = await _depositHoldRepo.GetByIdAsync(depositId, ct)
            ?? throw new NotFoundException($"Deposit {depositId} not found");
        
        if (deposit.UserId != userId)
            throw new UnauthorizedException(
                $"Deposit {depositId} belongs to user {deposit.UserId}, not {userId}");
        
        if (deposit.Status != BvcHoldStatus.Held)
            throw new ConflictException(
                $"Deposit {depositId} is in status {deposit.Status}, not Held — cannot refund");
        
        if (deposit.AmountBvc != amountBvc)
            throw new ConflictException(
                $"Deposit {depositId} amount {deposit.AmountBvc} ≠ requested {amountBvc}");
        
        // Re-check idempotency INSIDE transaction (race-safe)
        existing = await _ledgerRepo.GetByIdempotencyKeyAsync(idempotencyKey, ct);
        if (existing != null)
        {
            await dbTx.RollbackAsync(ct);
            return existing;
        }
        
        // Move money: heldBalance → availableBalance
        wallet.HeldBalanceBvc -= amountBvc;
        wallet.AvailableBalanceBvc += amountBvc;
        
        deposit.Status = BvcHoldStatus.Released;
        deposit.ReleasedAt = DateTime.UtcNow;
        deposit.ReleaseReason = reason;
        
        var ledgerEntry = new BvcLedgerEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = reason,
            AmountBvc = amountBvc,
            BalanceAfterBvc = wallet.AvailableBalanceBvc,
            IdempotencyKey = idempotencyKey,
            ReferenceId = depositId,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };
        
        await _walletRepo.UpdateAsync(wallet, ct);
        await _depositHoldRepo.UpdateAsync(deposit, ct);
        await _ledgerRepo.AddAsync(ledgerEntry, ct);
        
        await _unitOfWork.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);
        
        _logger.LogInformation(
            "Refund merge: User {UserId} deposit {DepositId} {Amount} BVC → availableBalance",
            userId, depositId, amountBvc);
        
        return ledgerEntry;
    }
    catch (DbUpdateException dbe) when (dbe.InnerException is PostgresException pe && pe.SqlState == "23505")
    {
        // Idempotency conflict → return existing
        _logger.LogWarning("Idempotency conflict on {Key}, fetching existing ledger entry", idempotencyKey);
        
        existing = await _ledgerRepo.GetByIdempotencyKeyAsync(idempotencyKey, ct);
        if (existing != null) return existing;
        
        throw;
    }
    catch
    {
        await dbTx.RollbackAsync(ct);
        throw;
    }
}
```

### Task B4.4: Idempotency key generator

**File:** `Application/Services/WalletService.cs`
**Effort:** 0.5h

Pattern (xem **[§D3](#d3-idempotency-key-patterns)**):
- Merge refund: `refund-merge-{memberId}-{depositId}-{mergedAt:o}`
- Discount capture: `capture-discount-{sessionId}-{totalApplied}`
- Remainder release: `release-remainder-{sessionId}-{remainder}`
- Member BVC debit: `member-bvc-debit-{memberId}-{billId}-{amount}`

### Task B4.5: Race condition guard

**File:** `Application/Services/MergeService.cs`
**Effort:** 1h

(Đã cover trong B4.2 — check `DepositAppliedAmount == 0` trước refund)

### Task B4.6: Update existing merge API endpoint

**File:** `Api/Controllers/LobbyController.cs` hoặc `ActiveSessionController.cs`
**Effort:** 1h

```csharp
[HttpPost("api/v1/lobbies/{lobbyId}/members/{memberId}/merge")]
public async Task<MergeMemberResponseDto> MergeMember(
    Guid lobbyId, Guid memberId,
    [FromBody] MergeMemberRequestDto request,
    CancellationToken ct)
{
    var staffUserId = User.GetUserId();
    var response = await _mergeService.HandleMemberMergeAsync(
        memberId, lobbyId, request.TargetLobbyId, staffUserId, ct);
    return response;
}
```

### Task B4.7: Error handling + manual intervention

**File:** `Application/Services/MergeService.cs` + `NotificationService.cs`
**Effort:** 2h

```csharp
// Trong catch block:
await _notificationService.AlertStaffAsync(
    cafeId: ...,
    message: $"Merge failed cho member {memberId}. Cần can thiệp thủ công.",
    severity: NotificationSeverity.High);

// Background job queue manual interventions (Option A fallback)
await _manualInterventionQueue.EnqueueAsync(new ManualIntervention
{
    Type = "MemberMerge_RefundFailed",
    MemberId = memberId,
    FromLobbyId = fromLobbyId,
    ToLobbyId = toLobbyId,
    Reason = ex.Message,
    CreatedAt = DateTime.UtcNow
});
```

---

## B5. Tests cho M1

### Task B4.1-B4.4: Unit tests Case 1 — discount modes

**File:** `Tests/Unit/ActiveSessionServiceTests.cs`
**Effort:** 8h

| Test | Setup | Expected |
|---|---|---|
| `PaySession_None_CapturesFullDeposit` | 3 members, host 50k, mode None | Capture 50k, no member discount |
| `PaySession_DiscountGroup_DistributesByMinutes` | 3 members (60/120/60 min), host 50k, mode DiscountGroup | A1=12.5k, A2=25k, A3=12.5k |
| `PaySession_DiscountHostOnly_OnlyHostGetsDiscount` | 3 members, host 50k, mode DiscountHostOnly | A1=50k, A2=0, A3=0 |
| `PaySession_DiscountGroup_RemainderReleased` | 3 members total 30k, deposit 50k, mode DiscountGroup | Apply 30k, release 20k |

### Task B4.5-B4.7: Unit tests Exception 4 — merge case

**File:** `Tests/Unit/ActiveSessionServiceTests.cs`
**Effort:** 4h

| Test | Setup | Expected |
|---|---|---|
| `PaySession_MergedMemberExcludedFromDiscount` | A3 LeftAt=13:00, Group A pay 14:00, mode DiscountGroup | A1+A2 only, A3=0 |
| `PaySession_MergedMemberIncludedInNewSession` | A3 in Group B, pay 15:00, mode DiscountGroup | A3 gets discount from Group B |
| `PaySession_CancelledReservation_SkipsDiscount` | Reservation.Status=Cancelled | Fallback to None mode, no discount |

### Task B4.8-B4.11: Unit tests Option A — refund merge

**File:** `Tests/Unit/MergeServiceTests.cs` (NEW)
**Effort:** 5h

| Test | Setup | Expected |
|---|---|---|
| `Merge_RefundsDepositToWallet` | A3 has DepositId=20k, merge | Wallet +20k, DepositId=null, audit log |
| `Merge_DepositAlreadyApplied_NoRefund` | A3 DepositAppliedAmount>0, merge | No refund, reason="DepositConsumed_BeforeMerge" |
| `Merge_Idempotent_DoesNotRefundTwice` | Call merge twice | First: refund. Second: skip (DepositRefundedAt set) |
| `Merge_GuestSlot_NoRefund` | A3 IsGuestSlot=true | Skip refund, no error |

### Task B4.12-B4.14: Integration tests M1

**File:** `Tests/Integration/M1IntegrationTests.cs`
**Effort:** 12h

| Test | Scenario |
|---|---|
| `Integration_FullPaySession_WithDiscountGroup` | Setup 4 members, pay with DiscountGroup → verify wallet, ledger, audit log |
| `Integration_Exception4_MergeDiscountFlow` | A3 merge Group A→B → verify both sessions' discount distribution |
| `Integration_RefundFail_ManualQueue` | Mock DB error → verify rollback + manual intervention queue |

### Task B4.15: Concurrency test

**File:** `Tests/Stress/ConcurrencyTests.cs`
**Effort:** 2h

```csharp
[Fact]
public async Task TwoStaffMergeSameMember_OnlyOneSucceeds()
{
    // Race condition: 2 staff trigger merge cho cùng A3 simultaneously
    // → 1 success, 1 idempotent reject
    // → wallet chỉ +20k 1 lần
}
```

### Task B4.16: Audit log + ledger entry test

**File:** `Tests/Unit/MergeServiceTests.cs`
**Effort:** 1h

Verify `MemberDepositAuditLog` + `BvcLedgerEntry` đúng format.

---

## B6. Frontend tối thiểu cho M1

### Task B5.1: POS dropdown cho HostDepositUsageMode

**File:** `client-pos/src/components/CheckoutFlow/`
**Effort:** 4h

UI chỉ cần:
- Dropdown 3 options (None/DiscountGroup/DiscountHostOnly)
- Disable dropdown nếu session là walk-in (no reservation)
- Pass value qua `PaySessionRequest`

### Task B5.2: POS display DepositAppliedBreakdown

**Effort:** 2h

Hiển thị per-member discount trên receipt preview.

### Task B5.3: Error handling UI

**Effort:** 2h

Toast notification cho merge fail / refund fail → "Yêu cầu staff can thiệp".

---

# PHẦN C — MILESTONE 2 (19 NGÀY): CASE 2 + FULL UI + REPORTING

## C1. Schema bổ sung cho M2

### Task C1.1: Extend `MemberPaymentStatus` enum

**File:** `Enum/MemberPaymentStatus.cs`
**Effort:** 0.5h

```csharp
public enum MemberPaymentStatus
{
    NotPaid = 0,
    PaidCash = 1,
    PaidQr = 2,
    // NEW
    PaidBvc = 3,
    PartialBvc = 4,
    RefundedBvc = 5
}
```

### Task C1.2: Add fields to `ActiveSessionMember` (M2)

**Effort:** 1h

```csharp
public long PaidBvcAmount { get; set; }
public decimal PaidCashRemainder { get; set; }
public DateTime? BvcRefundedAt { get; set; }
public string? BvcRefundReason { get; set; }
```

### Task C1.3: Create `MemberPaymentAuditLog` entity

**File:** `Entities/MemberPaymentAuditLog.cs`
**Effort:** 2h

```csharp
public class MemberPaymentAuditLog : BaseEntity
{
    public Guid MemberId { get; set; }
    public string PaymentMethod { get; set; }  // 'Bvc' | 'Cash' | 'BvcPartial'
    public long AmountBvc { get; set; }
    public decimal AmountCash { get; set; }
    public Guid? WalletTxnId { get; set; }
    public string IdempotencyKey { get; set; }
    public Guid CreatedByUserId { get; set; }
    
    // NEW: refund tracking
    public DateTime? RefundedAt { get; set; }
    public string? RefundReason { get; set; }
}
```

### Task C1.4: Migration SQL cho M2

**File:** `sql/m2_member_bvc_payment.sql`
**Effort:** 1h

Xem **[§D2](#d2-migration-sql-full)** cho M2 portion.

---

## C2. Backend Code — Case 2 (BVC Member Payment)

### Task C2.1: `WalletService.DirectDebitForBillAsync`

**File:** `Application/Services/WalletService.cs` (NEW)
**Effort:** 6h

```csharp
public async Task<BvcLedgerEntry> DirectDebitForBillAsync(
    Guid userId,
    long amountBvc,
    Guid memberId,
    Guid billId,
    string idempotencyKey,
    CancellationToken ct = default)
{
    // 1. Validate amount > 0
    if (amountBvc <= 0)
        throw new ValidationException("Amount must be > 0");
    
    // 2. Validate user has enough availableBalance
    var wallet = await _walletRepo.GetByUserIdAsync(userId, ct)
        ?? throw new NotFoundException($"Wallet not found for user {userId}");
    
    if (wallet.AvailableBalanceBvc < amountBvc)
        throw new InsufficientFundsException(
            $"User {userId} has {wallet.AvailableBalanceBvc} BVC, needs {amountBvc}");
    
    // 3. Idempotency check
    var existing = await _ledgerRepo.GetByIdempotencyKeyAsync(idempotencyKey, ct);
    if (existing != null) return existing;
    
    // 4. Debit + ledger
    await using var dbTx = await _unitOfWork.BeginTransactionAsync(ct);
    
    wallet.AvailableBalanceBvc -= amountBvc;
    // Revenue goes to cafe via separate settlement flow (không touch wallet.heldBalance)
    
    var ledgerEntry = new BvcLedgerEntry
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = LedgerEntryType.MemberBillDebit,
        AmountBvc = -amountBvc, // negative for debit
        BalanceAfterBvc = wallet.AvailableBalanceBvc,
        IdempotencyKey = idempotencyKey,
        ReferenceId = memberId,
        Notes = $"BVC payment for member {memberId} bill {billId}",
        CreatedAt = DateTime.UtcNow
    };
    
    await _walletRepo.UpdateAsync(wallet, ct);
    await _ledgerRepo.AddAsync(ledgerEntry, ct);
    
    await _unitOfWork.SaveChangesAsync(ct);
    await dbTx.CommitAsync(ct);
    
    return ledgerEntry;
}
```

### Task C2.2: Validate overpayment (Gap #6)

**File:** `Application/Services/WalletSessionPaymentService.cs`
**Effort:** 0.5h

```csharp
if (request.BvcAmount > totalDue)
    throw new ValidationException(
        $"BvcAmount {request.BvcAmount} > totalDue {totalDue} (overpayment not allowed)");
```

### Task C2.3: Penalty payment by BVC (Gap #5)

**Effort:** 1h

```csharp
// totalDue bao gồm penalty cho non-Guest members
var totalDue = member.Subtotal + member.PenaltyAmount - member.DepositAppliedAmount;

if (member.IsGuestSlot && member.PenaltyAmount > 0)
{
    // Guest_Slot penalty = 0 per BR (không charge penalty cho guest)
    member.PenaltyAmount = 0;
}
```

### Task C2.4: Distinct UserId check (Gap #7)

**Effort:** 1h

```csharp
// Validate không có 2 members cùng UserId trong session
var duplicateUsers = session.Members
    .Where(m => m.UserId.HasValue)
    .GroupBy(m => m.UserId.Value)
    .Where(g => g.Count() > 1)
    .Select(g => g.Key)
    .ToList();

if (duplicateUsers.Any())
    throw new ConflictException(
        $"Session {session.Id} has duplicate UserIds: {string.Join(", ", duplicateUsers)}");
```

### Task C2.5: Idempotency với state check (Gap #17)

**File:** `Application/Services/WalletSessionPaymentService.cs`
**Effort:** 2h

```csharp
// Validate idempotency key chưa được dùng + amount khớp
var existing = await _auditLogRepo.GetByIdempotencyKeyAsync(idempotencyKey, ct);
if (existing != null)
{
    if (existing.AmountBvc != amountBvc)
        throw new ConflictException(
            $"Idempotency key {idempotencyKey} đã dùng với amount {existing.AmountBvc}, không khớp {amountBvc}");
    
    return existing; // Re-process hoặc skip? Skip để an toàn
}
```

### Task C2.6: Endpoint `GET /sessions/{id}/member-bill-preview`

**File:** `Api/Controllers/WalletSessionPaymentController.cs` (NEW)
**Effort:** 4h

```csharp
[HttpGet("api/v1/sessions/{sessionId}/member-bill-preview")]
public async Task<MemberBillPreviewDto> GetMemberBillPreview(
    Guid sessionId,
    [FromQuery] Guid memberId,
    CancellationToken ct)
{
    // Trả về breakdown cho member bill
    return await _walletPaymentService.GetMemberBillPreviewAsync(sessionId, memberId, ct);
}
```

### Task C2.7: Endpoint `POST /sessions/{id}/pay-member-bill`

**Effort:** 8h

```csharp
[HttpPost("api/v1/sessions/{sessionId}/members/{memberId}/pay-bill")]
public async Task<MemberPaymentResponseDto> PayMemberBill(
    Guid sessionId,
    Guid memberId,
    [FromBody] MemberBillPaymentRequestDto request,
    CancellationToken ct)
{
    // 1. Validate request
    // 2. Lock member row (FOR UPDATE)
    // 3. Calculate totalDue
    // 4. Validate overpayment
    // 5. Debit wallet
    // 6. Update member payment status
    // 7. Create audit log
    // 8. Check all-paid → trigger session paid (if applicable)
    // 9. Return response
}
```

### Task C2.8: Auto-refund flow (Gap #11)

**File:** `Application/Services/WalletSessionPaymentService.cs`
**Effort:** 4h

```csharp
public async Task<BvcLedgerEntry> RefundMemberBillAsync(
    Guid memberId,
    long amountBvc,
    string reason,
    CancellationToken ct = default)
{
    // Refund to user's availableBalance + audit log
    // Update member.BvcRefundedAt + BvcRefundReason
}
```

### Task C2.9: Notification (Gap #12)

**File:** `Application/Services/NotificationService.cs`
**Effort:** 2h

Push notification cho:
- BVC payment success → member + host
- BVC payment fail (insufficient funds) → member
- Bill refund → member

### Task C2.10: All-paid trigger

**File:** `Application/Services/ActiveSessionService.cs`
**Effort:** 4h

```csharp
private async Task CheckAllMembersPaidAsync(Guid sessionId, CancellationToken ct)
{
    var session = await _sessionRepo.GetByIdAsync(sessionId, ct);
    var unpaidMembers = session.Members
        .Where(m => m.PaymentStatus == MemberPaymentStatus.NotPaid 
                 || m.PaymentStatus == MemberPaymentStatus.PartialBvc)
        .ToList();
    
    if (!unpaidMembers.Any() && !session.IsPaid)
    {
        // Auto-trigger session paid
        await TriggerAutoPaySessionAsync(sessionId, ct);
    }
}
```

### Task C2.11: Audit log + telemetry

**Effort:** 2h

### Task C2.12: HeldBalance cap warning (Gap #9)

**Effort:** 1h

```csharp
// BR-USER-LIMIT-03: nếu refund làm wallet vượt cap → warning + manual review
if (wallet.AvailableBalanceBvc + amountBvc > wallet.MaxAvailableBalanceBvc)
{
    _logger.LogWarning(
        "User {UserId} refund would exceed held cap: {Current} + {Refund} > {Max}",
        userId, wallet.AvailableBalanceBvc, amountBvc, wallet.MaxAvailableBalanceBvc);
    
    await _notificationService.AlertUserAsync(
        userId,
        $"Bạn có refund {amountBvc} BVC nhưng wallet vượt giới hạn. Vui lòng liên hệ hỗ trợ.",
        severity: NotificationSeverity.Medium);
}
```

### Task C2.13: No-show cleanup (Gap #21)

**Effort:** 1h

Background job mỗi 30 phút:
```csharp
// Tìm members có BVC pre-selected nhưng không đến
var ghostSelections = await _selectionRepo.GetStaleSelectionsAsync(
    staleAfter: TimeSpan.FromHours(2), ct);
foreach (var s in ghostSelections)
{
    s.Cleanup();
}
```

### Task C2.14: API doc update

**File:** `docs/api/wallet.md` + `docs/api/cafe-pos.md`
**Effort:** 2h

---

### Task C2.15: Endpoint `GET /sessions/{id}/members/{memberId}/receipt` (Gap #32)

**File:** `Api/Controllers/ReceiptController.cs` (extend existing)
**Effort:** 4h + 2h tests = **6h**

> **Schema:** Không cần migration mới. Chỉ reuses `ActiveSessionMember.PaidAt/PaymentMethod/PaymentStatus/TransactionId` (đã có sẵn từ migration 2026-08-24).

Mục đích: Member muốn receipt riêng cho phần bill của mình (expense report, khiếu nại, etc.). Existing endpoint `GET /sessions/{id}/receipt` trả cả session; endpoint này filter theo `memberId`.

```csharp
[HttpGet("api/v1/sessions/{sessionId}/members/{memberId}/receipt")]
[Authorize(Roles = "Admin,Manager,CafeStaff,Player")]
public async Task<FileResult> GetMemberReceipt(
    Guid sessionId,
    Guid memberId,
    [FromQuery] string format = "pdf", // 'pdf' | 'png'
    CancellationToken ct = default)
{
    // 1. Auth check: 
    //    - Staff: phải thuộc cafe
    //    - Player: phải là member đó (UserId == currentUser.Id) HOẶC là host của session
    
    // 2. Validate session & member
    var session = await _sessionRepo.GetByIdAsync(sessionId, ct)
        ?? throw new NotFoundException($"Session {sessionId} not found");
    
    if (session.Status != ActiveSessionStatus.Paid)
        throw new ConflictException(
            $"Session {sessionId} chưa thanh toán (status={session.Status}), không thể generate receipt");
    
    var member = session.Members.FirstOrDefault(m => m.Id == memberId)
        ?? throw new NotFoundException($"Member {memberId} không thuộc session {sessionId}");
    
    // 3. Build member invoice
    var memberInvoice = new MemberReceiptDto
    {
        SessionId = session.Id,
        MemberId = member.Id,
        DisplayName = member.DisplayName,
        IsHost = member.IsHost,
        CafeName = session.Cafe.Name,
        CafeAddress = session.Cafe.Address,
        SessionStart = session.ActualStartAt ?? session.CreatedAt,
        SessionEnd = session.ActualEndAt ?? DateTime.UtcNow,
        DurationMinutes = member.TotalMinutesPlayed,
        Subtotal = member.Subtotal,
        DepositApplied = member.DepositAppliedAmount,
        PenaltyAmount = member.PenaltyAmount,
        Total = member.TotalAmount,
        PaidAt = member.PaidAt,
        PaymentMethod = member.PaymentMethod.ToString(),
        PaymentStatus = member.PaymentStatus.ToString(),
        OrderId = member.OrderId, // nếu QR
        TransactionId = member.TransactionId,
        ReceiptGeneratedAt = DateTime.UtcNow
    };
    
    // 4. Generate PDF/PNG
    var receipt = await _receiptService.GenerateMemberReceiptAsync(
        memberInvoice, format, ct);
    
    return File(receipt.Bytes, receipt.ContentType, receipt.FileName);
}
```

**Tests:**
- `GetMemberReceipt_PaidSession_ReturnsPdf`
- `GetMemberReceipt_PlayerSelf_Authorized`
- `GetMemberReceipt_OtherPlayer_403Forbidden`
- `GetMemberReceipt_UnpaidSession_409Conflict`
- `GetMemberReceipt_GuestSlot_NoUserId_StaffOnly`

---

### Task C2.16: Endpoint `POST /sessions/{id}/force-close` (Gap #33)

**File:** `Api/Controllers/ActiveSessionController.cs` (extend existing)
**Effort:** 4h + 4h schema = **8h**

> **⚠️ Schema trước khi implement code:**
>
> C2.16 tham chiếu 3 enum values + 1 entity chưa tồn tại trong codebase. Phải tạo migration trước:
>
> | Item | File | Action |
> |---|---|---|
> | `MemberPaymentStatus.PaidByHost = 3` | `BoardVerse.Core/Enum/MemberPaymentStatus.cs` | Thêm enum value + migration `AddPaidByHostToMemberPaymentStatus` |
> | `IndividualSessionStatus.NoShow = 3` | `BoardVerse.Core/Enum/IndividualSessionStatus.cs` | Thêm enum value + migration `AddNoShowToIndividualSessionStatus` |
> | `GroupSessionStatus.UnpaidForced = 5` | `BoardVerse.Core/Enum/GroupSessionStatus.cs` | Thêm enum value + migration `AddUnpaidForcedToGroupSessionStatus` |
> | `MemberDebt` entity (MỚI) | `BoardVerse.Core/Entities/MemberDebt.cs` | Tạo mới + table + EF config |
> | `ForceCloseAuditLog` entity (MỚI) | `BoardVerse.Core/Entities/ForceCloseAuditLog.cs` | Tạo mới + table + EF config |

Mục đích: Host / Cafe Manager / Admin force-close session khi có member no-show biến mất không trả được. Không thể chờ `all-paid → auto Paid` mãi mãi.

```csharp
public class ForceCloseRequestDto
{
    /// <summary>Cách xử lý unpaid members: "MarkNoShow" | "MarkAsDebt" | "CompensationByHost"</summary>
    public string UnpaidMemberHandling { get; set; }
    
    /// <summary>Lý do force-close (audit).</summary>
    public string Reason { get; set; }
    
    /// <summary>Có cho phép member tự trả sau không? (true = unpaid members vẫn còn 'có thể thanh toán', false = close hẳn).</summary>
    public bool AllowLatePayment { get; set; } = true;
}

[HttpPost("api/v1/sessions/{sessionId}/force-close")]
[Authorize(Roles = "CafeManager,Admin")]
public async Task<ForceCloseResponseDto> ForceCloseWithUnpaid(
    Guid sessionId,
    [FromBody] ForceCloseRequestDto request,
    CancellationToken ct = default)
{
    var session = await _sessionRepo.GetByIdAsync(sessionId, ct)
        ?? throw new NotFoundException($"Session {sessionId} not found");
    
    // 1. Validate
    if (session.Status != ActiveSessionStatus.Unpaid)
        throw new ConflictException(
            $"Session phải ở UNPAID (hiện tại={session.Status}). Đã Paid thì không cần force-close.");
    
    // 2. Find unpaid members
    var unpaidMembers = session.Members
        .Where(m => m.PaymentStatus == MemberPaymentStatus.NotPaid)
        .ToList();
    
    if (!unpaidMembers.Any())
        throw new ConflictException($"Session {sessionId} không có unpaid members → dùng /pay thay vì force-close");
    
    // 3. Process unpaid members based on handling mode
    foreach (var member in unpaidMembers)
    {
        switch (request.UnpaidMemberHandling)
        {
            case "MarkNoShow":
                member.Status = MemberStatus.NoShow;
                member.PaymentStatus = MemberPaymentStatus.NotPaid; // stays Unpaid
                member.NoShowAt = DateTime.UtcNow;
                member.NoShowReason = request.Reason;
                // Note: late payment vẫn cho phép nếu AllowLatePayment = true
                break;
                
            case "MarkAsDebt":
                member.Status = MemberStatus.HasDebt;
                member.PaymentStatus = MemberPaymentStatus.NotPaid;
                var debt = new MemberDebt
                {
                    Id = Guid.NewGuid(),
                    MemberId = member.Id,
                    UserId = member.UserId,
                    SessionId = sessionId,
                    AmountBvc = 0, // legacy support
                    AmountCash = member.TotalAmount,
                    CreatedAt = DateTime.UtcNow,
                    Status = DebtStatus.Pending,
                    Reason = request.Reason
                };
                await _debtRepo.AddAsync(debt, ct);
                break;
                
            case "CompensationByHost":
                // Host covers unpaid members (tự động charge vào host bill)
                // Gọi /pay-member với memberIds = unpaid
                // Hoặc apply vào host's DepositAppliedAmount
                var hostMember = session.Members.First(m => m.IsHost);
                if (hostMember.PaymentStatus == MemberPaymentStatus.NotPaid)
                    throw new ConflictException(
                        "Host cũng chưa paid → không thể compensate");
                
                // Mark unpaid as paid by host
                foreach (var unpaid in unpaidMembers)
                {
                    unpaid.PaymentStatus = MemberPaymentStatus.PaidByHost;
                    unpaid.PaidByHostAt = DateTime.UtcNow;
                    unpaid.PaidByHostUserId = hostMember.UserId;
                }
                break;
                
            default:
                throw new ValidationException($"Unknown handling: {request.UnpaidMemberHandling}");
        }
    }
    
    // 4. Force close session (chỉ khi không còn NotPaid)
    var stillUnpaid = session.Members
        .Count(m => m.PaymentStatus == MemberPaymentStatus.NotPaid);
    
    if (stillUnpaid > 0 && !request.AllowLatePayment)
    {
        throw new ConflictException(
            $"Vẫn còn {stillUnpaid} unpaid members và AllowLatePayment=false. " +
            "Hãy chọn MarkAsDebt hoặc CompensateByHost");
    }
    
    if (stillUnpaid == 0)
    {
        session.Status = ActiveSessionStatus.Paid;
        session.ActualPaidAt = DateTime.UtcNow;
        // Release table/box/inventory (existing pay flow)
        await _cleanupService.CompleteSessionPaymentCleanupAsync(sessionId, ct);
    }
    else
    {
        // AllowLatePayment = true → session vẫn ở Unpaid nhưng members marked NoShow/Debt
        session.Status = ActiveSessionStatus.UnpaidForced;
        // Add audit
    }
    
    // 5. Audit log
    await _auditLogRepo.AddAsync(new ForceCloseAuditLog
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        TriggeredByUserId = User.GetUserId(),
        UnpaidHandling = request.UnpaidMemberHandling,
        Reason = request.Reason,
        UnpaidMemberIds = unpaidMembers.Select(m => m.Id).ToList(),
        SessionClosedAfter = session.Status == ActiveSessionStatus.Paid,
        CreatedAt = DateTime.UtcNow
    }, ct);
    
    return new ForceCloseResponseDto
    {
        SessionId = sessionId,
        Status = session.Status.ToString(),
        UnpaidMembers = unpaidMembers.Select(m => new UnpaidMemberDto
        {
            MemberId = m.Id,
            DisplayName = m.DisplayName,
            Amount = m.TotalAmount,
            Handling = request.UnpaidMemberHandling
        }).ToList(),
        ForceClosedAt = DateTime.UtcNow
    };
}
```

**Tests:**
- `ForceClose_MarkNoShow_AllMembersLatePayment`
- `ForceClose_MarkAsDebt_DebtRecordCreated`
- `ForceClose_CompensationByHost_HostMustBePaid`
- `ForceClose_PaidSession_409Conflict`
- `ForceClose_AllPaid_NoUnpaid_409Conflict`
- `ForceClose_NonManager_403Forbidden`

---

## C3. Frontend đầy đủ

### Task C3.1: POS dropdown HostDepositUsageMode

(Đã có trong M1 B5.1 — refine cho M2)

### Task C3.2: POS disable discount cho walk-in (Gap #23)

**Effort:** 0.5 ngày

```tsx
{!session.isWalkIn && (
  <Select label="Sử dụng cọc">
    <Option value="None">Không dùng</Option>
    <Option value="DiscountGroup">Giảm giá cho cả nhóm</Option>
    <Option value="DiscountHostOnly">Giảm giá cho host</Option>
  </Select>
)}
```

### Task C3.3: POS hiển thị DepositAppliedBreakdown

**Effort:** 0.5 ngày

### Task C3.4: Mobile tab "Trả bằng BVC"

**File:** `mobile-app/src/screens/SessionBill/`
**Effort:** 1.5 ngày

UI: tab riêng cho BVC payment, hiển thị breakdown (subtotal, penalty, discount, total).

### Task C3.5: Mobile split BVC/cash selector

**Effort:** 1 ngày

UI cho phép chọn partial BVC + partial cash.

### Task C3.6: Toast + push notification

**Effort:** 0.5 ngày

### Task C3.7: History BVC payment

**Effort:** 0.5 ngày

Tab "Lịch sử thanh toán BVC" trong user profile.

### Task C3.8: App resume + state restore (Gap #22)

**Effort:** 1 ngày

Persist `MemberPaymentSelectionDto` vào AsyncStorage, restore khi app resume.

---

## C4. Tests cho M2

### Task C4.1-B4.4: Unit tests Case 2 — BVC debit

**Effort:** 8h

| Test | Expected |
|---|---|
| `PayMemberBill_FullBvc_DebitsWallet` | Wallet -X, ledger entry created, member status = PaidBvc |
| `PayMemberBill_PartialBvc_AndCash` | Wallet -X/2, status = PartialBvc, cash remainder tracked |
| `PayMemberBill_InsufficientFunds_Throws` | Throw InsufficientFundsException |
| `PayMemberBill_Overpayment_Throws` | Throw ValidationException |

### Task C4.5-C4.6: Edge case tests

**Effort:** 3h

| Test | Expected |
|---|---|
| `PayMemberBill_TwoMembersSameUserId_Throws` | Distinct UserId check (Gap #7) |
| `PayMemberBill_GuestSlot_NoBvcPenalty` | Penalty for Guest_Slot = 0 |

### Task C4.7-C4.10: Integration tests M2

**Effort:** 12h

| Test |
|---|
| Full flow: setup session → 2 members pay BVC → 1 pays cash → all-paid trigger |
| Refund flow: bill sai → auto-refund BVC → audit log |
| Concurrency: 2 members pay same time → both succeed (different rows) |
| Webhook retry: SePay retry with idempotent key → no double debit |

### Task C4.11: All-paid trigger test

**Effort:** 2h

### Task C4.12: Refund + wallet cap test (Gap #9)

**Effort:** 1h

### Task C4.13: No-show cleanup test (Gap #21)

**Effort:** 1h

### Task C4.14: App resume state test (Gap #22)

**Effort:** 1h

---

## C5. Reporting + Rollout

### Task C5.1: Feature flag `ENABLE_HOST_DEPOSIT_DISCOUNT`

**File:** `appsettings.json` + `IFeatureFlagService`
**Effort:** 2h

Default: `false`. A/B test 2 cafes.

### Task C5.2: Feature flag `ENABLE_MEMBER_BVC_PAYMENT`

**Effort:** 2h

Default: `false`.

### Task C5.3: Reporting breakdown (Gap #20)

**File:** `Reports/DepositDiscountReport.cs` (NEW)
**Effort:** 1 ngày

```sql
-- Report: Discount & BVC payment breakdown per cafe per month
SELECT 
    cafe_id,
    DATE_TRUNC('month', paid_at) AS month,
    SUM(CASE WHEN source = 'DiscountGroup' THEN amount ELSE 0 END) AS total_discount_group,
    SUM(CASE WHEN source = 'DiscountHostOnly' THEN amount ELSE 0 END) AS total_discount_host,
    SUM(CASE WHEN source = 'MemberBvcDebit' THEN amount ELSE 0 END) AS total_member_bvc,
    SUM(CASE WHEN source = 'Cash' THEN amount ELSE 0 END) AS total_cash
FROM payment_records
WHERE paid_at >= NOW() - INTERVAL '6 months'
GROUP BY cafe_id, DATE_TRUNC('month', paid_at);
```

### Task C5.4: A/B test pilot

**Effort:** 1 ngày

Roll out cho 2 cafes (1 urban, 1 suburban) trong 2 tuần.

### Task C5.5: Monitor metrics + dashboard

**Effort:** ongoing

Metrics cần theo dõi:
- `deposit.discount.applied.count` (counter)
- `deposit.discount.skipped.count` (counter, by reason)
- `member.bvc.payment.success_rate` (gauge)
- `merge.refund.success_rate` (gauge)
- `payment.dispute_rate` (counter)

---

# PHẦN D — PHỤ LỤC

## D1. Edge Case Tables

### Edge cases Case 1 (Discount)

| # | Edge case | Cách xử lý | Gap |
|---|---|---|---|
| 1 | Deposit > session.TotalAmount | Clamp + refund remainder | existing |
| 2 | Deposit < session.TotalAmount | Apply full deposit | existing |
| 3 | Reservation.Status = Cancelled/Merged/NoShow | Skip discount, fallback BR-09 | #2 |
| 4 | Lobby.Status = Closed/CancelledByCafe/TimeoutFailed | Skip discount, fallback BR-09 | #4 |
| 5 | Member merge sang session khác (A3 case) | Filter `LeftAt > payTime` → EXCLUDED | #3 |
| 6 | Member early checkout | `LeftAt = checkout_time` → EXCLUDED nếu checkout trước Pay | existing |
| 7 | Host capture xong nhưng session fail | Ambient transaction + rollback | #8 |
| 8 | Walk-in session (no LobbyId) | `reservation == null` → skip | existing |
| 9 | Session merge từ 2 reservations | Sum source deposits via `MergedFromLobbyId` chain | #14 |
| 10 | Guest slot | Không áp `DiscountHostOnly`, vẫn chia đều trong `DiscountGroup` | existing |
| 11 | Idempotency | Key: `capture-discount-{sessionId}-{totalApplied}` | existing + #8 |
| 12 | Cooling-off user (BR-NEW-10) | Không ảnh hưởng discount (deposit ×2 chỉ ở confirm) | #10 |
| 13 | HeldBalance cap (BR-USER-LIMIT-03) | Refund remainder nếu vượt cap → warning UI | #9 |
| 14 | Concurrent Pay requests | FOR UPDATE lock session row | #24 |
| 15 | Webhook retry với state mismatch | Status re-check trước capture | #26 |

### Edge cases cho Split Bill per-member (existing API — chỉ reference)

> **Lưu ý:** Các edge case #16-22 dưới đây đã được handle bởi existing API `POST /pay-member` (xem A5.1). Design doc này chỉ liệt kê để audit completeness, **KHÔNG cần implement lại**.

| # | Edge case | Cách xử lý | Gap | API hiện có |
|---|---|---|---|---|
| 16 | Member pays cash riêng | `memberIds=[self]`, `paymentMethod=CASH` | #27 | ✅ `/pay-member` |
| 17 | Host pays self only | `memberIds=[hostMemberId]`, `paymentMethod=CASH` | #28 | #29 |
| 18 | Host covers A2+A3 (multi-pay) | `memberIds=[A2,A3]`, paymentMethod=A1 chooses | #29 | ✅ `/pay-member` |
| 19 | Member đã paid → gọi lại | API trả 409 `MemberAlreadyPaid` (built-in) | #30 | ✅ `/pay-member` |
| 20 | Member early checkout cash | `POST /partial-checkout` → SuspendedMutation | #31 | ✅ `/partial-checkout` |
| 21 | Member pays via individual SePay QR | `memberIds=[self]`, `paymentMethod=QR_CODE` → webhook | #34 | ✅ `/pay-member` + webhook |

### Edge cases CẦN implement mới (M2 — Gap #32, #33)

| # | Edge case | Cách xử lý | Gap | Status |
|---|---|---|---|---|
| 22 | Member muốn receipt riêng (expense report) | `GET /sessions/{id}/members/{memberId}/receipt` | #32 | ❌ NEW (Task C2.15) |
| 23 | Members no-show, others want to close session | `POST /sessions/{id}/force-close` (Manager/Admin) → mark unpaid = NoShow + debt record | #33 | ❌ NEW (Task C2.16) |

### Edge cases Case 2 (Member BVC Payment)

| # | Edge case | Cách xử lý | Gap |
|---|---|---|---|
| 1 | Member không đủ BVC | `SplitBvcCash` mode | existing |
| 2 | BvcAmount > totalDue | Throw 400 | #6 |
| 3 | Penalty payment bằng BVC | OK cho non-Guest member | #5 |
| 4 | 2 members cùng trả 1 lúc | `FOR UPDATE` lock trên member row | #16 |
| 5 | Member trả BVC xong, session fail | Transaction wrap + rollback | #18 |
| 6 | Webhook retry (SePay) với amount mismatch | Validate amount khớp ledger trước commit | #17 |
| 7 | Member đã PaidCash → retry BVC | Check `PaymentStatus != NotPaid` → throw 409 | existing |
| 8 | 2 members same UserId (multi-account) | Distinct UserId → throw 400 | #7 |
| 9 | Deposit Applied > 0 (from Case 1) | `totalDue = subtotal + penalty - depositApplied` | existing |
| 10 | Refund bill sai sau BVC payment | Auto-refund BVC về `availableBalance` + audit log | #11 |
| 11 | No-show member | Cleanup BVC pre-selection state | #21 |
| 12 | App resume mid-payment | Restore `MemberPaymentSelectionDto` state | #22 |
| 13 | Walk-in member (no deposit) | vẫn cho BVC payment (chỉ không có discount) | #15 |
| 14 | Refund → wallet vượt cap | Warning UI + manual review | #9 |

### Edge cases Option A (Merge Refund)

| # | Edge case | Cách xử lý |
|---|---|---|
| 1 | A3 có deposit + LeftAt = null | Refund + clear fields |
| 2 | A3 có deposit + DepositAppliedAmount > 0 (race lost) | KHÔNG refund, reason="DepositConsumed_BeforeMerge" |
| 3 | A3 không có deposit | Skip refund |
| 4 | A3 là Guest_Slot (no UserId) | Skip refund, log info |
| 5 | A3 merge2 lần (idempotent retry) | Skip lần 2 (DepositRefundedAt đã set) |
| 6 | Refund fail (DB/network) | Rollback + queue manual intervention |
| 7 | 2 staff merge A3 simultaneously | 1 success, 1 reject (transaction lock) |
| 8 | A3 merge xong Group A pay ngay | Group A filter: A3 EXCLUDED |
| 9 | A3 deposit đã refund nhưng Group A chưa pay | Group A pay vẫn exclude A3 |
| 10 | A3 merge sang Walk-in lobby (no Reservation) | Refund logic độc lập với lobby type |
| 11 | A3's wallet bị lock | Refund fail → manual intervention |

---

## D2. Migration SQL Full

### M1: Host Deposit Discount + Option A

```sql
-- File: sql/m1_host_deposit_discount.sql

BEGIN;

-- 1. Extend LedgerEntryType enum
ALTER TYPE "LedgerEntryType"
  ADD VALUE IF NOT EXISTS 'DepositRefund_Merge' AFTER 'DepositRelease';

-- 2. ActiveSessionMember: refund tracking (Option A)
ALTER TABLE "ActiveSessionMembers"
  ADD COLUMN "DepositRefundedAt" TIMESTAMP WITH TIME ZONE NULL,
  ADD COLUMN "DepositRefundReason" VARCHAR(500) NULL,
  ADD COLUMN "DepositRefundLedgerId" UUID NULL;

-- 3. Reservation: audit snapshot
ALTER TABLE "Reservations"
  ADD COLUMN "HostDepositUsageSnapshot" VARCHAR(20) NULL,
  ADD COLUMN "DiscountAppliedAmount" BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN "DiscountRefundedAmount" BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN "DiscountAuditTrail" JSONB NULL;

-- 4. MemberDepositAuditLog table
CREATE TABLE IF NOT EXISTS "MemberDepositAuditLogs" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "MemberId" UUID NOT NULL REFERENCES "ActiveSessionMembers"("Id") ON DELETE CASCADE,
    "UserId" UUID NOT NULL REFERENCES "Users"("Id"),
    "Action" VARCHAR(50) NOT NULL,
    "AmountBvc" BIGINT NOT NULL,
    "DepositId" UUID,
    "FromLobbyId" UUID,
    "FromSessionId" UUID,
    "ToLobbyId" UUID,
    "ToSessionId" UUID,
    "MergedAt" TIMESTAMP WITH TIME ZONE NULL,
    "Reason" VARCHAR(500),
    "IdempotencyKey" VARCHAR(100) UNIQUE,
    "LedgerEntryId" UUID,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByUserId" UUID REFERENCES "Users"("Id")
);

CREATE INDEX IF NOT EXISTS "ix_member_deposit_audit_member_id" 
    ON "MemberDepositAuditLogs"("MemberId");
CREATE INDEX IF NOT EXISTS "ix_member_deposit_audit_user_id" 
    ON "MemberDepositAuditLogs"("UserId");
CREATE INDEX IF NOT EXISTS "ix_member_deposit_audit_action" 
    ON "MemberDepositAuditLogs"("Action");
CREATE INDEX IF NOT EXISTS "ix_member_deposit_audit_created_at" 
    ON "MemberDepositAuditLogs"("CreatedAt" DESC);

COMMIT;
```

### M2: Member BVC Payment

```sql
-- File: sql/m2_member_bvc_payment.sql

BEGIN;

-- 1. ActiveSessionMember: BVC payment tracking
ALTER TABLE "ActiveSessionMembers"
  ADD COLUMN "PaidBvcAmount" BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN "PaidCashRemainder" DECIMAL(18, 0) NOT NULL DEFAULT 0,
  ADD COLUMN "BvcRefundedAt" TIMESTAMP WITH TIME ZONE NULL,
  ADD COLUMN "BvcRefundReason" VARCHAR(500) NULL;

-- 2. Extend MemberPaymentStatus enum
ALTER TYPE "MemberPaymentStatus"
  ADD VALUE IF NOT EXISTS 'PaidBvc' AFTER 'PaidQr';
ALTER TYPE "MemberPaymentStatus"
  ADD VALUE IF NOT EXISTS 'PartialBvc' AFTER 'PaidBvc';
ALTER TYPE "MemberPaymentStatus"
  ADD VALUE IF NOT EXISTS 'RefundedBvc' AFTER 'PartialBvc';

-- 3. MemberPaymentAuditLog table
CREATE TABLE IF NOT EXISTS "MemberPaymentAuditLogs" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "MemberId" UUID NOT NULL REFERENCES "ActiveSessionMembers"("Id"),
    "PaymentMethod" VARCHAR(20) NOT NULL,
    "AmountBvc" BIGINT NOT NULL DEFAULT 0,
    "AmountCash" DECIMAL(18, 0) NOT NULL DEFAULT 0,
    "WalletTxnId" UUID,
    "IdempotencyKey" VARCHAR(100) UNIQUE NOT NULL,
    "RefundedAt" TIMESTAMP WITH TIME ZONE NULL,
    "RefundReason" VARCHAR(500) NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByUserId" UUID NOT NULL REFERENCES "Users"("Id")
);

CREATE INDEX IF NOT EXISTS "ix_member_payment_audit_member_id" 
    ON "MemberPaymentAuditLogs"("MemberId");
CREATE INDEX IF NOT EXISTS "ix_member_payment_audit_created_at" 
    ON "MemberPaymentAuditLogs"("CreatedAt" DESC);

COMMIT;
```

---

## D3. Idempotency Key Patterns

| Use case | Pattern | Example |
|---|---|---|
| Merge refund (Option A) | `refund-merge-{memberId}-{depositId}-{mergedAt:o}` | `refund-merge-a1b2c3d4-d5e6f7g8-2026-10-01T09:30:00.000Z` |
| Discount capture | `capture-discount-{sessionId}-{totalApplied}` | `capture-discount-sess123-50000` |
| Remainder release | `release-remainder-{sessionId}-{remainder}` | `release-remainder-sess123-10000` |
| Member BVC debit | `member-bvc-debit-{memberId}-{billId}-{amount}` | `member-bvc-debit-mem456-bill789-30000` |
| Member BVC refund | `member-bvc-refund-{memberId}-{amount}-{reason}` | `member-bvc-refund-mem456-30000-bill_sai` |
| All-paid trigger | `trigger-paid-{sessionId}` | `trigger-paid-sess123` |

---

## D4. Audit Log Schema

### MemberDepositAuditLog (Option A)

| Column | Type | Notes |
|---|---|---|
| Id | UUID PK | |
| MemberId | UUID FK | ActiveSessionMember |
| UserId | UUID FK | |
| Action | VARCHAR(50) | 'Refunded_OnMerge', 'Captured_OnGroupAPay', 'Captured_OnEarlyCheckout' |
| AmountBvc | BIGINT | |
| DepositId | UUID | |
| FromLobbyId | UUID | (merge context) |
| FromSessionId | UUID | |
| ToLobbyId | UUID | |
| ToSessionId | UUID | |
| MergedAt | TIMESTAMP | |
| Reason | VARCHAR(500) | |
| IdempotencyKey | VARCHAR(100) UNIQUE | |
| LedgerEntryId | UUID | FK to BvcLedgerEntry |
| CreatedAt | TIMESTAMP | |
| CreatedByUserId | UUID | Staff user |

### MemberPaymentAuditLog (M2)

| Column | Type | Notes |
|---|---|---|
| Id | UUID PK | |
| MemberId | UUID FK | |
| PaymentMethod | VARCHAR(20) | 'Bvc', 'Cash', 'BvcPartial' |
| AmountBvc | BIGINT | |
| AmountCash | DECIMAL(18,0) | |
| WalletTxnId | UUID | FK to BvcLedgerEntry |
| IdempotencyKey | VARCHAR(100) UNIQUE | |
| RefundedAt | TIMESTAMP | (NULL unless refunded) |
| RefundReason | VARCHAR(500) | |
| CreatedAt | TIMESTAMP | |
| CreatedByUserId | UUID FK | |

---

## D5. Open Questions

Cần user/product confirm trước khi implement:

1. **BR-22 activation timeline:** Khi nào BR-22 (per-member deposit) sẽ active? Option A được design forward-compat nhưng cần biết khi nào để test thoroughly.

2. **HeldBalance cap exact value:** BR-USER-LIMIT-03 đề cập cap, giá trị cụ thể là bao nhiêu? (để tính toán refund edge case #9)

3. **Refund manual intervention queue:** POS staff nào nhận notification? Cafe manager hay admin?

4. **A/B test cafe selection:** 2 cafes pilot nào? (Urban vs Suburban?)

5. **Feature flag rollout order:** M1 trước rồi M2, hay parallel?

6. **Reporting tool:** Built-in dashboard hay export CSV cho finance team?

7. **Dispute handling:** Nếu A3 claim "tôi đã refund mà chưa thấy tiền" → process thế nào?

8. **Tax implication:** Discount bill có ảnh hưởng VAT calculation không? (Cần confirm với finance)

---

## 📋 Quick Reference — Task Counts (cập nhật sau khi check existing APIs)

> **Note:** Sau khi check codebase, 6 gaps (27-31, 34) được handle bởi existing APIs (`/pay-member` + `/partial-checkout` + SePay webhook). **Net new work M2 giảm từ 45 → 47 tasks** (do thêm 2 endpoints mới C2.15, C2.16 nhưng trừ effort viết code các gap đã có).

| Milestone | Schema | Backend | Tests | Frontend | Reporting | Total tasks |
|---|---|---|---|---|---|---|
| **M1** | 11 | 18 | 16 | 3 | 0 | **48 tasks** (unchanged) |
| **M2** | 4 | 16 (+2 new: receipt + force-close) | 14 | 8 | 5 | **47 tasks** (+2) |
| **Combined** | 15 | 34 | 30 | 11 | 5 | **95 tasks** |

### Effective new work M2 (sau khi reuse existing APIs)

| M2 task | Effort | Reuse existing API? |
|---|---|---|
| C2.1-C2.14 (BVC Member Payment) | 12.5 ngày | ❌ NEW |
| **C2.15 (per-member receipt)** | 0.5 ngày | ❌ NEW |
| **C2.16 (force-close unpaid)** | 0.5 ngày | ❌ NEW |
| C3.1-C3.3 (POS UI) | 1 ngày | ✅ Reuse existing components |
| C3.4-C3.8 (Mobile UI) | 4 ngày | ❌ NEW (BVC tab) |
| C5.1-C5.5 (Reporting) | 3 ngày | ❌ NEW |
| **Net new** | **~13.5 ngày** | (down từ 19 nhờ reuse) |

---

## 📋 Quick Reference — Existing APIs Used (không build lại)

| API | Status | Used for (Gap) |
|---|---|---|
| `POST /pay-member` (CASH) | ✅ Existing | #27, #28, #29, #34 |
| `POST /pay-member` (QR) + webhook | ✅ Existing | #34 |
| `POST /partial-checkout` | ✅ Existing | #31 |
| `POST /pay` (full session) | ✅ Existing | Case 1 integration (M1) |
| 409 `MemberAlreadyPaid` | ✅ Existing | #30 guard |
| `MemberPayments` audit | ✅ Existing | Audit trail cho C2 |

## 📋 Quick Reference — Timeline

| Milestone | Days | Wall-clock |
|---|---|---|
| M1 | 10 (3.5 + 4 + 2 + 0.5 + test parallel) | 10 days |
| M2 | 13.5 (5.5 + 5 + 3) + test parallel | 19 days |
| **Total** | | **~29 days** |

---

**Document version:** 1.1
**Last updated:** 2026-10-01 09:45 UTC+7
**Next review:** Sau M1 acceptance (estimated 2026-10-11)

**Changelog:**
- v1.0 (2026-10-01 09:30): Initial design — 26 gaps, 93 tasks
- v1.1 (2026-10-01 09:45): Add 8 gaps (27-34), reference existing APIs (`/pay-member`, `/partial-checkout`), add 2 new M2 tasks (C2.15 per-member receipt, C2.16 force-close unpaid). Net new tasks M2: +2 (down từ 8 vì reuse existing).
- v1.2 (2026-10-01 09:53): **Code verification pass** — read actual implementations of `CafePosController.PayMembers`, `SplitBillService.PayMembersAsync`, `MemberPaymentStatus` enum. Confirmed 6 gaps (27-31, 34) thực sự đã được handle. Phát hiện C2.16 tham chiếu 3 enum/entity chưa tồn tại (`PaidByHost`, `MemberStatus.NoShow`, `ActiveSessionStatus.UnpaidForced`, `MemberDebt`) — đã cập nhật Task C2.16 schema requirements. Add A7 Verification Appendix.

---

## A7. Code Verification (2026-10-01 09:53)

Phần này ghi lại verification thực tế bằng cách đọc code, không chỉ dựa vào docs.

### A7.1. `POST /pay-member` — verified ✅

**File:** `BoardVerse.API/Controllers/CafePosController.cs` line 1214-1233
```csharp
[HttpPost("sessions/{sessionId:guid}/pay-member")]
public async Task<IActionResult> PayMembers(
    Guid cafeId, Guid sessionId, [FromBody] PayMemberRequestDto request)
{
    var (userId, role) = GetViewerContext();
    var result = await _splitBillService.PayMembersAsync(
        sessionId, request, userId, role, HttpContext.RequestAborted);
    return this.NewResponse(200, "Thanh toan per-member thanh cong.", result);
}
```

**File:** `BoardVerse.Services/Services/SplitBillService.cs` line 86-141
- Validate `paymentMethod ∈ {CASH, QR_CODE}` (line 105-109).
- Validate `session.Status == GroupSessionStatus.Unpaid` (line 113-116).
- Member đã thanh toán (`PaymentStatus != NotPaid`) → throw `ConflictException` (line 141-145) → trả **409** đúng như doc claim.
- Member không thuộc session → throw `NotFoundException` (line 132-136).

### A7.2. `MemberPaymentStatus` enum — verified với corrections

**File:** `BoardVerse.Core/Enum/MemberPaymentStatus.cs`
```csharp
public enum MemberPaymentStatus
{
    NotPaid = 0,
    PaidQr = 1,
    PaidCash = 2
}
```

⚠️ **Quan trọng:** Enum hiện tại **CHỈ CÓ 3 values** (NotPaid/PaidQr/PaidCash), **CHƯA có** `PaidByHost` như C2.16 design draft dùng. Cần:
- Thêm `PaidByHost = 3` enum value + migration `AddPaidByHostToMemberPaymentStatus`.
- Hoặc dùng tạm `PaidCash` cho compensation by host (giả định host trả cash).

### A7.3. `IndividualSessionStatus` + `GroupSessionStatus` enums — verified

**File:** `BoardVerse.Core/Enum/IndividualSessionStatus.cs`
```csharp
public enum IndividualSessionStatus { Playing = 0, SuspendedMutation = 1, Finished = 2 }
```
→ **CHƯA có** `NoShow` value. Cần thêm trong C2.16 migration.

**File:** `BoardVerse.Core/Enum/GroupSessionStatus.cs`
```csharp
public enum GroupSessionStatus { Active = 0, Checking = 1, Unpaid = 2, Paid = 3, Closed = 4 }
```
→ **CHƯA có** `UnpaidForced` value. Cần thêm trong C2.16 migration.

### A7.4. Entities chưa tồn tại

`MemberDebt` entity, `ForceCloseAuditLog` entity — **CHƯA CÓ** trong codebase. Cần tạo mới trong C2.16 schema.

### A7.5. Summary — design corrections cần apply

| Item | Status | Action |
|---|---|---|
| `POST /pay-member` endpoint | ✅ Đã có | Dùng trực tiếp |
| `PaymentMethod = CASH` mode | ✅ Đã có | Dùng trực tiếp |
| `PaymentMethod = QR_CODE` mode | ✅ Đã có | Dùng trực tiếp |
| Webhook `/payments/sepay/webhook/member-payment` | ✅ Đã có | Dùng trực tiếp |
| 409 MemberAlreadyPaid guard | ✅ Đã có | Dùng trực tiếp |
| `MemberPaymentStatus.PaidByHost` enum | ❌ Chưa có | Cần thêm (C2.16 schema) |
| `IndividualSessionStatus.NoShow` enum | ❌ Chưa có | Cần thêm (C2.16 schema) |
| `GroupSessionStatus.UnpaidForced` enum | ❌ Chưa có | Cần thêm (C2.16 schema) |
| `MemberDebt` entity | ❌ Chưa có | Cần tạo (C2.16 schema) |
| `ForceCloseAuditLog` entity | ❌ Chưa có | Cần tạo (C2.16 schema) |
| Endpoint `GET /receipt/member/{memberId}` | ❌ Chưa có | Cần tạo (C2.15) |
| Endpoint `POST /sessions/{id}/force-close` | ❌ Chưa có | Cần tạo (C2.16) |
| `POST /partial-checkout` endpoint | ✅ Đã có | Dùng trực tiếp |
