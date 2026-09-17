using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class ManualPaymentService : IManualPaymentService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IBookingDepositRepository _depositRepository;
    private readonly IActiveSessionRepository _sessionRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly BoardVerseDbContext _dbContext;
    private readonly ILogger<ManualPaymentService> _logger;

    public ManualPaymentService(
        ITransactionRepository transactionRepository,
        IBookingDepositRepository depositRepository,
        IActiveSessionRepository sessionRepository,
        ICafeRepository cafeRepository,
        BoardVerseDbContext dbContext,
        ILogger<ManualPaymentService> logger)
    {
        _transactionRepository = transactionRepository;
        _depositRepository = depositRepository;
        _sessionRepository = sessionRepository;
        _cafeRepository = cafeRepository;
        _dbContext = dbContext;
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

        // GAP-MANUAL-PAY-RACE Fix: Atomic flip Status=Unpaid → Paid với WHERE clause.
        // Trước đây: 2 staff cùng click "Confirm" cho 1 session → cả 2 pass status check
        //   → cả 2 tạo Transaction { Status = Succeeded } → cả 2 flip session.Status = Paid
        //   → orphan Transaction record (2 lần ghi nhận thanh toán cho 1 session).
        // Sau: ExecuteUpdateAsync WHERE Status=Unpaid → atomic. Nếu 0 rows → race detected
        //   → throw ConflictException (đã có người khác xử lý) → không tạo Transaction.
        // InMemory provider không hỗ trợ ExecuteUpdateAsync → fallback direct mutation + SaveChanges.
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

        // H7: Wrap flip + add Transaction + SaveChanges trong 1 atomic transaction.
        // Nếu bất kỳ step nào fail → rollback toàn bộ, không có orphan Succeeded Transaction.
        // null-safe: unit test với Mock không setup BeginTransactionAsync → null.
        await using var dbTx = await TryBeginTransactionAsync(cancellationToken);

        try
        {
            // GAP-MANUAL-PAY-RACE Fix: atomic flip với WHERE clause.
            // Bypass change tracker (ExecuteUpdateAsync tự update DB không qua tracker).
            // Nếu InMemory provider → fallback mutation trực tiếp trên entity + SaveChanges.
            var flipped = await TryAtomicFlipSessionStatusAsync(
                request.OrderId,
                GroupSessionStatus.Unpaid,
                now,
                cancellationToken);

            if (!flipped)
            {
                // Race: session đã được paid bởi request khác (staff khác click cùng lúc,
                // hoặc webhook QR vừa flip Paid). KHÔNG tạo Transaction để tránh orphan.
                _logger.LogWarning(
                    "GAP-MANUAL-PAY-RACE: Session {SessionId} đã được thanh toán bởi request khác trước khi manual confirm commit. " +
                    "Staff={StaffId}. Bỏ qua manual confirm.",
                    request.OrderId, staffId);
                throw new ConflictException(ApiErrorMessages.Payment.SessionNotUnpaid(
                    "Paid (already processed by another request)"));
            }

            await _transactionRepository.AddAsync(transaction, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Detach session + members để các operation tiếp theo (ReleaseMembersAndCloseLobbyAsync,
            // ReleaseSessionTableAndBoxAsync) không bị EF Core Identity Resolution trả về
            // tracked entity với Status cũ (Unpaid). Giống pattern đã fix trong SplitBillService.
            // session.Members có thể null nếu test truyền entity rỗng — null-safe.
            if (session.Members is not null)
            {
                foreach (var m in session.Members)
                {
                    if (m is not null)
                    {
                        _dbContext.Entry(m).State = EntityState.Detached;
                    }
                }
            }
            _dbContext.Entry(session).State = EntityState.Detached;

            // Lifecycle cleanup: close lobby (in transaction with status update).
            // GAP-08 Fix: wrap trong try/catch — fail vẫn commit payment.
            try
            {
                await _sessionRepository.ReleaseMembersAndCloseLobbyAsync(request.OrderId, cancellationToken);
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
                await _sessionRepository.ReleaseSessionTableAndBoxAsync(request.OrderId, cancellationToken);
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

    /// <summary>
    /// GAP-MANUAL-PAY-RACE Fix: Atomic flip ActiveSession.Status từ Unpaid → Paid với WHERE clause.
    /// Trả về true nếu flip thành công, false nếu session đã có Status khác (race condition).
    /// InMemory provider không hỗ trợ ExecuteUpdateAsync → fallback direct mutation + SaveChanges.
    /// </summary>
    private async Task<bool> TryAtomicFlipSessionStatusAsync(
        Guid sessionId,
        GroupSessionStatus expectedStatus,
        DateTime paidAt,
        CancellationToken cancellationToken)
    {
        // InMemory provider doesn't support ExecuteUpdateAsync — fallback
        if (_dbContext.Database.ProviderName?.Contains("InMemory") == true)
        {
            var tracked = await _dbContext.ActiveSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (tracked == null || tracked.Status != expectedStatus)
            {
                return false;
            }

            tracked.Status = GroupSessionStatus.Paid;
            tracked.PaidAt = paidAt;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Real database: atomic ExecuteUpdateAsync — bypass change tracker.
        var rowsAffected = await _dbContext.ActiveSessions
            .Where(s => s.Id == sessionId && s.Status == expectedStatus)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, GroupSessionStatus.Paid)
                .SetProperty(s => s.PaidAt, paidAt),
                cancellationToken);

        return rowsAffected > 0;
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