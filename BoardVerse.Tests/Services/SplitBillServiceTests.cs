using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class SplitBillServiceTests : IDisposable
{
    private readonly BoardVerseDbContext _dbContext;
    private readonly Mock<IActiveSessionRepository> _sessionRepoMock;
    private readonly Mock<ITransactionRepository> _transactionRepoMock;
    private readonly Mock<ICafeRepository> _cafeRepoMock;
    private readonly Mock<IPaymentGatewayService> _gatewayMock;
    private readonly Mock<ILogger<SplitBillService>> _loggerMock;
    private readonly Mock<IPaymentWebhookAuditRepository> _webhookAuditRepoMock;
    private readonly SplitBillService _service;

    public SplitBillServiceTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new BoardVerseDbContext(options);
        _sessionRepoMock = new Mock<IActiveSessionRepository>();
        _transactionRepoMock = new Mock<ITransactionRepository>();
        _cafeRepoMock = new Mock<ICafeRepository>();
        _gatewayMock = new Mock<IPaymentGatewayService>();
        _loggerMock = new Mock<ILogger<SplitBillService>>();
        _webhookAuditRepoMock = new Mock<IPaymentWebhookAuditRepository>();

        _service = new SplitBillService(
            _sessionRepoMock.Object,
            _transactionRepoMock.Object,
            _cafeRepoMock.Object,
            _gatewayMock.Object,
            _dbContext,
            _loggerMock.Object,
            _webhookAuditRepoMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region GetSessionPaymentStatusAsync Tests

    [Fact]
    public async Task GetSessionPaymentStatusAsync_WhenSessionNotFound_ThrowsNotFoundException()
    {
        var sessionId = Guid.NewGuid();
        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveSession?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetSessionPaymentStatusAsync(sessionId));
    }

    [Fact]
    public async Task GetSessionPaymentStatusAsync_ReturnsCorrectPaymentStatus()
    {
        var sessionId = Guid.NewGuid();
        var member1Id = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var member3Id = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            TotalAmount = 300000m,
            Members = new List<ActiveSessionMember>
            {
                new() { Id = member1Id, TotalAmount = 100000m, PaymentStatus = MemberPaymentStatus.NotPaid, Status = IndividualSessionStatus.Playing },
                new() { Id = member2Id, TotalAmount = 100000m, PaymentStatus = MemberPaymentStatus.PaidCash, PaymentMethod = "CASH", Status = IndividualSessionStatus.Playing },
                new() { Id = member3Id, TotalAmount = 100000m, PaymentStatus = MemberPaymentStatus.PaidQr, PaymentMethod = "QR_CODE", Status = IndividualSessionStatus.Playing }
            }
        };

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await _service.GetSessionPaymentStatusAsync(sessionId);

        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(300000m, result.TotalAmount);
        Assert.Equal(200000m, result.TotalPaid); // member2 + member3 đã trả
        Assert.Equal(100000m, result.TotalRemaining);
        Assert.Equal(3, result.Members.Count);
        Assert.Equal(2, result.Members.Count(m => m.Status != MemberPaymentStatus.NotPaid));
    }

    #endregion

    #region PayMembersAsync Tests

    [Fact]
    public async Task PayMembersAsync_WhenSessionNotUnpaid_ThrowsConflictException()
    {
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Active,
            CafeId = cafeId,
            Members = new List<ActiveSessionMember>
            {
                new() { Id = Guid.NewGuid(), TotalAmount = 100000m, PaymentStatus = MemberPaymentStatus.NotPaid, Status = IndividualSessionStatus.Playing }
            }
        };

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test", Address = "addr", ManagerId = staffId });
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new PayMemberRequestDto
        {
            MemberIds = [Guid.NewGuid()],
            PaymentMethod = "CASH"
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.PayMembersAsync(sessionId, request, staffId, "Manager"));
    }

    [Fact]
    public async Task PayMembersAsync_InvalidPaymentMethod_ThrowsBadRequestException()
    {
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeId = cafeId,
            Members = new List<ActiveSessionMember>
            {
                new() { Id = Guid.NewGuid(), TotalAmount = 100000m, PaymentStatus = MemberPaymentStatus.NotPaid, Status = IndividualSessionStatus.Playing }
            }
        };

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test", Address = "addr", ManagerId = staffId });
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new PayMemberRequestDto
        {
            MemberIds = [Guid.NewGuid()],
            PaymentMethod = "INVALID_METHOD"
        };

        // Fix #5: ArgumentException → BadRequestException
        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.PayMembersAsync(sessionId, request, staffId, "Manager"));
    }

    [Fact]
    public async Task PayMembersAsync_WhenMemberAlreadyPaid_ThrowsConflictException()
    {
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            PaymentStatus = MemberPaymentStatus.PaidCash,
            TotalAmount = 100000m,
            Status = IndividualSessionStatus.Playing,
            User = new User { Username = "testuser", Email = "test@test.com" }
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeId = cafeId,
            Members = new List<ActiveSessionMember> { member }
        };

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test", Address = "addr", ManagerId = staffId });
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new PayMemberRequestDto
        {
            MemberIds = [memberId],
            PaymentMethod = "CASH"
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.PayMembersAsync(sessionId, request, staffId, "Manager"));
    }

    [Fact]
    public async Task PayMembersAsync_CashPayment_UpdatesMemberPaymentStatus()
    {
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            TotalAmount = 100000m,
            PaymentStatus = MemberPaymentStatus.NotPaid,
            Status = IndividualSessionStatus.Playing
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeId = cafeId,
            Members = new List<ActiveSessionMember> { member }
        };

        // InMemory: add entities so DbContext tracks them for ExecuteUpdate fallback
        await _dbContext.ActiveSessions.AddAsync(session);
        await _dbContext.ActiveSessionMembers.AddAsync(member);
        await _dbContext.SaveChangesAsync();

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test", Address = "addr", ManagerId = staffId });
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _transactionRepoMock.Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

        var request = new PayMemberRequestDto
        {
            MemberIds = [memberId],
            PaymentMethod = "CASH"
        };

        var result = await _service.PayMembersAsync(sessionId, request, staffId, "Manager");

        Assert.Single(result);
        Assert.Equal(memberId, result[0].MemberId);
        Assert.Equal(MemberPaymentStatus.PaidCash, result[0].Status);
        Assert.Equal("CASH", result[0].PaymentMethod);
        Assert.Equal(100000m, result[0].AmountPaid);
    }

    [Fact]
    public async Task PayMembersAsync_QrPayment_CreatesQrForEachMember()
    {
        var sessionId = Guid.NewGuid();
        var member1Id = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var members = new List<ActiveSessionMember>
        {
            new() { Id = member1Id, TotalAmount = 100000m, PaymentStatus = MemberPaymentStatus.NotPaid, Status = IndividualSessionStatus.Playing },
            new() { Id = member2Id, TotalAmount = 150000m, PaymentStatus = MemberPaymentStatus.NotPaid, Status = IndividualSessionStatus.Playing }
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeId = cafeId,
            Members = members
        };

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test Street",
            SePayBankCode = "MB",
            SePayAccountNumber = "123456789",
            ManagerId = Guid.NewGuid()
        };

        // InMemory: add entities so DbContext tracks them for ExecuteUpdate fallback
        await _dbContext.ActiveSessions.AddAsync(session);
        await _dbContext.ActiveSessionMembers.AddRangeAsync(members);
        await _dbContext.SaveChangesAsync();

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cafe);
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _gatewayMock.Setup(g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayResult
            {
                IsSuccess = true,
                QrImageUrl = "https://qr.vietqr.io/test",
                OrderId = "BV-TEST"
            });

        var request = new PayMemberRequestDto
        {
            MemberIds = [member1Id, member2Id],
            PaymentMethod = "QR_CODE"
        };

        var result = await _service.PayMembersAsync(sessionId, request, staffId, "Manager");

        Assert.Equal(2, result.Count);
        Assert.All(result, r =>
        {
            Assert.Equal(MemberPaymentStatus.NotPaid, r.Status);
            Assert.Equal("QR_CODE", r.PaymentMethod);
            Assert.NotNull(r.QrImageUrl);
            // Fix #2: OrderId format = BV-MEMBER-{32-char-N-format-Guid}
            Assert.StartsWith("BV-MEMBER-", r.OrderId);
            // N-format Guid = 32 hex chars, không có dashes
            Assert.Equal(10 + 32, r.OrderId!.Length);
            // Fix #2: TransferContent = OrderId (khớp nhau)
            Assert.Equal(r.OrderId, r.TransferContent);
        });
    }

    #endregion

    #region CreateMemberQrAsync Tests

    [Fact]
    public async Task CreateMemberQrAsync_WhenCafeMissingSePayConfig_ThrowsException()
    {
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeId = cafeId,
            Members = new List<ActiveSessionMember>
            {
                new() { Id = memberId, TotalAmount = 100000m }
            }
        };

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test Street",
            SePayBankCode = null,
            ManagerId = Guid.NewGuid()
        };

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cafe);
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateMemberQrAsync(sessionId, memberId, staffId, "Manager"));
    }

    #endregion

    #region ConfirmMemberCashAsync Tests

    [Fact]
    public async Task ConfirmMemberCashAsync_WhenAmountMismatch_ThrowsConflictException()
    {
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            TotalAmount = 100000m,
            PaymentStatus = MemberPaymentStatus.NotPaid
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeId = cafeId,
            Members = new List<ActiveSessionMember> { member }
        };

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test", Address = "addr", ManagerId = staffId });
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Fix #8: Amount comparison với tolerance — 50000 ≠ 100000 → reject
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ConfirmMemberCashAsync(sessionId, memberId, 50000m, staffId, "Manager"));
    }

    #endregion

    #region ProcessMemberQrWebhookAsync Tests

    [Fact]
    public async Task ProcessMemberQrWebhookAsync_WhenMemberNotFound_ReturnsSilently_AndRecordsAudit()
    {
        // Fix #6: ProcessMemberQrWebhookAsync returns 200 silently (no throw) when member not found
        // to prevent SePay from retrying indefinitely. Audit record is still saved for observability.
        var memberId = Guid.NewGuid();

        _sessionRepoMock.Setup(r => r.GetByMemberIdWithSessionAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveSession?)null);

        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = memberId,
            Amount = 100000m,
            Status = "success"
        };

        // Should NOT throw — silent return
        await _service.ProcessMemberQrWebhookAsync(webhook);

        // Verify audit record was saved
        _webhookAuditRepoMock.Verify(r => r.AddAsync(It.IsAny<PaymentWebhookAudit>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessMemberQrWebhookAsync_WhenMemberAlreadyPaid_SkipsProcessing()
    {
        var memberId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            PaymentStatus = MemberPaymentStatus.PaidCash,
            TotalAmount = 100000m
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Members = new List<ActiveSessionMember> { member }
        };

        // Fix #1: Use GetByMemberIdWithSessionAsync
        _sessionRepoMock.Setup(r => r.GetByMemberIdWithSessionAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = memberId,
            Amount = 100000m,
            Status = "success"
        };

        // Should not throw — just skip
        await _service.ProcessMemberQrWebhookAsync(webhook);
    }

    [Fact]
    public async Task ProcessMemberQrWebhookAsync_WhenAmountMismatch_ReturnsSilently_AndRecordsAudit()
    {
        // Fix #6: Amount mismatch returns silently (no throw ConflictException) to prevent
        // SePay from retrying. Audit record is saved for observability.
        var memberId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            PaymentStatus = MemberPaymentStatus.NotPaid,
            TotalAmount = 100000m
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Members = new List<ActiveSessionMember> { member }
        };

        _sessionRepoMock.Setup(r => r.GetByMemberIdWithSessionAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = memberId,
            Amount = 50000m, // Mismatch with member.TotalAmount (100000)
            Status = "success"
        };

        // Should NOT throw — silent return
        await _service.ProcessMemberQrWebhookAsync(webhook);

        // Member payment status should remain NotPaid
        Assert.Equal(MemberPaymentStatus.NotPaid, member.PaymentStatus);

        // Verify audit record was saved with success=false
        _webhookAuditRepoMock.Verify(r => r.AddAsync(It.IsAny<PaymentWebhookAudit>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessMemberQrWebhookAsync_WhenOrderIdParse_FindsCorrectMember()
    {
        var memberId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            PaymentStatus = MemberPaymentStatus.NotPaid,
            TotalAmount = 100000m
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Members = new List<ActiveSessionMember> { member }
        };

        // InMemory: add entities so DbContext tracks them for ExecuteUpdate fallback
        await _dbContext.ActiveSessions.AddAsync(session);
        await _dbContext.ActiveSessionMembers.AddAsync(member);
        await _dbContext.SaveChangesAsync();

        _sessionRepoMock.Setup(r => r.GetByMemberIdWithSessionAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Fix #2: Full OrderId format
        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = Guid.Empty, // MemberId empty → parse from OrderId
            OrderId = $"BV-MEMBER-{memberId:N}",
            Amount = 100000m,
            Status = "success"
        };

        // Should not throw — member found and processed
        await _service.ProcessMemberQrWebhookAsync(webhook);
    }

    #endregion

    #region ConfirmMemberQrAsync Tests

    [Fact]
    public async Task ConfirmMemberQrAsync_WhenMemberChoseCash_ThrowsConflictException()
    {
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            TotalAmount = 100000m,
            PaymentStatus = MemberPaymentStatus.NotPaid,
            PaymentMethod = "CASH",
            Status = IndividualSessionStatus.Playing
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeId = cafeId,
            Members = new List<ActiveSessionMember> { member }
        };

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test", Address = "addr", ManagerId = staffId });
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Fix #7: Member đã chọn CASH → không được confirm QR
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ConfirmMemberQrAsync(sessionId, memberId, staffId, "Manager"));
    }

    #endregion

    #region RegenerateMemberQrAsync Tests

    [Fact]
    public async Task RegenerateMemberQrAsync_WhenMemberNotChosenQr_ThrowsConflictException()
    {
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            TotalAmount = 100000m,
            PaymentStatus = MemberPaymentStatus.NotPaid,
            PaymentMethod = "CASH",
            Status = IndividualSessionStatus.Playing
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeId = cafeId,
            Members = new List<ActiveSessionMember> { member }
        };

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test", Address = "addr", ManagerId = staffId });
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Fix #11: Regenerate chỉ khi đã chọn QR_CODE
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.RegenerateMemberQrAsync(sessionId, memberId, staffId, "Manager"));
    }

    #endregion

    #region AllMembersPaid Tests

    [Fact]
    public async Task PayMembersAsync_WhenAllMembersPaid_SessionBecomesPaid()
    {
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            TotalAmount = 100000m,
            PaymentStatus = MemberPaymentStatus.NotPaid,
            Status = IndividualSessionStatus.Playing
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeId = cafeId,
            Members = new List<ActiveSessionMember> { member }
        };

        // InMemory: add entities so DbContext tracks them for ExecuteUpdate fallback
        await _dbContext.ActiveSessions.AddAsync(session);
        await _dbContext.ActiveSessionMembers.AddAsync(member);
        await _dbContext.SaveChangesAsync();

        _sessionRepoMock.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _sessionRepoMock.Setup(r => r.UpdateAsync(It.IsAny<ActiveSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sessionRepoMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sessionRepoMock.Setup(r => r.ReleaseMembersAndCloseLobbyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sessionRepoMock.Setup(r => r.ReleaseSessionTableAndBoxAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cafeRepoMock.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test", Address = "addr", ManagerId = staffId });
        _cafeRepoMock.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _transactionRepoMock.Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

        var request = new PayMemberRequestDto
        {
            MemberIds = [memberId],
            PaymentMethod = "CASH"
        };

        await _service.PayMembersAsync(sessionId, request, staffId, "Manager");

        // Verify session status transitioned to Paid (via InMemory direct update)
        var updatedSession = await _dbContext.ActiveSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        Assert.NotNull(updatedSession);
        Assert.Equal(GroupSessionStatus.Paid, updatedSession.Status);
    }

    #endregion
}
