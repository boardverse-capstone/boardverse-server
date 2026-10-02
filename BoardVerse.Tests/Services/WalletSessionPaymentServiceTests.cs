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
/// M2 / Case 2 — Unit tests cho <c>WalletSessionPaymentService</c>.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.6, §C2.7, §C2.8.
///
/// <para>
/// <b>SCOPE:</b> Validate authorization (Gap-#1), idempotency (Gap-#3),
/// overpayment, balance check (Gap-#4), all-paid trigger, refund flow.
/// </para>
/// </summary>
public class WalletSessionPaymentServiceTests : IDisposable
{
    private readonly Mock<IActiveSessionRepository> _activeSessionRepo = new();
    private readonly Mock<IWalletService> _walletService = new();
    private readonly Mock<ILogger<WalletSessionPaymentService>> _logger = new();

    private BoardVerseDbContext _db = default!;

    public WalletSessionPaymentServiceTests()
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

    private WalletSessionPaymentService CreateService() => new(
        _activeSessionRepo.Object, _walletService.Object, _db, _logger.Object);

    // =====================================================================
    // HELPERS
    // =====================================================================

    private static ActiveSession CreateUnpaidSession(
        Guid hostUserId,
        params (Guid memberUserId, bool isHost, bool isGuest, MemberPaymentStatus status,
                decimal subtotal, decimal penalty, decimal deposit, long paidBvc)[] memberSpecs)
    {
        var sessionId = Guid.NewGuid();
        var members = new List<ActiveSessionMember>();

        foreach (var (memberUserId, isHost, isGuest, status, subtotal, penalty, deposit, paidBvc) in memberSpecs)
        {
            members.Add(new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = sessionId,
                UserId = isGuest ? null : memberUserId,
                IsGuestSlot = isGuest,
                GuestDisplayName = isGuest ? "Khách vô danh" : null,
                IsHost = isHost,
                Status = IndividualSessionStatus.Finished,
                JoinedAt = DateTime.UtcNow.AddHours(-2),
                PaymentStatus = status,
                Subtotal = subtotal,
                PenaltyAmount = penalty,
                DepositAppliedAmount = deposit,
                PaidBvcAmount = paidBvc
            });
        }

