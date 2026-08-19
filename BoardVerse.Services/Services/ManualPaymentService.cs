using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class ManualPaymentService : IManualPaymentService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IBookingDepositRepository _depositRepository;
    private readonly IActiveSessionRepository _sessionRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ILogger<ManualPaymentService> _logger;

    public ManualPaymentService(
        ITransactionRepository transactionRepository,
        IBookingDepositRepository depositRepository,
        IActiveSessionRepository sessionRepository,
        ICafeRepository cafeRepository,
        ILogger<ManualPaymentService> logger)
    {
        _transactionRepository = transactionRepository;
        _depositRepository = depositRepository;
        _sessionRepository = sessionRepository;
        _cafeRepository = cafeRepository;
        _logger = logger;
    }

    public async Task<ManualPaymentConfirmResponseDto> ConfirmManualPaymentAsync(
        ManualPaymentConfirmRequestDto request,
        Guid staffId,
        string actorRole,
        CancellationToken cancellationToken = default)
    {
        // Validate payment type — chỉ chấp nhận SESSION (M6).
        // DEPOSIT có endpoint riêng (cash deposit) — tách để tránh staff lạm quyền.
        if (!string.Equals(request.PaymentType, "SESSION", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(ApiErrorMessages.Payment.InvalidPaymentType(request.PaymentType));
        }

        // Validate payment method
        var validMethods = new[] { "CASH", "BANK_TRANSFER", "QR_CODE", "MANUAL" };
        if (!validMethods.Contains(request.PaymentMethod.ToUpperInvariant()))
        {
            throw new ArgumentException(ApiErrorMessages.Payment.InvalidPaymentMethod(request.PaymentMethod));
        }

        // C1: Validate target order FIRST (read-only) before persisting the Transaction record.
        // Tránh ghi Transaction Succeeded rồi mới phát hiện order invalid → orphan financial record.
        var session = await _sessionRepository.GetByIdWithMembersAsync(request.OrderId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ActiveSessionNotFound(request.OrderId));

        if (session.Status != GroupSessionStatus.Unpaid)
        {
            throw new ConflictException(ApiErrorMessages.Payment.SessionNotUnpaid(session.Status.ToString()));
        }

        // H5: Amount mismatch check.
        if (request.Amount != session.TotalAmount)
        {
            throw new ConflictException(
                ApiErrorMessages.Payment.ManualConfirmAmountMismatch(session.TotalAmount, request.Amount));
        }

        // C3: Cafe ownership/staff check (Admin bypass).
        if (!string.Equals(actorRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var cafe = await _cafeRepository.GetActiveByIdAsync(session.CafeId)
                ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(session.CafeId));

            var isOwner = cafe.ManagerId == staffId;
            var isStaff = await _cafeRepository.IsStaffMemberExistsAsync(session.CafeId, staffId);

            if (!isOwner && !isStaff)
            {
                _logger.LogWarning(
                    "Manual confirm rejected: staff {StaffId} not affiliated with cafe {CafeId}",
                    staffId, session.CafeId);
                throw new ForbiddenException(
                    ApiErrorMessages.Payment.ManualConfirmNotAuthorizedForCafe(session.CafeId));
            }
        }

        var now = DateTime.UtcNow;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = session.HostId,
            CafeId = session.CafeId,
            Amount = request.Amount,
            Currency = "VND",
            Gateway = "MANUAL",
            GatewayTransactionId = request.OrderId.ToString(),
            GatewayResponseCode = "MANUAL_CONFIRM",
            GatewayResponseMessage = request.Notes ?? "Thanh toán thủ công bởi nhân viên",
            Status = TransactionStatus.Succeeded,
            Type = TransactionType.GameRental,
            Direction = TransactionDirection.In,
            Notes = $"Manual confirm by Staff: {staffId} (Role={actorRole}). Method: {request.PaymentMethod}.",
            CreatedAt = now,
            CompletedAt = now
        };

        // H7: Wrap add Transaction + status update + cleanup in a single atomic transaction.
        // If any step fails → rollback toàn bộ, không có orphan Succeeded Transaction.
        // null-safe: unit test với Mock không setup BeginTransactionAsync → null.
        await using var dbTx = await TryBeginTransactionAsync(cancellationToken);

        try
        {
            await _transactionRepository.AddAsync(transaction, cancellationToken);

            session.Status = GroupSessionStatus.Paid;
            session.PaidAt = now;
            await _sessionRepository.UpdateAsync(session);
            await _sessionRepository.SaveChangesAsync();

            // Lifecycle cleanup: close lobby (in transaction with status update).
            // GAP-08 Fix: wrap trong try/catch — fail vẫn commit payment.
            try
            {
                await _sessionRepository.ReleaseMembersAndCloseLobbyAsync(request.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GAP-08: ManualPay - ReleaseMembersAndCloseLobby failed for SessionId={SessionId}. " +
                    "Payment vẫn commit; lobby close sẽ retry qua AutoReleaseExpiredSessionsJob.",
                    request.OrderId);
            }

            if (dbTx != null)
            {
                await dbTx.CommitAsync(cancellationToken);
            }

            // FIX: Release table/box AFTER payment commit (not at checkout).
            // This ensures table/box stays InUse while awaiting payment.
            // GAP-06 Fix: try/catch + log — fail thì background job retry.
            try
            {
                await _sessionRepository.ReleaseSessionTableAndBoxAsync(request.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GAP-06: ManualPay - ReleaseSessionTableAndBox failed for SessionId={SessionId} AFTER commit. " +
                    "Session PAID nhưng table/box vẫn InUse. Background job sẽ retry.",
                    request.OrderId);
            }

            _logger.LogInformation(
                "Manual session payment confirmed. SessionId={SessionId}, Amount={Amount}, Method={Method}, StaffId={StaffId}, Role={Role}",
                request.OrderId, request.Amount, request.PaymentMethod, staffId, actorRole);

            return new ManualPaymentConfirmResponseDto
            {
                TransactionId = transaction.Id,
                PaymentType = "Session",
                OrderId = request.OrderId,
                Amount = request.Amount,
                PaymentMethod = request.PaymentMethod,
                Status = "Confirmed",
                ConfirmedAt = now,
                ConfirmedBy = staffId.ToString()
            };
        }
        catch
        {
            if (dbTx != null)
            {
                await dbTx.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    // Helper: try begin transaction; return null if repository doesn't support it
    // (e.g., unit tests with Mock<ITransactionRepository>).
    private async Task<Core.IRepositories.IDatabaseTransactionContext?> TryBeginTransactionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _transactionRepository.BeginTransactionAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }
}