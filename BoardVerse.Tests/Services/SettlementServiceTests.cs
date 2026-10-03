using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class SettlementServiceTests
{
    private readonly Mock<IBookingDepositRepository> _mockDepositRepo;
    private readonly Mock<ICafeSettlementRepository> _mockSettlementRepo;
    private readonly Mock<ICafeRepository> _mockCafeRepo;
    private readonly Mock<IActiveSessionRepository> _mockSessionRepo;
    private readonly Mock<IBvcLedgerEntryRepository> _mockLedgerRepo;
    private readonly Mock<ISePayClient> _mockSePayClient;
    private readonly Mock<ISePayAccountService> _mockSePayAccountService;
    private readonly Mock<ILogger<SettlementService>> _mockLogger;
    private readonly BoardVerseDbContext _db;
    private readonly SettlementService _service;

    public SettlementServiceTests()
    {
        _mockDepositRepo = new Mock<IBookingDepositRepository>();
        _mockSettlementRepo = new Mock<ICafeSettlementRepository>();
        _mockCafeRepo = new Mock<ICafeRepository>();
        _mockSessionRepo = new Mock<IActiveSessionRepository>();
        _mockLedgerRepo = new Mock<IBvcLedgerEntryRepository>();
        _mockSePayClient = new Mock<ISePayClient>();
        _mockSePayAccountService = new Mock<ISePayAccountService>();
        _mockLogger = new Mock<ILogger<SettlementService>>();

        // Use in-memory database for DbContext
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new BoardVerseDbContext(options);

        _service = new SettlementService(
            _mockDepositRepo.Object,
            _mockSettlementRepo.Object,
            _mockCafeRepo.Object,
            _mockSessionRepo.Object,
            _mockLedgerRepo.Object,
            _mockSePayClient.Object,
            _mockSePayAccountService.Object,
            _mockLogger.Object,
            _db);
    }

    /// <summary>
    /// Build a cafe with SePay destination config (Gap 4 fix).
    /// </summary>
    private static Cafe BuildCafe(Guid cafeId) => new()
    {
        Id = cafeId,
        Name = "Test Cafe",
        Address = "123 St",
        ManagerId = Guid.NewGuid(),
        SePayAccountNumber = "0855199924",
        SePayBankCode = "MBBank"
    };

    #region ReleaseSessionDepositAsync

    [Fact]
    public async Task ReleaseSessionDepositAsync_SessionNotPaid_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            DepositAppliedAmount = 50_000m
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(BuildCafe(cafeId));
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));

        Assert.Contains("đã thanh toán", ex.Message);
    }

    [Fact]
    public async Task ReleaseSessionDepositAsync_NoMasterAccount_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 0
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(BuildCafe(cafeId));
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockSePayAccountService.Setup(s => s.GetRawMasterAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((SePayAccount?)null);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));

        Assert.Contains("Chưa cấu hình master account", ex.Message);
    }

    /// <summary>
    /// Gap 4: Cafe chưa cấu hình SePay bank → throw ConflictException rõ ràng.
    /// </summary>
    [Fact]
    public async Task ReleaseSessionDepositAsync_CafeMissingSePayConfig_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 50_000m
        };

        // Cafe without SePay config
        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 St",
            ManagerId = Guid.NewGuid()
            // SePayAccountNumber/SePayBankCode null intentionally
        });
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockSePayAccountService.Setup(s => s.GetRawMasterAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SePayAccount { Id = Guid.NewGuid(), AccountHolder = "Test", IsActive = true });

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));

        Assert.Contains("SePay", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReleaseSessionDepositAsync_DepositNotFound_ThrowsNotFoundException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 50_000m
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(BuildCafe(cafeId));
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockSePayAccountService.Setup(s => s.GetRawMasterAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SePayAccount { Id = Guid.NewGuid(), AccountHolder = "Test", IsActive = true });
        _mockDepositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));
    }

    [Fact]
    public async Task ReleaseSessionDepositAsync_DepositNotPaid_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 50_000m
        };

        var deposit = new BookingDeposit
        {
            Id = depositId,
            ActiveSessionId = sessionId,
            UserId = Guid.NewGuid(),
            Amount = 50_000m,
            Status = BookingDepositStatus.Pending
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(BuildCafe(cafeId));
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockSePayAccountService.Setup(s => s.GetRawMasterAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SePayAccount { Id = Guid.NewGuid(), AccountHolder = "Test", IsActive = true });
        _mockDepositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));

        Assert.Contains("PAID", ex.Message);
    }

    /// <summary>
    /// Gap 3+4: Transfer succeed → SettlementStatus=Succeeded, deposit.Status=Released.
    /// Destination = cafe bank (not master account).
    /// </summary>
    [Fact]
    public async Task ReleaseSessionDepositAsync_TransferSucceeds_StatusSucceeded()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 50_000m
        };

        var deposit = new BookingDeposit
        {
            Id = depositId,
            ActiveSessionId = sessionId,
            UserId = Guid.NewGuid(),
            Amount = 50_000m,
            Status = BookingDepositStatus.Paid
        };

        var transferResponse = new SePayTransferResponse
        {
            IsSuccess = true,
            TransferId = "TXN-TRANSFER-001"
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(BuildCafe(cafeId));
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockSePayAccountService.Setup(s => s.GetRawMasterAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SePayAccount { Id = Guid.NewGuid(), AccountHolder = "Test", IsActive = true });
        _mockDepositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);
        _mockSePayClient.Setup(c => c.CreateTransferAsync(It.IsAny<CreateTransferRequest>(), default))
            .Callback<CreateTransferRequest, CancellationToken>((req, _) =>
            {
                // Verify Gap 4: destination = cafe bank (not master account)
                Assert.Equal("MBBank", req.ToBankAccount);
                Assert.Equal("0855199924", req.ToAccountNumber);
            })
            .ReturnsAsync(transferResponse);

        var result = await _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId);

        Assert.Equal(CafeSettlementStatus.Succeeded, result.Status);
        Assert.Equal("TXN-TRANSFER-001", result.SePayTransferId);
        Assert.Equal(50_000m, result.DepositAmount);
        Assert.Equal(50_000m, result.NetTransferAmount);
    }

    /// <summary>
    /// Gap 3: Transfer fail → SettlementStatus=Failed, deposit.Status vẫn = Paid (chưa Released)
    /// để SettlementRetryJob có thể retry.
    /// </summary>
    [Fact]
    public async Task ReleaseSessionDepositAsync_TransferFails_StatusFailedDepositStillPaid()
    {
        // Arrange
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();
        var deposit = new BookingDeposit
        {
            Id = depositId,
            ActiveSessionId = sessionId,
            UserId = Guid.NewGuid(),
            Amount = 50_000m,
            Status = BookingDepositStatus.Paid
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSession
            {
                Id = sessionId,
                CafeId = cafeId,
                Status = GroupSessionStatus.Paid,
                DepositAppliedAmount = 50_000m
            });
        _mockSePayAccountService.Setup(s => s.GetRawMasterAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SePayAccount { Id = Guid.NewGuid(), AccountHolder = "Test", IsActive = true });
        _mockDepositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deposit);
        _mockSePayClient.Setup(c => c.CreateTransferAsync(It.IsAny<CreateTransferRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SePay unavailable"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));

        Assert.Contains("SePay unavailable", ex.Message);

        // Verify deposit.Status is still Paid (Gap 3: NOT released so SettlementRetryJob can retry)
        _mockDepositRepo.Verify(
            r => r.UpdateAsync(
                It.Is<BookingDeposit>(d => d.Id == depositId && d.Status == BookingDepositStatus.Paid),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockSettlementRepo.Verify(
            r => r.UpdateAsync(
                It.Is<CafeSettlement>(s => s.Status == CafeSettlementStatus.Failed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region GetPendingSettlementsAsync

    [Fact]
    public async Task GetPendingSettlementsAsync_ReturnsSettlements()
    {
        var cafeId = Guid.NewGuid();

        var settlements = new List<CafeSettlement>
        {
            new CafeSettlement
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                Status = CafeSettlementStatus.Pending,
                DepositAmount = 50_000m
            }
        };

        _mockSettlementRepo.Setup(r => r.GetPendingAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(settlements);
        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(BuildCafe(cafeId));

        var result = await _service.GetPendingSettlementsAsync(cafeId, Guid.NewGuid(), "Admin");

        Assert.Single(result);
        Assert.Equal(CafeSettlementStatus.Pending, result[0].Status);
    }

    #endregion

    #region GetPagedAsync (W-06 list endpoint)

    /// <summary>
    /// W-06: Service chỉ pass-through query tới repo — không thêm logic.
    /// </summary>
    [Fact]
    public async Task GetPagedAsync_DelegatesToRepository_ReturnsRepoResult()
    {
        var query = new SettlementListQuery
        {
            Status = CafeSettlementStatus.Failed,
            PageNumber = 1,
            PageSize = 10
        };

        var expected = new PaginatedResponse<SettlementListItemDto>
        {
            Data = new List<SettlementListItemDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    CafeId = Guid.NewGuid(),
                    CafeName = "Cafe A",
                    Status = CafeSettlementStatus.Failed,
                    DepositAmount = 50_000m,
                    NetTransferAmount = 50_000m,
                    FailureReason = "SePay timeout",
                    RetryCount = 5
                }
            },
            Meta = new PaginationMeta
            {
                CurrentPage = 1,
                PageSize = 10,
                TotalItems = 1,
                TotalPages = 1
            }
        };

        _mockSettlementRepo
            .Setup(r => r.GetPagedAsync(It.Is<SettlementListQuery>(q =>
                q.Status == CafeSettlementStatus.Failed && q.PageNumber == 1 && q.PageSize == 10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _service.GetPagedAsync(query);

        Assert.Same(expected, result);
        Assert.Single(result.Data);
        Assert.Equal("SePay timeout", result.Data.First().FailureReason);
        _mockSettlementRepo.Verify(r => r.GetPagedAsync(It.IsAny<SettlementListQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// W-06: Empty result (không có settlement nào Failed) trả về paginated response rỗng.
    /// </summary>
    [Fact]
    public async Task GetPagedAsync_NoMatchingSettlements_ReturnsEmptyPagination()
    {
        var query = new SettlementListQuery
        {
            Status = CafeSettlementStatus.Failed,
            CafeId = Guid.NewGuid()
        };

        var empty = new PaginatedResponse<SettlementListItemDto>
        {
            Data = new List<SettlementListItemDto>(),
            Meta = new PaginationMeta
            {
                CurrentPage = 1,
                PageSize = 20,
                TotalItems = 0,
                TotalPages = 0
            }
        };

        _mockSettlementRepo
            .Setup(r => r.GetPagedAsync(It.IsAny<SettlementListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(empty);

        var result = await _service.GetPagedAsync(query);

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Meta.TotalItems);
        Assert.Equal(0, result.Meta.TotalPages);
        Assert.False(result.Meta.HasNext);
        Assert.False(result.Meta.HasPrevious);
    }

    /// <summary>
    /// W-06: All filters (status, cafeId, cafeManagerId, fromUtc, toUtc) đều được forward tới repo.
    /// </summary>
    [Fact]
    public async Task GetPagedAsync_ForwardsAllFiltersToRepository()
    {
        var cafeId = Guid.NewGuid();
        var cafeManagerId = Guid.NewGuid();
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;

        var query = new SettlementListQuery
        {
            Status = CafeSettlementStatus.Failed,
            CafeId = cafeId,
            CafeManagerId = cafeManagerId,
            FromUtc = from,
            ToUtc = to,
            PageNumber = 2,
            PageSize = 50
        };

        SettlementListQuery? captured = null;
        _mockSettlementRepo
            .Setup(r => r.GetPagedAsync(It.IsAny<SettlementListQuery>(), It.IsAny<CancellationToken>()))
            .Callback<SettlementListQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new PaginatedResponse<SettlementListItemDto>());

        await _service.GetPagedAsync(query);

        Assert.NotNull(captured);
        Assert.Equal(CafeSettlementStatus.Failed, captured!.Status);
        Assert.Equal(cafeId, captured.CafeId);
        Assert.Equal(cafeManagerId, captured.CafeManagerId);
        Assert.Equal(from, captured.FromUtc);
        Assert.Equal(to, captured.ToUtc);
        Assert.Equal(2, captured.PageNumber);
        Assert.Equal(50, captured.PageSize);
    }

    #endregion

    #region GetDailySummaryAsync (W-07 daily summary)

    /// <summary>
    /// Helper: tạo cafe entity trong DbContext (in-memory) để service join được.
    /// </summary>
    private static Cafe BuildCafeDbEntity(Guid cafeId, string name, string? bankCode, string? accountNumber) => new()
    {
        Id = cafeId,
        Name = name,
        Address = "123 St",
        ManagerId = Guid.NewGuid(),
        SePayBankCode = bankCode,
        SePayAccountNumber = accountNumber
    };

    /// <summary>
    /// W-07: Không truyền date → mặc định lấy "hôm nay" theo giờ VN (UTC+7).
    /// Service gọi repo với UTC range = [today 00:00 VN, tomorrow 00:00 VN).
    /// </summary>
    [Fact]
    public async Task GetDailySummaryAsync_NoDate_DefaultsToTodayVietnam()
    {
        var cafeId = Guid.NewGuid();
        var settlement = new CafeSettlement
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            Status = CafeSettlementStatus.Succeeded,
            DepositAmount = 50_000m,
            NetTransferAmount = 50_000m,
            CreatedAt = DateTime.UtcNow,
            TransferredAt = DateTime.UtcNow
        };

        _db.Cafes.Add(BuildCafeDbEntity(cafeId, "Cafe Test", "MBBank", "0855199924"));
        await _db.SaveChangesAsync();

        DateTime? capturedStart = null;
        DateTime? capturedEnd = null;
        _mockSettlementRepo
            .Setup(r => r.GetForDailySummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, CancellationToken>((s, e, _) =>
            {
                capturedStart = s;
                capturedEnd = e;
            })
            .ReturnsAsync(new List<CafeSettlement> { settlement });

        var result = await _service.GetDailySummaryAsync();

        Assert.NotNull(capturedStart);
        Assert.NotNull(capturedEnd);
        // Khoảng cách giữa start và end = đúng 24 giờ.
        Assert.Equal(TimeSpan.FromHours(24), capturedEnd!.Value - capturedStart!.Value);
        Assert.Equal(cafeId, result.Cafes[0].CafeId);
        Assert.Equal("Cafe Test", result.Cafes[0].CafeName);
        Assert.Equal(50_000m, result.Cafes[0].TotalTransferred);
        Assert.Equal(50_000m, result.Cafes[0].TotalToTransfer);
        Assert.Equal(1, result.TotalSettlementCount);
    }

    /// <summary>
    /// W-07: Có 1 cafe với nhiều settlement (Succeeded + Pending + Failed) → breakdown đúng từng status.
    /// </summary>
    [Fact]
    public async Task GetDailySummaryAsync_GroupsByCafeAndBreaksDownByStatus()
    {
        var cafeId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var settlements = new List<CafeSettlement>
        {
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Status = CafeSettlementStatus.Succeeded, DepositAmount = 50_000m, NetTransferAmount = 50_000m, CreatedAt = now, TransferredAt = now },
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Status = CafeSettlementStatus.Succeeded, DepositAmount = 50_000m, NetTransferAmount = 50_000m, CreatedAt = now, TransferredAt = now },
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Status = CafeSettlementStatus.Pending, DepositAmount = 30_000m, NetTransferAmount = 30_000m, CreatedAt = now },
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Status = CafeSettlementStatus.Failed, DepositAmount = 20_000m, NetTransferAmount = 20_000m, CreatedAt = now, FailureReason = "SePay timeout" }
        };

        _db.Cafes.Add(BuildCafeDbEntity(cafeId, "Cafe XYZ", "VCB", "1234567890"));
        await _db.SaveChangesAsync();

        _mockSettlementRepo
            .Setup(r => r.GetForDailySummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlements);

        var result = await _service.GetDailySummaryAsync(new DateOnly(2026, 10, 3));

        var cafeSummary = result.Cafes.Single(c => c.CafeId == cafeId);
        Assert.Equal("Cafe XYZ", cafeSummary.CafeName);
        Assert.Equal(4, cafeSummary.TotalCount);
        Assert.Equal(150_000m, cafeSummary.TotalDepositAmount); // 50+50+30+20
        Assert.Equal(100_000m, cafeSummary.TotalTransferred); // 50+50 (Succeeded)
        Assert.Equal(30_000m, cafeSummary.TotalPending); // Pending 30
        Assert.Equal(20_000m, cafeSummary.TotalFailed); // Failed 20
        Assert.Equal(130_000m, cafeSummary.TotalToTransfer); // 100+30 (Succeeded+Pending)

        // ByStatus: đủ 5 status (Pending/Succeeded/Failed/Retrying/Overridden) với count khớp.
        Assert.Equal(5, cafeSummary.ByStatus.Count);
        Assert.Equal(2, cafeSummary.ByStatus.Single(b => b.Status == CafeSettlementStatus.Succeeded).Count);
        Assert.Equal(1, cafeSummary.ByStatus.Single(b => b.Status == CafeSettlementStatus.Pending).Count);
        Assert.Equal(1, cafeSummary.ByStatus.Single(b => b.Status == CafeSettlementStatus.Failed).Count);
        Assert.Equal(0, cafeSummary.ByStatus.Single(b => b.Status == CafeSettlementStatus.Retrying).Count);
        Assert.Equal(0, cafeSummary.ByStatus.Single(b => b.Status == CafeSettlementStatus.Overridden).Count);
        Assert.Equal(4, cafeSummary.SettlementIds.Count);
    }

    /// <summary>
    /// W-07: Grand totals cộng đúng từ nhiều cafe, sort theo TotalToTransfer DESC.
    /// </summary>
    [Fact]
    public async Task GetDailySummaryAsync_AggregatesGrandTotalsAndSortsByTotalDesc()
    {
        var cafeA = Guid.NewGuid();
        var cafeB = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Cafe B có số tiền nhỏ hơn A → sắp xếp A lên đầu.
        var settlements = new List<CafeSettlement>
        {
            new() { Id = Guid.NewGuid(), CafeId = cafeA, Status = CafeSettlementStatus.Succeeded, DepositAmount = 100_000m, NetTransferAmount = 100_000m, CreatedAt = now, TransferredAt = now },
            new() { Id = Guid.NewGuid(), CafeId = cafeB, Status = CafeSettlementStatus.Succeeded, DepositAmount = 20_000m, NetTransferAmount = 20_000m, CreatedAt = now, TransferredAt = now }
        };

        _db.Cafes.AddRange(
            BuildCafeDbEntity(cafeA, "Cafe A", "VCB", "111"),
            BuildCafeDbEntity(cafeB, "Cafe B", "MB", "222"));
        await _db.SaveChangesAsync();

        _mockSettlementRepo
            .Setup(r => r.GetForDailySummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlements);

        var result = await _service.GetDailySummaryAsync(new DateOnly(2026, 10, 3));

        Assert.Equal(2, result.CafeCount);
        Assert.Equal(2, result.TotalSettlementCount);
        Assert.Equal(120_000m, result.GrandTotalDeposit);
        Assert.Equal(120_000m, result.GrandTotalTransferred);
        Assert.Equal(120_000m, result.GrandTotalToTransfer);

        // Sort: TotalToTransfer DESC → Cafe A (100k) trước Cafe B (20k).
        Assert.Equal(cafeA, result.Cafes[0].CafeId);
        Assert.Equal(cafeB, result.Cafes[1].CafeId);
    }

    /// <summary>
    /// W-07: Trả về rỗng khi không có settlement nào trong ngày.
    /// </summary>
    [Fact]
    public async Task GetDailySummaryAsync_NoSettlements_ReturnsEmptySummary()
    {
        _mockSettlementRepo
            .Setup(r => r.GetForDailySummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CafeSettlement>());

        var result = await _service.GetDailySummaryAsync(new DateOnly(2026, 10, 3));

        Assert.Equal("2026-10-03", result.Date);
        Assert.Equal(0, result.CafeCount);
        Assert.Empty(result.Cafes);
        Assert.Equal(0m, result.GrandTotalToTransfer);
        Assert.Equal(0m, result.GrandTotalFailed);
    }

    /// <summary>
    /// W-07: Truyền date cụ thể → repo nhận UTC range đúng cho ngày đó ở giờ VN.
    /// Ví dụ: 2026-10-03 ở VN (UTC+7) = [2026-10-02 17:00:00Z, 2026-10-03 17:00:00Z).
    /// </summary>
    [Fact]
    public async Task GetDailySummaryAsync_ExplicitDate_BuildsCorrectUtcRange()
    {
        DateTime? capturedStart = null;
        DateTime? capturedEnd = null;
        _mockSettlementRepo
            .Setup(r => r.GetForDailySummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, CancellationToken>((s, e, _) =>
            {
                capturedStart = s;
                capturedEnd = e;
            })
            .ReturnsAsync(Array.Empty<CafeSettlement>());

        await _service.GetDailySummaryAsync(new DateOnly(2026, 10, 3));

        Assert.NotNull(capturedStart);
        Assert.NotNull(capturedEnd);
        // VN 2026-10-03 00:00 = UTC 2026-10-02 17:00.
        Assert.Equal(new DateTime(2026, 10, 2, 17, 0, 0, DateTimeKind.Utc), capturedStart!.Value.ToUniversalTime());
        // VN 2026-10-04 00:00 = UTC 2026-10-03 17:00.
        Assert.Equal(new DateTime(2026, 10, 3, 17, 0, 0, DateTimeKind.Utc), capturedEnd!.Value.ToUniversalTime());
    }

    /// <summary>
    /// W-07: Cafe entity không tồn tại trong DB (orphan settlement) → vẫn group được,
    /// CafeName = null, SePay info = null.
    /// </summary>
    [Fact]
    public async Task GetDailySummaryAsync_OrphanSettlement_GroupsWithNullCafeName()
    {
        var orphanCafeId = Guid.NewGuid();
        var settlement = new CafeSettlement
        {
            Id = Guid.NewGuid(),
            CafeId = orphanCafeId,
            Status = CafeSettlementStatus.Failed,
            DepositAmount = 10_000m,
            NetTransferAmount = 10_000m,
            CreatedAt = DateTime.UtcNow,
            FailureReason = "Test"
        };

        // Không add cafe vào DbContext → join sẽ trả null.
        _mockSettlementRepo
            .Setup(r => r.GetForDailySummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeSettlement> { settlement });

        var result = await _service.GetDailySummaryAsync(new DateOnly(2026, 10, 3));

        var cafeSummary = result.Cafes.Single(c => c.CafeId == orphanCafeId);
        Assert.Null(cafeSummary.CafeName);
        Assert.Null(cafeSummary.SePayBankCode);
        Assert.Null(cafeSummary.SePayAccountNumber);
        Assert.Equal(10_000m, cafeSummary.TotalFailed);
    }

    /// <summary>
    /// W-07: Overridden settlement vẫn được tính vào TotalToTransfer (admin đã xử lý thủ công).
    /// </summary>
    [Fact]
    public async Task GetDailySummaryAsync_OverriddenSettlement_CountedInTotalToTransfer()
    {
        var cafeId = Guid.NewGuid();
        var settlement = new CafeSettlement
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            Status = CafeSettlementStatus.Overridden,
            DepositAmount = 80_000m,
            NetTransferAmount = 80_000m,
            CreatedAt = DateTime.UtcNow,
            TransferredAt = DateTime.UtcNow,
            OverrideBy = Guid.NewGuid(),
            OverrideAt = DateTime.UtcNow
        };

        _db.Cafes.Add(BuildCafeDbEntity(cafeId, "Cafe Override", "VCB", "999"));
        await _db.SaveChangesAsync();

        _mockSettlementRepo
            .Setup(r => r.GetForDailySummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeSettlement> { settlement });

        var result = await _service.GetDailySummaryAsync(new DateOnly(2026, 10, 3));

        var cafeSummary = result.Cafes.Single(c => c.CafeId == cafeId);
        Assert.Equal(80_000m, cafeSummary.TotalOverridden);
        Assert.Equal(80_000m, cafeSummary.TotalToTransfer); // Overridden vẫn tính
        Assert.Equal(0m, cafeSummary.TotalTransferred); // Overridden KHÔNG tính vào Succeeded
    }

    #endregion
}
