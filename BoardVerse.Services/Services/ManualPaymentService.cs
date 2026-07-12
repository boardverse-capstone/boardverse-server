using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class ManualPaymentService : IManualPaymentService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IBookingDepositRepository _depositRepository;
    private readonly IActiveSessionRepository _sessionRepository;
    private readonly ILogger<ManualPaymentService> _logger;

    public ManualPaymentService(
        ITransactionRepository transactionRepository,
        IBookingDepositRepository depositRepository,
        IActiveSessionRepository sessionRepository,
        ILogger<ManualPaymentService> logger)
    {
        _transactionRepository = transactionRepository;
        _depositRepository = depositRepository;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<ManualPaymentConfirmResponseDto> ConfirmManualPaymentAsync(
        ManualPaymentConfirmRequestDto request,
        Guid staffId,
        CancellationToken cancellationToken = default)
    {
        // Validate payment type
        var paymentType = request.PaymentType.ToUpperInvariant() switch
        {
            "DEPOSIT" => "Deposit",
            "SESSION" => "Session",
            _ => throw new ArgumentException($"Invalid payment type: {request.PaymentType}. Must be 'Deposit' or 'Session'.")
        };

        // Validate payment method
        var validMethods = new[] { "CASH", "BANK_TRANSFER", "QR_CODE", "MANUAL" };
        if (!validMethods.Contains(request.PaymentMethod.ToUpperInvariant()))
        {
            throw new ArgumentException($"Invalid payment method: {request.PaymentMethod}.");
        }

        // Create manual transaction
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Amount = request.Amount,
            Currency = "VND",
            Gateway = "MANUAL",
            GatewayTransactionId = request.OrderId.ToString(),
            GatewayResponseCode = "MANUAL_CONFIRM",
            GatewayResponseMessage = request.Notes ?? "Thanh toán thủ công bởi nhân viên",
            Status = TransactionStatus.Succeeded,
            Type = paymentType == "Deposit"
                ? TransactionType.BookingDeposit
                : TransactionType.GameRental,
            Direction = TransactionDirection.In,
            Notes = $"Manual confirm by Staff: {staffId}. Method: {request.PaymentMethod}. Type: {paymentType}.",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        // Update corresponding order status
        if (paymentType == "Deposit")
        {
            await HandleDepositConfirmationAsync(request.OrderId, request.Amount, staffId, cancellationToken);
        }
        else
        {
            await HandleSessionConfirmationAsync(request.OrderId, request.Amount, staffId, cancellationToken);
        }

        _logger.LogInformation(
            "Manual payment confirmed. Type={Type}, OrderId={OrderId}, Amount={Amount}, Method={Method}, StaffId={StaffId}",
            paymentType, request.OrderId, request.Amount, request.PaymentMethod, staffId);

        return new ManualPaymentConfirmResponseDto
        {
            TransactionId = transaction.Id,
            PaymentType = paymentType,
            OrderId = request.OrderId,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            Status = "Confirmed",
            ConfirmedAt = DateTime.UtcNow,
            ConfirmedBy = staffId.ToString()
        };
    }

    private async Task HandleDepositConfirmationAsync(Guid depositId, decimal amount, Guid staffId, CancellationToken cancellationToken)
    {
        var deposit = await _depositRepository.GetByIdAsync(depositId)
            ?? throw new NotFoundException($"Booking deposit not found: {depositId}");

        if (deposit.Status != BookingDepositStatus.Pending)
        {
            throw new ConflictException($"Deposit is not in Pending status. Current: {deposit.Status}");
        }

        deposit.Status = BookingDepositStatus.Paid;
        deposit.PaidAt = DateTime.UtcNow;

        await _depositRepository.UpdateAsync(deposit);

        _logger.LogInformation(
            "Booking deposit confirmed manually. DepositId={DepositId}, Amount={Amount}, StaffId={StaffId}",
            depositId, amount, staffId);
    }

    private async Task HandleSessionConfirmationAsync(Guid sessionId, decimal amount, Guid staffId, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId)
            ?? throw new NotFoundException($"Active session not found: {sessionId}");

        if (session.Status != GroupSessionStatus.Unpaid)
        {
            throw new ConflictException($"Session is not in Unpaid status. Current: {session.Status}");
        }

        session.Status = GroupSessionStatus.Paid;
        session.PaidAt = DateTime.UtcNow;

        await _sessionRepository.UpdateAsync(session);

        _logger.LogInformation(
            "Session payment confirmed manually. SessionId={SessionId}, Amount={Amount}, StaffId={StaffId}",
            sessionId, amount, staffId);
    }
}