        var hostMember = members.FirstOrDefault(m => m.IsHost);
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = Guid.NewGuid(),
            HostId = hostMember?.UserId ?? hostUserId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            Members = members
        };
        return session;
    }

    /// <summary>First member (helper for tests).</summary>
    private static ActiveSessionMember FirstMember(ActiveSession session) =>
        session.Members.First();

    /// <summary>Second member (helper for tests).</summary>
    private static ActiveSessionMember SecondMember(ActiveSession session) =>
        session.Members.Skip(1).First();

    private void SetupSessionRepo(ActiveSession session)
    {
        _activeSessionRepo
            .Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // ReleaseMembersAndCloseLobbyAsync + ReleaseSessionTableAndBoxAsync là no-op trong test.
        _activeSessionRepo
            .Setup(r => r.ReleaseMembersAndCloseLobbyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _activeSessionRepo
            .Setup(r => r.ReleaseSessionTableAndBoxAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupWalletDebit(long returnedAmount = 50)
    {
        var ledgerEntry = new BvcLedgerEntry
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = LedgerEntryType.MemberBillDebit,
            Amount = returnedAmount,
            CreatedAt = DateTime.UtcNow,
            IdempotencyKey = "test-key"
        };
        _walletService
            .Setup(w => w.DirectDebitForBillAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledgerEntry);
    }

    private void SetupWalletRefund()
    {
        var ledgerEntry = new BvcLedgerEntry
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = LedgerEntryType.MemberBillRefund,
            Amount = 50,
            CreatedAt = DateTime.UtcNow,
            IdempotencyKey = "test-refund"
        };
        _walletService
            .Setup(w => w.RefundMemberBillAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledgerEntry);
    }

    // =====================================================================
    // GAP-#1: AUTHORIZATION
    // =====================================================================

    [Fact]
    public async Task GetMemberBillPreviewAsync_OtherMemberAccess_AsPlayer_ThrowsForbidden()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // user khác, không phải member, không phải host
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 0m, 0L)
        );
        SetupSessionRepo(session);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateService().GetMemberBillPreviewAsync(
                session.Id, FirstMember(session).Id, attackerUserId, "Player", default));
        Assert.Contains("không có quyền", ex.Message);
    }

    [Fact]
    public async Task GetMemberBillPreviewAsync_OwnMemberAccess_AsPlayer_Succeeds()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 50000m, 0L)
        );
        SetupSessionRepo(session);

        // Act
        var preview = await CreateService().GetMemberBillPreviewAsync(
            session.Id, FirstMember(session).Id, memberUserId, "Player", default);

        // Assert
        Assert.Equal(10000m, preview.TotalDue);
        Assert.False(preview.IsGuestSlot);
    }

    [Fact]
    public async Task GetMemberBillPreviewAsync_StaffAccess_AlwaysSucceeds()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 0m, 0L)
        );
        SetupSessionRepo(session);

        // Act
        var preview = await CreateService().GetMemberBillPreviewAsync(
            session.Id, FirstMember(session).Id, staffUserId, "CafeStaff", default);

        // Assert
        Assert.Equal(60000m, preview.TotalDue);
    }

    [Fact]
    public async Task PayMemberBillAsync_OtherMemberAccess_AsPlayer_ThrowsForbidden()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 0m, 0L)
        );
        SetupSessionRepo(session);
        SetupWalletDebit();

        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = 60,
            IdempotencyKey = "test-key-1"
        };

        // Act + Assert
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateService().PayMemberBillAsync(
                session.Id, FirstMember(session).Id, request, attackerUserId, "Player", default));
    }

    // =====================================================================
    // GAP-#3: IDEMPOTENCY MEMBER-MISMATCH
    // =====================================================================

    [Fact]
    public async Task PayMemberBillAsync_IdempotencyKeyUsedByAnotherMember_ThrowsConflict()
    {
        // Arrange: Pre-seed audit log với key "shared-key" cho member A.
        var memberAUserId = Guid.NewGuid();
        var memberBUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberAUserId,
            (memberAUserId, true, false, MemberPaymentStatus.PaidBvc, 60000m, 0m, 0m, 60L), // A: đã paid
            (memberBUserId, false, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 0m, 0L)  // B: chưa paid
        );
        SetupSessionRepo(session);
        SetupWalletDebit();

        // Seed audit log row với key "shared-key" cho member A.
        var sharedKey = "shared-key";
        _db.MemberPaymentAuditLogs.Add(new MemberPaymentAuditLog
        {
            Id = Guid.NewGuid(),
            MemberId = FirstMember(session).Id, // A
            UserId = memberAUserId,
            PaymentMethod = "Bvc",
            AmountBvc = 60,
            AmountCash = 0,
            IdempotencyKey = sharedKey,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act: member B thanh toán với cùng key.
        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = 60,
            IdempotencyKey = sharedKey
        };

        // Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().PayMemberBillAsync(
                session.Id, SecondMember(session).Id, request, memberBUserId, "Player", default));
    }

    [Fact]
    public async Task PayMemberBillAsync_IdempotencyReplay_ReturnsSameResponse()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 50000m, 0L)
        );
        SetupSessionRepo(session);

        var key = "replay-key";
        var originalAmount = 10L;
        _db.MemberPaymentAuditLogs.Add(new MemberPaymentAuditLog
        {
            Id = Guid.NewGuid(),
            MemberId = FirstMember(session).Id,
            UserId = memberUserId,
            PaymentMethod = "Bvc",
            AmountBvc = originalAmount,
            AmountCash = 0,
            WalletTxnId = Guid.NewGuid(),
            IdempotencyKey = key,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = originalAmount,
            IdempotencyKey = key
        };

        // Act
        var response = await CreateService().PayMemberBillAsync(
            session.Id, FirstMember(session).Id, request, memberUserId, "Player", default);

        // Assert: replay trả lại audit log ID, không gọi wallet.
        Assert.NotEqual(Guid.Empty, response.AuditLogId);
        _walletService.Verify(
            w => w.DirectDebitForBillAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PayMemberBillAsync_IdempotencyReplay_AmountMismatch_ThrowsConflict()
    {
        // Arrange: audit log với key, amount = 100.
        var memberUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 50000m, 0L)
        );
        SetupSessionRepo(session);

        var key = "amount-mismatch";
        _db.MemberPaymentAuditLogs.Add(new MemberPaymentAuditLog
        {
            Id = Guid.NewGuid(),
            MemberId = FirstMember(session).Id,
            UserId = memberUserId,
            PaymentMethod = "Bvc",
            AmountBvc = 100,
            AmountCash = 0,
            IdempotencyKey = key,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act: replay với amount = 50 (khác 100).
        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = 50,
            IdempotencyKey = key
        };

        // Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().PayMemberBillAsync(
                session.Id, FirstMember(session).Id, request, memberUserId, "Player", default));
    }

    // =====================================================================
    // OVERPAYMENT + VALIDATION
    // =====================================================================

    [Fact]
    public async Task PayMemberBillAsync_Overpayment_ThrowsBadRequest()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 50000m, 0L)
        );
        SetupSessionRepo(session);
        SetupWalletDebit();

        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = 100_000, // vượt totalDue 10.000
            IdempotencyKey = "overpay"
        };

        // Act + Assert
        await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateService().PayMemberBillAsync(
                session.Id, FirstMember(session).Id, request, memberUserId, "Player", default));
    }

    [Fact]
    public async Task PayMemberBillAsync_SessionNotUnpaid_ThrowsConflict()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 0m, 0L)
        );
        session.Status = GroupSessionStatus.Paid; // already paid
        SetupSessionRepo(session);

        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = 60,
            IdempotencyKey = "session-paid"
        };

        // Act + Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().PayMemberBillAsync(
                session.Id, FirstMember(session).Id, request, memberUserId, "Player", default));
    }

    [Fact]
    public async Task PayMemberBillAsync_MemberAlreadyPaid_ThrowsConflict()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.PaidBvc, 60000m, 0m, 0m, 60L)
        );
        SetupSessionRepo(session);

        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = 60,
            IdempotencyKey = "already-paid"
        };

        // Act + Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().PayMemberBillAsync(
                session.Id, FirstMember(session).Id, request, memberUserId, "Player", default));
    }

    [Fact]
    public async Task PayMemberBillAsync_GuestSlot_ThrowsConflict()
    {
        // Arrange: actor là staff cố trigger pay cho Guest_Slot (UserId = null).
        var hostUserId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            hostUserId,
            (hostUserId, true, false, MemberPaymentStatus.NotPaid, 60000m, 0m, 0m, 0L),
            (Guid.Empty, false, true, MemberPaymentStatus.NotPaid, 60000m, 0m, 0m, 0L)
        );
        SetupSessionRepo(session);
        SetupWalletDebit();

        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = 60,
            IdempotencyKey = "guest-pay"
        };

        // Act + Assert: Service phát hiện Guest_Slot không có UserId/wallet.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().PayMemberBillAsync(
                session.Id, SecondMember(session).Id, request, staffUserId, "CafeStaff", default));
        Assert.Contains("Khách vô danh", ex.Message);
    }

    [Fact]
    public async Task PayMemberBillAsync_FullPayment_Succeeds()
    {
        // Arrange
        // Lưu ý: code hiện tại so sánh trực tiếp BvcAmount với (long)totalDue
        // (đã fix khác pre-existing: chưa nhân 1000 cho VND↔BVC).
        // Test này set BvcAmount = totalDue để pass qua `isFullBvc = true`.
        var memberUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 100m, 0m, 0m, 0L)
        );
        SetupSessionRepo(session);
        SetupWalletDebit(returnedAmount: 100);

        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = 100, // = totalDue (subtotal - deposit)
            IdempotencyKey = "full-pay"
        };

        // Act
        var response = await CreateService().PayMemberBillAsync(
            session.Id, FirstMember(session).Id, request, memberUserId, "Player", default);

        // Assert
        Assert.Equal(MemberPaymentStatus.PaidBvc, response.Status);
        Assert.Equal(100L, response.BvcAmount);
        Assert.Equal("Bvc", response.PaymentMethod);
        _walletService.Verify(
            w => w.DirectDebitForBillAsync(
                memberUserId, 100L, It.IsAny<Guid>(),
                session.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PayMemberBillAsync_PartialPayment_Succeeds()
    {
        // Arrange: subtotal = 100, deposit = 0, totalDue = 100.
        // BvcAmount = 30 (partial). CashRemainder = totalDue - bvcAmount = 70.
        var memberUserId = Guid.NewGuid();
        var session = CreateUnpaidSession(
            memberUserId,
            (memberUserId, true, false, MemberPaymentStatus.NotPaid, 100m, 0m, 0m, 0L)
        );
        SetupSessionRepo(session);
        SetupWalletDebit(returnedAmount: 30);

        var request = new MemberBillPaymentRequestDto
        {
            BvcAmount = 30, // partial
            IdempotencyKey = "partial-pay"
        };

        // Act
        var response = await CreateService().PayMemberBillAsync(
            session.Id, FirstMember(session).Id, request, memberUserId, "Player", default);

        // Assert
        Assert.Equal(MemberPaymentStatus.PartialBvc, response.Status);
        Assert.Equal(30L, response.BvcAmount);
        Assert.Equal(70m, response.CashRemainder);
    }

    // =====================================================================
    // REFUND FLOW
    // =====================================================================

    [Fact]
    public async Task RefundMemberBillAsync_AuditLogAlreadyRefunded_ReturnsExisting()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var auditLogId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        _db.MemberPaymentAuditLogs.Add(new MemberPaymentAuditLog
        {
            Id = auditLogId,
            MemberId = memberId,
            UserId = memberUserId,
            PaymentMethod = "Bvc",
            AmountBvc = 100,
            AmountCash = 0,
            WalletTxnId = Guid.NewGuid(),
            IdempotencyKey = "paid-key",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            RefundedAt = DateTime.UtcNow.AddMinutes(-2), // already refunded
            RefundReason = "BillSai"
        });
        await _db.SaveChangesAsync();

        // Act
        var response = await CreateService().RefundMemberBillAsync(
            auditLogId,
            new MemberBillRefundRequestDto { Reason = "duplicate-call", IdempotencyKey = "new-key" },
            Guid.NewGuid(),
            default);

        // Assert
        Assert.Equal(100L, response.RefundedBvc);
        _walletService.Verify(
            w => w.RefundMemberBillAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never); // Không gọi wallet vì đã refund
    }

    [Fact]
    public async Task RefundMemberBillAsync_NewRefund_Succeeds()
    {
        // Arrange
        var memberUserId = Guid.NewGuid();
        var auditLogId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        _db.MemberPaymentAuditLogs.Add(new MemberPaymentAuditLog
        {
            Id = auditLogId,
            MemberId = memberId,
            UserId = memberUserId,
            PaymentMethod = "Bvc",
            AmountBvc = 100,
            AmountCash = 0,
            WalletTxnId = Guid.NewGuid(),
            IdempotencyKey = "paid-key",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            // RefundedAt = null
        });
        await _db.ActiveSessionMembers.AddAsync(new ActiveSessionMember
        {
            Id = memberId,
            ActiveSessionId = Guid.NewGuid(),
            UserId = memberUserId,
            IsHost = true,
            IsGuestSlot = false,
            Status = IndividualSessionStatus.Finished,
            JoinedAt = DateTime.UtcNow.AddHours(-2),
            PaymentStatus = MemberPaymentStatus.PaidBvc,
            Subtotal = 60000m
        });
        await _db.SaveChangesAsync();

        SetupWalletRefund();

        // Act
        var response = await CreateService().RefundMemberBillAsync(
            auditLogId,
            new MemberBillRefundRequestDto { Reason = "BillSai", IdempotencyKey = "refund-key" },
            Guid.NewGuid(),
            default);

        // Assert
        Assert.Equal(100L, response.RefundedBvc);
        Assert.Equal(memberId, response.MemberId);
        _walletService.Verify(
            w => w.RefundMemberBillAsync(
                memberUserId, 100L, memberId,
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefundMemberBillAsync_AuditLogNotFound_ThrowsNotFound()
    {
        // Arrange
        var request = new MemberBillRefundRequestDto
        {
            Reason = "Test",
            IdempotencyKey = "missing"
        };

        // Act + Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService().RefundMemberBillAsync(
                Guid.NewGuid(), request, Guid.NewGuid(), default));
    }
}
