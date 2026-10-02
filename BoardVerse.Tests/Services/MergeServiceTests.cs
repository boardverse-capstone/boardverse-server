using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// M1 / Phase 4a — Unit tests cho <c>MergeService.HandleMemberMergeAsync</c>
/// (Option A — refund PER-MEMBER deposit khi member merge sang lobby khác).
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §B4 + §B5.
///
/// <para>
/// <b>SCOPE:</b> Chỉ test Option A (per-member deposit refund, BR-22 forward-compat).
/// Host deposit KHÔNG qua MergeService — xem <c>LobbyMergeDepositFollowTests</c>.
/// </para>
/// </summary>
public class MergeServiceTests : IDisposable
{
    private readonly Mock<IActiveSessionRepository> _activeSessionRepo = new();
    private readonly Mock<IBookingDepositRepository> _depositRepo = new();
    private readonly Mock<IMemberDepositAuditLogRepository> _auditRepo = new();
    private readonly Mock<IWalletService> _walletService = new();
    private readonly Mock<ILogger<MergeService>> _logger = new();

    private BoardVerseDbContext _db = default!;

    public MergeServiceTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new BoardVerseDbContext(options);
    }

    public void Dispose()
    {
        _db?.Dispose();
    }

    private MergeService CreateService() => new(
        _activeSessionRepo.Object, _depositRepo.Object, _auditRepo.Object,
        _walletService.Object, _db, _logger.Object);

    /// <summary>
    /// Helper: tạo ActiveSessionMember với deposit state.
    /// </summary>
    private static ActiveSessionMember CreateMember(
        Guid memberId,
        Guid userId,
        Guid activeSessionId,
        Guid? depositId,
        decimal depositAppliedAmount = 0m,
        bool isGuestSlot = false)
    {
        return new ActiveSessionMember
        {
            Id = memberId,
            UserId = isGuestSlot ? null : userId,
            IsGuestSlot = isGuestSlot,
            ActiveSessionId = activeSessionId,
            Status = IndividualSessionStatus.SuspendedMutation,
            JoinedAt = DateTime.UtcNow.AddHours(-1),
            TotalMinutesPlayed = 60,
            DepositId = depositId,
            DepositAppliedAmount = depositAppliedAmount,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Mock default cho audit log repository — AddAsync trả về entity với Id mới.
    /// </summary>
    private void SetupDefaultAuditMock()
    {
        _auditRepo.Setup(r => r.AddAsync(It.IsAny<MemberDepositAuditLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MemberDepositAuditLog log, CancellationToken _) =>
            {
                log.Id = Guid.NewGuid();
                return log;
            });
    }

    // =====================================================================
    // CASE 1: Basic Option A refund
    // =====================================================================

    /// <summary>
    /// Member có DepositId + DepositAppliedAmount = 0 → refund về wallet + audit log.
    /// M1 acceptance: "A3's per-member deposit (BR-22 nếu có) → refund về wallet".
    /// </summary>
    [Fact]
    public async Task HandleMemberMergeAsync_RefundsPerMemberDepositToWallet()
    {
        var memberId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var activeSessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();
        var fromLobbyId = Guid.NewGuid();
        var toLobbyId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        var member = CreateMember(memberId, userId, activeSessionId, depositId, depositAppliedAmount: 0m);
        var deposit = new BookingDeposit
        {
            Id = depositId,
            UserId = userId,
            Amount = 50000m,
            Status = BookingDepositStatus.Pending
        };

        _activeSessionRepo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
        _depositRepo.Setup(r => r.GetByIdAsync(depositId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deposit);

        var ledgerEntry = new BvcLedgerEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = LedgerEntryType.DepositRefund_Merge,
            Amount = 50000L
        };
        _walletService.Setup(w => w.RefundMemberDepositOnMergeAsync(
            userId, 50000L, depositId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledgerEntry);

        SetupDefaultAuditMock();

        var result = await CreateService().HandleMemberMergeAsync(
            memberId, fromLobbyId, toLobbyId, staffId);

        Assert.True(result.DepositRefunded);
        Assert.Equal(50000L, result.DepositRefundedBvc);
        Assert.Equal(ledgerEntry.Id, result.LedgerEntryId);
        Assert.Equal("Refunded_OnMerge", result.Action);

        // Verify wallet service called đúng amount
        _walletService.Verify(w => w.RefundMemberDepositOnMergeAsync(
            userId, 50000L, depositId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify audit log added
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<MemberDepositAuditLog>(log =>
                log.MemberId == memberId &&
                log.Action == "Refunded_OnMerge" &&
                log.AmountBvc == 50000L),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =====================================================================
    // CASE 2: Guest_Slot skip refund
    // =====================================================================

    /// <summary>
    /// Guest_Slot member (IsGuestSlot=true) → skip refund, return GuestSlot_NoDeposit.
    /// M1 acceptance: "Merge_GuestSlot_NoRefund".
    /// </summary>
    [Fact]
    public async Task HandleMemberMergeAsync_GuestSlot_NoRefund()
    {
        var memberId = Guid.NewGuid();
        var activeSessionId = Guid.NewGuid();
        var fromLobbyId = Guid.NewGuid();
        var toLobbyId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        // Guest slot: UserId = null, IsGuestSlot = true
        var guestMember = CreateMember(memberId, userId: Guid.Empty, activeSessionId,
            depositId: null, depositAppliedAmount: 0m, isGuestSlot: true);

        _activeSessionRepo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(guestMember);

        SetupDefaultAuditMock();

        var result = await CreateService().HandleMemberMergeAsync(
            memberId, fromLobbyId, toLobbyId, staffId);

        Assert.False(result.DepositRefunded);
        Assert.Equal(0L, result.DepositRefundedBvc);
        Assert.Null(result.UserId);
        Assert.Equal("GuestSlot_NoDeposit", result.Action);

        // Verify wallet service KHÔNG được gọi
        _walletService.Verify(w => w.RefundMemberDepositOnMergeAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =====================================================================
    // CASE 3: Idempotency
    // =====================================================================

    /// <summary>
    /// Member đã được refund trước đó (DepositRefundedAt set) → idempotent replay,
    /// trả về "IdempotentReplay" + KHÔNG refund lại.
    /// M1 acceptance: "Idempotent qua capture + refund flow".
    /// </summary>
    [Fact]
    public async Task HandleMemberMergeAsync_IdempotentReplay_DoesNotRefundAgain()
    {
        var memberId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var activeSessionId = Guid.NewGuid();
        var fromLobbyId = Guid.NewGuid();
        var toLobbyId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var firstRefundAt = DateTime.UtcNow.AddMinutes(-10);

        var member = CreateMember(memberId, userId, activeSessionId, depositId: null,
            depositAppliedAmount: 0m);
        member.DepositRefundedAt = firstRefundAt;
        member.DepositRefundReason = "Merged từ Lobby X";
        member.MergedFromLobbyId = fromLobbyId;

        _activeSessionRepo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
        _auditRepo.Setup(r => r.GetByMemberIdAsync(memberId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemberDepositAuditLog>
            {
                new MemberDepositAuditLog
                {
                    Id = Guid.NewGuid(),
                    MemberId = memberId,
                    Action = "Refunded_OnMerge",
                    AmountBvc = 50000L
                }
            });

        var result = await CreateService().HandleMemberMergeAsync(
            memberId, fromLobbyId, toLobbyId, staffId);

        Assert.True(result.DepositRefunded);
        Assert.Equal("IdempotentReplay", result.Action);
        Assert.Equal(50000L, result.DepositRefundedBvc);
        Assert.Equal(firstRefundAt, result.ProcessedAt);

        // Verify wallet service KHÔNG được gọi lần 2
        _walletService.Verify(w => w.RefundMemberDepositOnMergeAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =====================================================================
    // CASE 4: Member without DepositId (BR-22 chưa active)
    // =====================================================================

    /// <summary>
    /// Member không có DepositId + không phải Guest_Slot → NoDepositToRefund action.
    /// M1: BR-22 chưa active → forward-compat path.
    /// </summary>
    [Fact]
    public async Task HandleMemberMergeAsync_NoDeposit_ReturnsNoDepositToRefund()
    {
        var memberId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var activeSessionId = Guid.NewGuid();
        var fromLobbyId = Guid.NewGuid();
        var toLobbyId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        // Member hợp lệ (có UserId) nhưng không có DepositId
        var member = CreateMember(memberId, userId, activeSessionId, depositId: null,
            depositAppliedAmount: 0m);

        _activeSessionRepo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);

        SetupDefaultAuditMock();

        var result = await CreateService().HandleMemberMergeAsync(
            memberId, fromLobbyId, toLobbyId, staffId);

        Assert.False(result.DepositRefunded);
        Assert.Equal(0L, result.DepositRefundedBvc);
        Assert.Equal("NoDepositToRefund", result.Action);

        // Verify MergedFromLobbyId được set
        Assert.Equal(fromLobbyId, member.MergedFromLobbyId);
        _activeSessionRepo.Verify(r => r.UpdateMemberAsync(member, It.IsAny<CancellationToken>()), Times.Once);

        // Audit log vẫn được ghi (để tracking)
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<MemberDepositAuditLog>(log => log.Action == "NoDepositToRefund"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =====================================================================
    // CASE 5: B4.9 — Deposit đã apply vào bill trước khi merge → không refund
    // =====================================================================

    /// <summary>
    /// Member có DepositId + DepositAppliedAmount > 0 → race lost (deposit đã apply vào bill).
    /// KHÔNG refund về wallet, action = "DepositConsumed_BeforeMerge".
    /// M1 acceptance: "A3 DepositAppliedAmount>0, merge → No refund, reason=DepositConsumed_BeforeMerge".
    /// </summary>
    [Fact]
    public async Task HandleMemberMergeAsync_DepositAlreadyApplied_NoRefund()
    {
        var memberId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var activeSessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();
        var fromLobbyId = Guid.NewGuid();
        var toLobbyId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        // Member đã apply 50k deposit vào bill trước khi merge
        var member = CreateMember(memberId, userId, activeSessionId, depositId,
            depositAppliedAmount: 50000m);

        _activeSessionRepo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);

        SetupDefaultAuditMock();

        var result = await CreateService().HandleMemberMergeAsync(
            memberId, fromLobbyId, toLobbyId, staffId);

        // KHÔNG refund, action = DepositConsumed_BeforeMerge
        Assert.False(result.DepositRefunded);
        Assert.Equal(0L, result.DepositRefundedBvc);
        Assert.Equal("DepositConsumed_BeforeMerge", result.Action);
        Assert.Equal("DepositConsumed_BeforeMerge", member.DepositRefundReason);
        Assert.Null(member.DepositId); // deposit cleared from member

        // Verify wallet service KHÔNG được gọi
        _walletService.Verify(w => w.RefundMemberDepositOnMergeAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Audit log ghi nhận race lost
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<MemberDepositAuditLog>(log =>
                log.MemberId == memberId &&
                log.Action == "DepositConsumed_BeforeMerge" &&
                log.AmountBvc == 0L),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =====================================================================
    // CASE 6: B4.16 — Audit log + ledger entry format verification
    // =====================================================================

    /// <summary>
    /// Verify <c>MemberDepositAuditLog</c> + <c>BvcLedgerEntry</c> đúng format khi refund.
    /// - Audit log: MemberId, Action="Refunded_OnMerge", AmountBvc, WalletTxnId set.
    /// - Ledger entry: Type=DepositRefund_Merge, Amount dương, UserId match.
    /// </summary>
    [Fact]
    public async Task HandleMemberMergeAsync_AuditLogAndLedgerEntry_FormatCorrect()
    {
        var memberId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var activeSessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();
        var fromLobbyId = Guid.NewGuid();
        var toLobbyId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        var member = CreateMember(memberId, userId, activeSessionId, depositId,
            depositAppliedAmount: 0m);
        var deposit = new BookingDeposit
        {
            Id = depositId,
            UserId = userId,
            Amount = 30000m, // 30 BVC deposit
            Status = BookingDepositStatus.Pending
        };

        _activeSessionRepo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
        _depositRepo.Setup(r => r.GetByIdAsync(depositId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deposit);

        var ledgerId = Guid.NewGuid();
        var ledgerEntry = new BvcLedgerEntry
        {
            Id = ledgerId,
            UserId = userId,
            Type = LedgerEntryType.DepositRefund_Merge,
            Amount = 30000L
        };
        _walletService.Setup(w => w.RefundMemberDepositOnMergeAsync(
            userId, 30000L, depositId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledgerEntry);

        MemberDepositAuditLog? capturedLog = null;
        _auditRepo.Setup(r => r.AddAsync(It.IsAny<MemberDepositAuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<MemberDepositAuditLog, CancellationToken>((log, _) =>
            {
                capturedLog = log;
                log.Id = Guid.NewGuid();
            })
            .ReturnsAsync((MemberDepositAuditLog log, CancellationToken _) => log);

        var result = await CreateService().HandleMemberMergeAsync(
            memberId, fromLobbyId, toLobbyId, staffId);

        // Verify ledger entry returned by wallet service linked to result
        Assert.Equal(ledgerId, result.LedgerEntryId);

        // Verify audit log fields
        Assert.NotNull(capturedLog);
        Assert.Equal(memberId, capturedLog!.MemberId);
        Assert.Equal("Refunded_OnMerge", capturedLog.Action);
        Assert.Equal(30000L, capturedLog.AmountBvc);
        Assert.Equal(ledgerId, capturedLog.LedgerEntryId);
        // DepositId được clear trên member sau refund (member.DepositId = null tại line 220),
        // nên audit log capture DepositId = null — đây là design intent (forward-link qua LedgerEntryId).
        Assert.Null(capturedLog.DepositId);
    }

    // =====================================================================
    // CASE 7: B4.15 — Concurrency test (idempotency under race)
    // =====================================================================

    /// <summary>
    /// 2 staff trigger merge cho cùng A3 đồng thời. Mong đợi: 1 success, 1 idempotent skip.
    /// Wallet chỉ được +X 1 lần (KHÔNG double-refund).
    /// M1 acceptance: "Idempotent qua capture + refund flow" + "race condition resistant".
    /// </summary>
    [Fact]
    public async Task HandleMemberMergeAsync_ConcurrentCalls_OnlyOneRefunds()
    {
        var memberId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var activeSessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();
        var fromLobbyId = Guid.NewGuid();
        var toLobbyId = Guid.NewGuid();
        var staffAId = Guid.NewGuid();
        var staffBId = Guid.NewGuid();

        var member = CreateMember(memberId, userId, activeSessionId, depositId,
            depositAppliedAmount: 0m);
        var deposit = new BookingDeposit
        {
            Id = depositId,
            UserId = userId,
            Amount = 50000m,
            Status = BookingDepositStatus.Pending
        };

        _activeSessionRepo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // Return a SHARED instance (simulate DB row) — first call sets DepositRefundedAt,
                // second call sees it set → idempotent replay.
                return member;
            });
        _depositRepo.Setup(r => r.GetByIdAsync(depositId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deposit);

        var ledgerEntry = new BvcLedgerEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = LedgerEntryType.DepositRefund_Merge,
            Amount = 50000L
        };
        _walletService.Setup(w => w.RefundMemberDepositOnMergeAsync(
            userId, 50000L, depositId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledgerEntry);

        // IdempotentReplay path queries existing audit log → return empty list.
        _auditRepo.Setup(r => r.GetByMemberIdAsync(memberId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemberDepositAuditLog>());

        SetupDefaultAuditMock();

        // Race: 2 staff trigger merge concurrently với cùng memberId.
        var taskA = CreateService().HandleMemberMergeAsync(memberId, fromLobbyId, toLobbyId, staffAId);
        var taskB = CreateService().HandleMemberMergeAsync(memberId, fromLobbyId, toLobbyId, staffBId);

        var results = await Task.WhenAll(taskA, taskB);

        // Verify: 1 refund + 1 idempotent replay.
        var refundCount = results.Count(r => r.Action == "Refunded_OnMerge");
        var idempotentCount = results.Count(r => r.Action == "IdempotentReplay");

        Assert.Equal(1, refundCount);
        Assert.Equal(1, idempotentCount);

        // Verify wallet service called exactly 1 lần (KHÔNG double-refund).
        _walletService.Verify(w => w.RefundMemberDepositOnMergeAsync(
            userId, 50000L, depositId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}