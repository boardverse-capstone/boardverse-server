using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class BookingDepositServiceTests
{
    private readonly Mock<IBookingDepositRepository> _mockDepositRepo;
    private readonly Mock<ICafeRepository> _mockCafeRepo;
    private readonly Mock<ILogger<BookingDepositService>> _mockLogger;
    private readonly BookingDepositService _service;

    public BookingDepositServiceTests()
    {
        _mockDepositRepo = new Mock<IBookingDepositRepository>();
        _mockCafeRepo = new Mock<ICafeRepository>();
        _mockLogger = new Mock<ILogger<BookingDepositService>>();

        _service = new BookingDepositService(
            _mockDepositRepo.Object,
            _mockCafeRepo.Object,
            _mockLogger.Object);
    }

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_CafeNotFound_ThrowsNotFoundException()
    {
        var cafeId = Guid.NewGuid();
        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync((Cafe?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateAsync(
                activeSessionId: Guid.NewGuid(),
                cafeId: cafeId,
                cafeManagerId: Guid.NewGuid(),
                amount: 50_000m,
                refundPolicy: DepositRefundPolicy.Full));

        Assert.Contains("quán", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AmountExceedsBr03Cap_ThrowsBadRequestException()
    {
        var cafeId = Guid.NewGuid();
        var cafe = CreateTestCafe(cafeId, basePrice: 100_000m); // max deposit = 50,000đ

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(cafe);

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(
                activeSessionId: Guid.NewGuid(),
                cafeId: cafeId,
                cafeManagerId: Guid.NewGuid(),
                amount: 75_000m, // exceeds 50,000đ cap
                refundPolicy: DepositRefundPolicy.Full));

        Assert.Contains("50%", ex.Message);
        Assert.Contains("BR-03", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AmountZeroOrNegative_ThrowsBadRequestException()
    {
        var cafeId = Guid.NewGuid();
        var cafe = CreateTestCafe(cafeId, basePrice: 100_000m);

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(cafe);

        var exZero = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(
                activeSessionId: Guid.NewGuid(),
                cafeId: cafeId,
                cafeManagerId: Guid.NewGuid(),
                amount: 0m,
                refundPolicy: DepositRefundPolicy.Full));

        Assert.Contains("lớn hơn 0", exZero.Message);
    }

    [Fact]
    public async Task CreateAsync_AmountAtExactlyFiftyPercent_IsAllowed()
    {
        var cafeId = Guid.NewGuid();
        var cafeId2 = Guid.NewGuid();
        var cafe = CreateTestCafe(cafeId, basePrice: 100_000m); // max deposit = 50,000đ

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(cafe);
        _mockDepositRepo.Setup(r => r.AddAsync(It.IsAny<BookingDeposit>())).Returns(Task.CompletedTask);
        _mockDepositRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var result = await _service.CreateAsync(
            activeSessionId: Guid.NewGuid(),
            cafeId: cafeId,
            cafeManagerId: Guid.NewGuid(),
            amount: 50_000m, // exactly 50%
            refundPolicy: DepositRefundPolicy.Full);

        Assert.NotNull(result);
        Assert.Equal(50_000m, result.Amount);
        _mockDepositRepo.Verify(r => r.AddAsync(It.IsAny<BookingDeposit>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesDepositWithCorrectStatus()
    {
        var cafeId = Guid.NewGuid();
        var cafe = CreateTestCafe(cafeId, basePrice: 100_000m);
        var activeSessionId = Guid.NewGuid();
        var cafeManagerId = Guid.NewGuid();
        var amount = 30_000m;
        var scheduledAt = DateTime.UtcNow.AddHours(2);

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(cafe);
        _mockDepositRepo.Setup(r => r.AddAsync(It.IsAny<BookingDeposit>())).Returns(Task.CompletedTask);
        _mockDepositRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var result = await _service.CreateAsync(
            activeSessionId: activeSessionId,
            cafeId: cafeId,
            cafeManagerId: cafeManagerId,
            amount: amount,
            refundPolicy: DepositRefundPolicy.Full,
            scheduledAt: scheduledAt);

        Assert.Equal(activeSessionId, result.ActiveSessionId);
        Assert.Equal(cafeId, result.CafeId);
        Assert.Equal(cafeManagerId, result.CafeManagerId);
        Assert.Equal(amount, result.Amount);
        Assert.Equal(BookingDepositStatus.Pending, result.Status);
        Assert.Equal(DepositRefundPolicy.Full, result.RefundPolicy);
        Assert.Equal(scheduledAt, result.ScheduledAt);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    #endregion

    #region MarkAsPaidAsync

    [Fact]
    public async Task MarkAsPaidAsync_DepositNotFound_ThrowsNotFoundException()
    {
        var depositId = Guid.NewGuid();
        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync((BookingDeposit?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.MarkAsPaidAsync(depositId));

        Assert.Contains("deposit", ex.Message.ToLower());
    }

    [Fact]
    public async Task MarkAsPaidAsync_AlreadyPaid_ReturnsDepositWithoutUpdate()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        var result = await _service.MarkAsPaidAsync(depositId);

        Assert.Equal(BookingDepositStatus.Paid, result.Status);
        _mockDepositRepo.Verify(r => r.UpdateAsync(It.IsAny<BookingDeposit>()), Times.Never);
    }

    [Fact]
    public async Task MarkAsPaidAsync_NotPending_ThrowsConflictException()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Refunded);

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.MarkAsPaidAsync(depositId));

        Assert.Contains("'Refunded'", ex.Message);
    }

    [Fact]
    public async Task MarkAsPaidAsync_ValidRequest_UpdatesStatusToPaid()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);
        var sePayTxnId = "TXN-123";

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);
        _mockDepositRepo.Setup(r => r.UpdateAsync(It.IsAny<BookingDeposit>())).Returns(Task.CompletedTask);
        _mockDepositRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var result = await _service.MarkAsPaidAsync(depositId, sePayTxnId);

        Assert.Equal(BookingDepositStatus.Paid, result.Status);
        Assert.NotNull(result.PaidAt);
        Assert.Equal(sePayTxnId, result.SePayTransactionId);
        _mockDepositRepo.Verify(r => r.UpdateAsync(deposit), Times.Once);
        _mockDepositRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region MarkAsRefundedAsync

    [Fact]
    public async Task MarkAsRefundedAsync_AlreadyRefunded_ReturnsDepositWithoutUpdate()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Refunded);

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        var result = await _service.MarkAsRefundedAsync(depositId);

        Assert.Equal(BookingDepositStatus.Refunded, result.Status);
        _mockDepositRepo.Verify(r => r.UpdateAsync(It.IsAny<BookingDeposit>()), Times.Never);
    }

    [Fact]
    public async Task MarkAsRefundedAsync_NotPaid_ThrowsConflictException()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.MarkAsRefundedAsync(depositId));

        Assert.Contains("'Pending'", ex.Message);
    }

    [Fact]
    public async Task MarkAsRefundedAsync_ValidRequest_UpdatesStatusToRefunded()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);
        _mockDepositRepo.Setup(r => r.UpdateAsync(It.IsAny<BookingDeposit>())).Returns(Task.CompletedTask);
        _mockDepositRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var result = await _service.MarkAsRefundedAsync(depositId);

        Assert.Equal(BookingDepositStatus.Refunded, result.Status);
        Assert.NotNull(result.RefundedAt);
        _mockDepositRepo.Verify(r => r.UpdateAsync(deposit), Times.Once);
        _mockDepositRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region ForfeitAsync

    [Fact]
    public async Task ForfeitAsync_AlreadyForfeited_ReturnsDepositWithoutUpdate()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Forfeited);
        deposit.RefundPolicy = DepositRefundPolicy.None;

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        var result = await _service.ForfeitAsync(depositId);

        Assert.Equal(BookingDepositStatus.Forfeited, result.Status);
        _mockDepositRepo.Verify(r => r.UpdateAsync(It.IsAny<BookingDeposit>()), Times.Never);
    }

    [Fact]
    public async Task ForfeitAsync_NotPaid_ThrowsConflictException()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ForfeitAsync(depositId));

        Assert.Contains("'Pending'", ex.Message);
    }

    [Fact]
    public async Task ForfeitAsync_WrongRefundPolicy_ThrowsConflictException()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);
        deposit.RefundPolicy = DepositRefundPolicy.Full; // wrong policy

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ForfeitAsync(depositId));

        Assert.Contains("'Full'", ex.Message);
    }

    [Fact]
    public async Task ForfeitAsync_ValidRequest_UpdatesStatusToForfeited()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);
        deposit.RefundPolicy = DepositRefundPolicy.None;

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);
        _mockDepositRepo.Setup(r => r.UpdateAsync(It.IsAny<BookingDeposit>())).Returns(Task.CompletedTask);
        _mockDepositRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var result = await _service.ForfeitAsync(depositId);

        Assert.Equal(BookingDepositStatus.Forfeited, result.Status);
        Assert.NotNull(result.ForfeitedAt);
        _mockDepositRepo.Verify(r => r.UpdateAsync(deposit), Times.Once);
        _mockDepositRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region ExpireAsync

    [Fact]
    public async Task ExpireAsync_NotPending_DoesNotUpdate()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        await _service.ExpireAsync(depositId);

        _mockDepositRepo.Verify(r => r.UpdateAsync(It.IsAny<BookingDeposit>()), Times.Never);
    }

    [Fact]
    public async Task ExpireAsync_Pending_UpdatesStatusToRefunded()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);

        _mockDepositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);
        _mockDepositRepo.Setup(r => r.UpdateAsync(It.IsAny<BookingDeposit>())).Returns(Task.CompletedTask);
        _mockDepositRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        await _service.ExpireAsync(depositId);

        Assert.Equal(BookingDepositStatus.Refunded, deposit.Status);
        Assert.NotNull(deposit.RefundedAt);
        _mockDepositRepo.Verify(r => r.UpdateAsync(deposit), Times.Once);
        _mockDepositRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region ProcessExpiredDepositsAsync

    [Fact]
    public async Task ProcessExpiredDepositsAsync_NoExpiredDeposits_DoesNotUpdate()
    {
        _mockDepositRepo.Setup(r => r.GetPendingExpiredAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<BookingDeposit>());

        await _service.ProcessExpiredDepositsAsync();

        _mockDepositRepo.Verify(r => r.UpdateAsync(It.IsAny<BookingDeposit>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredDepositsAsync_WithExpiredDeposits_UpdatesAllToRefunded()
    {
        var deposit1 = CreateTestDeposit(Guid.NewGuid(), BookingDepositStatus.Pending);
        var deposit2 = CreateTestDeposit(Guid.NewGuid(), BookingDepositStatus.Pending);

        _mockDepositRepo.Setup(r => r.GetPendingExpiredAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<BookingDeposit> { deposit1, deposit2 });
        _mockDepositRepo.Setup(r => r.UpdateAsync(It.IsAny<BookingDeposit>())).Returns(Task.CompletedTask);
        _mockDepositRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        await _service.ProcessExpiredDepositsAsync();

        Assert.Equal(BookingDepositStatus.Refunded, deposit1.Status);
        Assert.Equal(BookingDepositStatus.Refunded, deposit2.Status);
        Assert.NotNull(deposit1.RefundedAt);
        Assert.NotNull(deposit2.RefundedAt);
        _mockDepositRepo.Verify(r => r.UpdateAsync(It.IsAny<BookingDeposit>()), Times.Exactly(2));
        _mockDepositRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredDepositsAsync_UsesFiveMinuteCutoff()
    {
        DateTime capturedCutoff = default;

        _mockDepositRepo.Setup(r => r.GetPendingExpiredAsync(It.IsAny<DateTime>()))
            .Callback<DateTime>(cutoff => capturedCutoff = cutoff)
            .ReturnsAsync(new List<BookingDeposit>());

        await _service.ProcessExpiredDepositsAsync();

        var expectedCutoff = DateTime.UtcNow.AddMinutes(-5);
        Assert.InRange(capturedCutoff, expectedCutoff.AddMinutes(-1), expectedCutoff.AddMinutes(1));
    }

    #endregion

    #region Helpers

    private static BookingDeposit CreateTestDeposit(Guid? id = null, BookingDepositStatus status = BookingDepositStatus.Pending, Guid? cafeId = null)
    {
        return new BookingDeposit
        {
            Id = id ?? Guid.NewGuid(),
            OrderId = $"BV{DateTime.UtcNow.Ticks % 100_000_000:D8}",
            ActiveSessionId = Guid.NewGuid(),
            CafeId = cafeId ?? Guid.NewGuid(),
            CafeManagerId = Guid.NewGuid(),
            MasterAccountId = Guid.NewGuid(),
            Amount = 50_000m,
            RefundPolicy = DepositRefundPolicy.Full,
            Status = status,
            TransferContent = "Transfer content",
            SePayTransactionId = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
    }

    private static Cafe CreateTestCafe(Guid id, decimal basePrice)
    {
        return new Cafe
        {
            Id = id,
            Name = "Test Cafe",
            Address = "123 Test St",
            BasePrice = basePrice,
            IsActive = true,
            BillingModel = CafePartnerBillingModel.TimeBased,
            TieredBlockMinutes = 15,
            TieredBlockRate = 10_000m
        };
    }

    #endregion
}
