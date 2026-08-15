using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class BookingDepositService : IBookingDepositService
{
    private readonly IBookingDepositRepository _depositRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ILogger<BookingDepositService> _logger;
    private readonly BoardVerseDbContext? _db; // GAP #26: cần cho batch transaction.

    public BookingDepositService(
        IBookingDepositRepository depositRepository,
        IBookingRepository bookingRepository,
        ICafeRepository cafeRepository,
        ILogger<BookingDepositService> logger,
        BoardVerseDbContext? db)
    {
        _depositRepository = depositRepository;
        _bookingRepository = bookingRepository;
        _cafeRepository = cafeRepository;
        _logger = logger;
        _db = db;
    }

    public async Task<BookingDeposit> CreateAsync(
        Guid userId,
        Guid cafeId,
        Guid cafeManagerId,
        decimal amount,
        DepositRefundPolicy refundPolicy,
        DateTime? scheduledAt = null,
        Guid? bookingId = null)
    {
        var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var maxDeposit = cafe.BasePrice * 0.5m;
        if (amount > maxDeposit)
        {
            throw new BadRequestException(
                ApiErrorMessages.Pos.DepositExceedsHalfBasePrice(amount, maxDeposit));
        }

        if (amount <= 0)
        {
            throw new BadRequestException(ApiErrorMessages.Pos.DepositAmountMustBePositive);
        }

        var deposit = new BookingDeposit
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CafeId = cafeId,
            CafeManagerId = cafeManagerId,
            Amount = amount,
            RefundPolicy = refundPolicy,
            Status = BookingDepositStatus.Pending,
            ScheduledAt = scheduledAt,
            BookingId = bookingId,
            CreatedAt = DateTime.UtcNow
        };

        await _depositRepository.AddAsync(deposit);
        await _depositRepository.SaveChangesAsync();

        _logger.LogInformation(
            "BookingDeposit created. DepositId={DepositId}, Amount={Amount}, CafeId={CafeId}, BookingId={BookingId}, RefundPolicy={RefundPolicy}",
            deposit.Id, deposit.Amount, cafeId, bookingId, refundPolicy);

        return deposit;
    }

    public async Task<BookingDeposit> MarkAsPaidAsync(Guid depositId, string? sePayTransactionId = null)
    {
        var now = DateTime.UtcNow;
        var rowsAffected = await _depositRepository.TryMarkAsPaidAsync(depositId, sePayTransactionId, now);

        if (rowsAffected == 0)
        {
            // Either deposit not found, or status was not Pending (already Paid/Refunded/Forfeited).
            // Re-fetch to distinguish + return idempotent for the "already paid" case.
            var existing = await _depositRepository.GetByIdAsync(depositId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

            if (existing.Status == BookingDepositStatus.Paid)
            {
                _logger.LogInformation("Deposit already paid (duplicate webhook). DepositId={DepositId}", depositId);
                return existing;
            }

            throw new ConflictException(ApiErrorMessages.Payment.DepositMarkAsPaidInvalidStatus(existing.Status.ToString()));
        }

        // GAP-C4: rowsAffected == 1 → we won the race; load updated entity + trigger booking confirm.
        var deposit = await _depositRepository.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        // BR-05: Nếu deposit có liên kết Booking -> tự động confirm booking.
        if (deposit.BookingId.HasValue)
        {
            var booking = await _bookingRepository.GetByIdAsync(deposit.BookingId.Value);
            if (booking != null && booking.Status == BookingStatus.PendingDeposit)
            {
                booking.Status = BookingStatus.Confirmed;
                await _bookingRepository.UpdateAsync(booking);
                await _bookingRepository.SaveChangesAsync();
                _logger.LogInformation(
                    "Booking auto-confirmed after deposit paid. BookingId={BookingId}, DepositId={DepositId}",
                    deposit.BookingId.Value, depositId);
            }
        }

        _logger.LogInformation(
            "BookingDeposit marked as paid. DepositId={DepositId}, Amount={Amount}, SePayTransactionId={SePayTransactionId}",
            deposit.Id, deposit.Amount, sePayTransactionId);

        return deposit;
    }

    public async Task<BookingDeposit> MarkAsRefundedAsync(Guid depositId)
    {
        var now = DateTime.UtcNow;
        var rowsAffected = await _depositRepository.TryMarkAsRefundedAsync(depositId, now);

        if (rowsAffected == 0)
        {
            var existing = await _depositRepository.GetByIdAsync(depositId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

            if (existing.Status == BookingDepositStatus.Refunded)
            {
                _logger.LogInformation("Deposit already refunded (idempotent). DepositId={DepositId}", depositId);
                return existing;
            }

            throw new ConflictException(ApiErrorMessages.Payment.DepositRefundInvalidStatus(existing.Status.ToString()));
        }

        var deposit = await _depositRepository.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        var refundAmount = CalculatePartialRefund(deposit);
        _logger.LogInformation(
            "Refund calculated. DepositId={DepositId}, OriginalAmount={Amount}, RefundAmount={RefundAmount}, Policy={Policy}",
            depositId, deposit.Amount, refundAmount, deposit.RefundPolicy);

        _logger.LogInformation(
            "BookingDeposit refunded. DepositId={DepositId}, RefundedAmount={RefundAmount}",
            depositId, refundAmount);

        return deposit;
    }

    public async Task<BookingDeposit> ForfeitAsync(Guid depositId)
    {
        var now = DateTime.UtcNow;
        var rowsAffected = await _depositRepository.TryForfeitAsync(depositId, now);

        if (rowsAffected == 0)
        {
            var existing = await _depositRepository.GetByIdAsync(depositId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

            if (existing.Status == BookingDepositStatus.Forfeited)
            {
                _logger.LogInformation("Deposit already forfeited (idempotent). DepositId={DepositId}", depositId);
                return existing;
            }

            if (existing.RefundPolicy != DepositRefundPolicy.None)
            {
                throw new ConflictException(ApiErrorMessages.Payment.DepositForfeitInvalidPolicy(existing.RefundPolicy.ToString()));
            }

            throw new ConflictException(ApiErrorMessages.Payment.DepositForfeitInvalidStatus(existing.Status.ToString()));
        }

        var deposit = await _depositRepository.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        _logger.LogInformation("BookingDeposit forfeited (no-refund policy). DepositId={DepositId}, Amount={Amount}",
            depositId, deposit.Amount);

        return deposit;
    }

    public async Task ExpireAsync(Guid depositId)
    {
        var now = DateTime.UtcNow;
        var rowsAffected = await _depositRepository.TryExpireAsync(depositId, now);

        if (rowsAffected == 0)
        {
            // Already expired (Refunded) or in another terminal state — idempotent no-op.
            _logger.LogInformation("Deposit expiry no-op (already in terminal state). DepositId={DepositId}", depositId);
            return;
        }

        _logger.LogInformation("BookingDeposit expired. DepositId={DepositId}", depositId);
    }

    public async Task ProcessExpiredDepositsAsync()
    {
        const int BatchSize = 50;
        var now = DateTime.UtcNow;
        var expiryThreshold = now.AddMinutes(-5);

        // GAP #26 fix: batch transaction cho FOR UPDATE SKIP LOCKED.
        // Mỗi tick load tối đa 50 deposit expired → tránh long-running tx.
        // BoardVerseDbContext không override BeginTransactionAsync(IsolationLevel) → dùng default.
        // Trong unit test với FakeDbContext (không có provider), BeginTransactionAsync throw → bỏ qua
        // transaction nhưng vẫn chạy logic. SaveChangesAsync() đã tự wrap trong implicit transaction.
        await using var batchTx = await TryBeginTransactionAsync();

        var expiredDeposits = await _depositRepository.GetPendingExpiredAsync(expiryThreshold, BatchSize);

        try
        {
            foreach (var deposit in expiredDeposits)
            {
                deposit.Status = BookingDepositStatus.Refunded;
                deposit.RefundedAt = now;
                deposit.UpdatedAt = now;
                await _depositRepository.UpdateAsync(deposit);
                _logger.LogInformation("Deposit expired. DepositId={DepositId}, CreatedAt={CreatedAt}", deposit.Id, deposit.CreatedAt);
            }

            if (expiredDeposits.Count > 0)
            {
                await _depositRepository.SaveChangesAsync();
            }

            if (batchTx != null)
            {
                await batchTx.CommitAsync();
            }
        }
        catch
        {
            if (batchTx != null)
            {
                await batchTx.RollbackAsync();
            }
            throw;
        }
    }

    private async Task<IDbContextTransaction?> TryBeginTransactionAsync()
    {
        if (_db == null)
        {
            return null;
        }
        try
        {
            return await _db.Database.BeginTransactionAsync();
        }
        catch (InvalidOperationException)
        {
            // DbContext không có database provider (ví dụ: FakeDbContext trong unit test).
            return null;
        }
    }

    public async Task<BookingDeposit?> GetByIdAsync(Guid depositId)
    {
        return await _depositRepository.GetByIdAsync(depositId);
    }

    public async Task<BookingDeposit?> GetByOrderIdAsync(string orderId)
    {
        return await _depositRepository.GetByOrderIdAsync(orderId);
    }

    public async Task<BookingDeposit?> GetBySePayTransactionIdAsync(string sePayTransactionId)
    {
        return await _depositRepository.GetBySePayTransactionIdAsync(sePayTransactionId);
    }

    /// <summary>BR-05: Lấy đơn cọc theo BookingId.</summary>
    public async Task<BookingDeposit?> GetByBookingIdAsync(Guid bookingId)
    {
        return await _depositRepository.GetByBookingIdAsync(bookingId);
    }

    public async Task UpdateQrInfoAsync(Guid depositId, string qrUrl, DateTime? qrExpiresAt, string? transferContent = null)
    {
        var deposit = await _depositRepository.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        deposit.QrUrl = qrUrl;
        deposit.QrExpiresAt = qrExpiresAt;
        if (!string.IsNullOrWhiteSpace(transferContent))
        {
            deposit.TransferContent = transferContent;
        }
        // P2 Fix #11: Track last QR regeneration time for rate limiting
        deposit.LastQrRegeneratedAt = DateTime.UtcNow;
        deposit.UpdatedAt = DateTime.UtcNow;

        await _depositRepository.UpdateAsync(deposit);
        await _depositRepository.SaveChangesAsync();

        _logger.LogInformation(
            "BookingDeposit QR updated. DepositId={DepositId}, QrExpiresAt={QrExpiresAt}",
            depositId, qrExpiresAt);
    }

    /// <summary>
    /// Tính số tiền hoàn cọc theo BR-18 cho các policy tương ứng.
    /// Expose public để controller có thể trả về RefundedAmount trong response.
    /// </summary>
    public decimal CalculatePartialRefundAmount(BookingDeposit deposit)
    {
        return deposit.RefundPolicy switch
        {
            DepositRefundPolicy.Full => deposit.Amount,
            DepositRefundPolicy.None => 0m,
            DepositRefundPolicy.Partial => CalculatePartialRefund(deposit),
            _ => 0m
        };
    }

    private static decimal CalculatePartialRefund(BookingDeposit deposit)
    {
        // BR-REFUND-02 (BR mới): tính theo khoảng cách từ lúc hủy đến giờ chơi dự kiến
        // >= 24 giờ trước giờ chơi → hoàn 100%
        // 6 giờ đến < 24 giờ → hoàn 50%
        // < 6 giờ → 0%
        var scheduledAt = deposit.ScheduledAt ?? DateTime.UtcNow;
        var hoursUntilPlay = (scheduledAt - DateTime.UtcNow).TotalHours;

        if (hoursUntilPlay < 0)
        {
            return 0m;
        }

        if (hoursUntilPlay >= 24)
        {
            return deposit.Amount; // 100%
        }
        if (hoursUntilPlay >= 6)
        {
            return deposit.Amount * 0.50m; // 50%
        }
        return 0m; // < 6 giờ
    }
}
