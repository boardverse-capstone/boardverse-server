using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class BookingDepositService : IBookingDepositService
{
    private readonly IBookingDepositRepository _depositRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ILogger<BookingDepositService> _logger;

    public BookingDepositService(
        IBookingDepositRepository depositRepository,
        ICafeRepository cafeRepository,
        ILogger<BookingDepositService> logger)
    {
        _depositRepository = depositRepository;
        _cafeRepository = cafeRepository;
        _logger = logger;
    }

    public async Task<BookingDeposit> CreateAsync(
        Guid activeSessionId,
        Guid cafeId,
        Guid cafeManagerId,
        decimal amount,
        DepositRefundPolicy refundPolicy,
        DateTime? scheduledAt = null)
    {
        var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var maxDeposit = cafe.BasePrice * 0.5m;
        if (amount > maxDeposit)
        {
            throw new BadRequestException(
                $"Số tiền cọc ({amount:N0}đ) vượt quá 50% giá vé cơ bản ({maxDeposit:N0}đ) của quán. BR-03.");
        }

        if (amount <= 0)
        {
            throw new BadRequestException("Số tiền cọc phải lớn hơn 0.");
        }

        var deposit = new BookingDeposit
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = activeSessionId,
            CafeId = cafeId,
            CafeManagerId = cafeManagerId,
            Amount = amount,
            RefundPolicy = refundPolicy,
            Status = BookingDepositStatus.Pending,
            ScheduledAt = scheduledAt,
            CreatedAt = DateTime.UtcNow
        };

        await _depositRepository.AddAsync(deposit);
        await _depositRepository.SaveChangesAsync();

        _logger.LogInformation(
            "BookingDeposit created. DepositId={DepositId}, Amount={Amount}, CafeId={CafeId}, RefundPolicy={RefundPolicy}",
            deposit.Id, deposit.Amount, cafeId, refundPolicy);

        return deposit;
    }

    public async Task<BookingDeposit> MarkAsPaidAsync(Guid depositId, string? sePayTransactionId = null)
    {
        var deposit = await _depositRepository.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        if (deposit.Status == BookingDepositStatus.Paid)
        {
            _logger.LogInformation("Deposit already paid. DepositId={DepositId}", depositId);
            return deposit;
        }

        if (deposit.Status != BookingDepositStatus.Pending)
        {
            throw new ConflictException($"Không thể đánh dấu đã thanh toán: trạng thái hiện tại là '{deposit.Status}', cần 'Pending'.");
        }

        deposit.Status = BookingDepositStatus.Paid;
        deposit.PaidAt = DateTime.UtcNow;
        deposit.SePayTransactionId = sePayTransactionId ?? deposit.SePayTransactionId;
        deposit.UpdatedAt = DateTime.UtcNow;

        await _depositRepository.UpdateAsync(deposit);
        await _depositRepository.SaveChangesAsync();

        _logger.LogInformation(
            "BookingDeposit marked as paid. DepositId={DepositId}, Amount={Amount}, SePayTransactionId={SePayTransactionId}",
            deposit.Id, deposit.Amount, sePayTransactionId);

        return deposit;
    }

    public async Task<BookingDeposit> MarkAsRefundedAsync(Guid depositId)
    {
        var deposit = await _depositRepository.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        if (deposit.Status == BookingDepositStatus.Refunded)
        {
            _logger.LogInformation("Deposit already refunded. DepositId={DepositId}", depositId);
            return deposit;
        }

        if (deposit.Status != BookingDepositStatus.Paid)
        {
            throw new ConflictException($"Không thể hoàn cọc: trạng thái hiện tại là '{deposit.Status}', cần 'Paid'.");
        }

        var refundAmount = CalculatePartialRefund(deposit);
        _logger.LogInformation(
            "Refund calculated. DepositId={DepositId}, OriginalAmount={Amount}, RefundAmount={RefundAmount}, Policy={Policy}",
            depositId, deposit.Amount, refundAmount, deposit.RefundPolicy);

        deposit.Status = BookingDepositStatus.Refunded;
        deposit.RefundedAt = DateTime.UtcNow;
        deposit.UpdatedAt = DateTime.UtcNow;

        await _depositRepository.UpdateAsync(deposit);
        await _depositRepository.SaveChangesAsync();

        _logger.LogInformation(
            "BookingDeposit refunded. DepositId={DepositId}, RefundedAmount={RefundAmount}",
            depositId, refundAmount);

        return deposit;
    }

    public async Task<BookingDeposit> ForfeitAsync(Guid depositId)
    {
        var deposit = await _depositRepository.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        if (deposit.Status == BookingDepositStatus.Forfeited)
        {
            _logger.LogInformation("Deposit already forfeited. DepositId={DepositId}", depositId);
            return deposit;
        }

        if (deposit.Status != BookingDepositStatus.Paid)
        {
            throw new ConflictException($"Không thể tịch thu cọc: trạng thái hiện tại là '{deposit.Status}', cần 'Paid'.");
        }

        if (deposit.RefundPolicy != DepositRefundPolicy.None)
        {
            throw new ConflictException($"Không thể tịch thu: chính sách hoàn tiền là '{deposit.RefundPolicy}', cần 'None'.");
        }

        deposit.Status = BookingDepositStatus.Forfeited;
        deposit.ForfeitedAt = DateTime.UtcNow;
        deposit.UpdatedAt = DateTime.UtcNow;

        await _depositRepository.UpdateAsync(deposit);
        await _depositRepository.SaveChangesAsync();

        _logger.LogInformation("BookingDeposit forfeited (no-refund policy). DepositId={DepositId}, Amount={Amount}",
            depositId, deposit.Amount);

        return deposit;
    }

    public async Task ExpireAsync(Guid depositId)
    {
        var deposit = await _depositRepository.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        if (deposit.Status != BookingDepositStatus.Pending)
        {
            _logger.LogInformation("Cannot expire deposit: status is {Status}, DepositId={DepositId}", deposit.Status, depositId);
            return;
        }

        deposit.Status = BookingDepositStatus.Refunded;
        deposit.RefundedAt = DateTime.UtcNow;
        deposit.UpdatedAt = DateTime.UtcNow;

        await _depositRepository.UpdateAsync(deposit);
        await _depositRepository.SaveChangesAsync();

        _logger.LogInformation("BookingDeposit expired. DepositId={DepositId}", depositId);
    }

    public async Task ProcessExpiredDepositsAsync()
    {
        var now = DateTime.UtcNow;
        var expiryThreshold = now.AddMinutes(-5);

        var expiredDeposits = await _depositRepository.GetPendingExpiredAsync(expiryThreshold);

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
        deposit.UpdatedAt = DateTime.UtcNow;

        await _depositRepository.UpdateAsync(deposit);
        await _depositRepository.SaveChangesAsync();

        _logger.LogInformation(
            "BookingDeposit QR updated. DepositId={DepositId}, QrExpiresAt={QrExpiresAt}",
            depositId, qrExpiresAt);
    }

    private static decimal CalculatePartialRefund(BookingDeposit deposit)
    {
        var elapsedHours = (DateTime.UtcNow - deposit.CreatedAt).TotalHours;

        if (elapsedHours >= 24)
        {
            return deposit.Amount * 0.50m;
        }
        if (elapsedHours >= 12)
        {
            return deposit.Amount * 0.25m;
        }
        return 0m;
    }
}
