using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BoardVerse.Services.Services;

/// <summary>
/// Wallet + BVC ledger service (Phase 1 theo BR § XXI-G).
/// Implement các BR:
///  - BR § II: tỷ lệ 1 BVC = 1.000 VND, integer, không bonus, min 10 BVC.
///  - BR § III.3: ledger append-only, idempotency key UNIQUE.
///  - BR-RISK-04: accountStatus validate trước top-up.
///  - BR-USER-LIMIT-03 cap sẽ áp dụng ở Phase 3 (reservation confirm) — không ở Phase 1.
/// </summary>
public class WalletService : IWalletService
{
    private const long BvcVndRate = 1000; // 1 BVC = 1.000 VND (BR § II.1)
    private const long MinimumTopUpVnd = 10_000; // 10 BVC (BR § II.2)
    private const int TopUpQrExpiryMinutes = 10;
    private const int TransactionHistoryDefaultPageSize = 20;
    private const int TransactionHistoryMaxPageSize = 100;

    private readonly IWalletRepository _walletRepository;
    private readonly IBvcLedgerEntryRepository _ledgerRepository;
    private readonly IBvcTopUpRequestRepository _topUpRequestRepository;
    private readonly IUserManagementRepository _userRepository;
    private readonly IPaymentGatewayService _paymentGateway;
    private readonly ISePayAccountService _sePayAccountService;
    private readonly IQrImageProxyService _qrImageProxy;
    private readonly ILogger<WalletService> _logger;
    private readonly BoardVerseDbContext _db; // GAP #13: cần cho BeginTransactionAsync
    private readonly IPaymentWebhookAuditRepository _webhookAuditRepository; // GAP 3.2: audit BVC top-up webhook

    public WalletService(
        IWalletRepository walletRepository,
        IBvcLedgerEntryRepository ledgerRepository,
        IBvcTopUpRequestRepository topUpRequestRepository,
        IUserManagementRepository userRepository,
        IPaymentGatewayService paymentGateway,
        ISePayAccountService sePayAccountService,
        IQrImageProxyService qrImageProxy,
        ILogger<WalletService> logger,
        BoardVerseDbContext db,
        IPaymentWebhookAuditRepository webhookAuditRepository)
    {
        _walletRepository = walletRepository;
        _ledgerRepository = ledgerRepository;
        _topUpRequestRepository = topUpRequestRepository;
        _userRepository = userRepository;
        _paymentGateway = paymentGateway;
        _sePayAccountService = sePayAccountService;
        _qrImageProxy = qrImageProxy;
        _logger = logger;
        _db = db;
        _webhookAuditRepository = webhookAuditRepository;
    }

    public async Task<WalletDto> GetOrCreateWalletAsync(Guid userId, bool includeHeld, CancellationToken cancellationToken = default)
    {
        var wallet = await _walletRepository.GetByUserIdAsync(userId);
        if (wallet == null)
        {
            // Validate user tồn tại trước khi tự tạo (tránh rác do lỗi upstream).
            var user = await _userRepository.GetByIdAsync(userId)
                ?? throw new NotFoundException(ApiErrorMessages.Wallet.WalletAutoCreateUserNotFound);

            wallet = new Wallet
            {
                UserId = user.Id,
                AvailableBalance = 0,
                HeldBalance = 0,
                TotalActiveDeposit = 0,
                RiskMultiplier = 1.0m,
                RiskScore = 0,
                RiskLevel = RiskLevel.Low,
                IsCoolingOff = false,
                AccountStatus = AccountStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _walletRepository.AddAsync(wallet);
            await _walletRepository.SaveChangesAsync();

            _logger.LogInformation(
                "Wallet auto-created. UserId={UserId}", userId);
        }

        return MapToDto(wallet, includeHeld);
    }

    public async Task<WalletDto> GetWalletAsync(Guid userId, bool includeHeld, CancellationToken cancellationToken = default)
    {
        var wallet = await _walletRepository.GetByUserIdAsync(userId)
            ?? throw new NotFoundException(ApiErrorMessages.Wallet.NotFound(userId));

        return MapToDto(wallet, includeHeld);
    }

    public async Task<TopUpResponseDto> CreateTopUpAsync(Guid userId, TopUpRequestDto request, CancellationToken cancellationToken = default)
    {
        ValidateTopUpRequest(request);

        // BUGFIX (subagent audit #21): Banned/Suspended user without wallet could bypass
        // BR-RISK-04 because GetOrCreateWalletAsync auto-creates with AccountStatus=Active.
        // Check User-level status BEFORE auto-creating wallet.
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new NotFoundException(ApiErrorMessages.Wallet.WalletAutoCreateUserNotFound);
        if (!user.IsActive)
        {
            throw new ForbiddenException(ApiErrorMessages.Wallet.TopUpBlockedAccount);
        }

        // Validate tài khoản không bị khóa (BR-RISK-04).
        var wallet = await GetOrCreateWalletAsync(userId, includeHeld: false);
        if (wallet.AccountStatus is AccountStatus.Suspended or AccountStatus.Banned or AccountStatus.Restricted)
        {
            throw new ForbiddenException(ApiErrorMessages.Wallet.TopUpBlockedAccount);
        }

        // Idempotency theo key — BR § XVII.1.
        // Ưu tiên lookup theo BvcTopUpRequest (chứa OrderId + Status tracking) trước,
        // fallback ledger cho backward-compat với data cũ.
        //
        // GAP-R4-A2 Fix: Race condition giữa idempotency check và INSERT.
        // 2 request cùng IdempotencyKey đến đồng thời (double-tap, 2 thiết bị) sẽ cùng pass lookup,
        // cùng gọi SePay gateway → 2 OrderId, 2 QR, 2 INSERT → user bị SePay charge 2 lần.
        // Wrap toàn bộ trong Serializable transaction + dùng unique-violation catch
        // để biết request thứ 2 đã xử lý.
        await using var topUpTx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, default);
        try
        {
            var existingTopUp = await _topUpRequestRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey);
            if (existingTopUp != null)
            {
                _logger.LogInformation(
                    "Top-up idempotent hit (BvcTopUpRequest). UserId={UserId}, Key={Key}, Bvc={Bvc}, Status={Status}",
                    userId, request.IdempotencyKey, existingTopUp.ExpectedBvc, existingTopUp.Status);

                // Reconstruct VietQR URL từ master account để proxy lại QR cho client
                // (OrderId đã được lưu lúc tạo, nhưng QrUrl không persist → build lại từ master).
                string? qrUrlForReplay = null;
                try
                {
                    var masterForReplay = await _sePayAccountService.GetRawMasterAccountAsync();
                    if (masterForReplay != null && masterForReplay.IsActive
                        && !string.IsNullOrWhiteSpace(masterForReplay.BankCode)
                        && !string.IsNullOrWhiteSpace(masterForReplay.AccountNumber))
                    {
                        qrUrlForReplay = BuildVietQrUrl(
                            masterForReplay,
                            existingTopUp.AmountVnd,
                            $"BVC-{existingTopUp.OrderId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to reconstruct VietQR URL for idempotent replay. UserId={UserId}, OrderId={OrderId}",
                        userId, existingTopUp.OrderId);
                }

                var replayBase64 = string.IsNullOrEmpty(qrUrlForReplay)
                    ? null
                    : await TryFetchQrBase64Async(qrUrlForReplay, userId, existingTopUp.OrderId);

                await topUpTx.CommitAsync(default);
                return new TopUpResponseDto
                {
                    PaymentUrl = existingTopUp.OrderId,
                    QrUrl = qrUrlForReplay,
                    QrImageBase64 = replayBase64,
                    OrderId = existingTopUp.OrderId,
                    ExpectedBvc = existingTopUp.ExpectedBvc,
                    ExpiresAt = existingTopUp.ExpiresAt,
                    IdempotencyKey = request.IdempotencyKey
                };
            }

        var bvcAmount = request.AmountVnd / BvcVndRate;
        if (bvcAmount <= 0)
        {
            // Đã được validate ở trên nhưng vẫn double-check phòng numeric edge case.
            throw new BadRequestException(ApiErrorMessages.Wallet.TopUpBelowMinimum);
        }

        // Tạo đơn qua SePay master account (luồng top-up tiền thật → BVC).
        // Dùng RAW entity (không DTO) để lấy AccountNumber nguyên gốc cho VietQR —
        // DTO đã bị MaskedAccountNumber() thay '****' vào, VietQR parser reject '**'.
        var master = await _sePayAccountService.GetRawMasterAccountAsync();
        if (master == null
            || !master.IsActive
            || string.IsNullOrWhiteSpace(master.BankCode)
            || string.IsNullOrWhiteSpace(master.AccountNumber))
        {
            // Gap 3.7: Ngoài throw PaymentException, log cảnh báo ngay tại đây để
            // Ops / admin monitor log tự động (cấu hình alert rule trên Seq/Datadog:
            // "SePayMasterAccountMissing" → page on-call). Nếu có hệ thống notification
            // (Slack/email), sẽ trigger qua cùng alert pipeline.
            _logger.LogCritical(
                "[Gap 3.7] SePay Master Account NOT CONFIGURED. Top-up BLOCKED. UserId={UserId}, Key={Key}, ActionRequired=Configure bai-master + active=true",
                userId, request.IdempotencyKey);
            throw new PaymentException(ApiErrorMessages.Payment.SePayMasterAccountNotFound);
        }

        var orderId = GenerateOrderId(userId);
        // W-07: Include full 18-char OrderId in transferContent so webhook can do exact lookup.
        // Previously used only 8-char user hash which caused collision risk.
        var transferContent = $"BVC-{orderId}";

        var gatewayRequest = new PaymentGatewayRequest
        {
            OrderId = orderId,
            Amount = request.AmountVnd,
            CustomerEmail = null,
            Description = transferContent,
            Metadata = new Dictionary<string, string?>
            {
                ["ledgerKey"] = request.IdempotencyKey,
                ["userId"] = userId.ToString(),
                ["bvcAmount"] = bvcAmount.ToString(),
                ["kind"] = "bvc_topup"
            },
            BankCode = master.BankCode!,
            AccountNumber = master.AccountNumber!,
            AccountName = master.AccountHolder ?? string.Empty
        };

        var result = await _paymentGateway.CreatePaymentAsync(gatewayRequest);
        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Top-up gateway failed. UserId={UserId}, Key={Key}, Error={Error}",
                userId, request.IdempotencyKey, result.ErrorMessage);
            // Gap 3.5: throw vẫn fail — FailureReason không lưu vì transaction rollback.
            // Đã log error đầy đủ để admin query log.
            throw new PaymentException(ApiErrorMessages.Wallet.TopUpGatewayFailed);
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl
            ?? throw new PaymentException(ApiErrorMessages.Wallet.SePayCheckoutUrlMissing);

        // Lưu tracking entity để webhook tra cứu theo OrderId.
        // Ledger entry sẽ được ghi khi SePay webhook success.
        var now = DateTime.UtcNow;
        var topUpRequest = new BvcTopUpRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderId = orderId,
            AmountVnd = request.AmountVnd,
            ExpectedBvc = bvcAmount,
            IdempotencyKey = request.IdempotencyKey,
            Status = BvcTopUpStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(TopUpQrExpiryMinutes)
        };
        await _topUpRequestRepository.AddAsync(topUpRequest);

        // GAP-R4-A2: Catch unique-violation race — request thứ 2 với cùng IdempotencyKey
        // đã được insert bởi request thứ 1 trong transaction Serializable. DB reject duplicate
        // → replay response từ request thứ 1 (đã được process).
        try
        {
            await _topUpRequestRepository.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogWarning(
                "Top-up duplicate IdempotencyKey race detected. UserId={UserId}, Key={Key}",
                userId, request.IdempotencyKey);
            await topUpTx.RollbackAsync(default);
            // Replay existing request
            // Gap 3.6: đảm bảo QrUrl fallback vẫn có khi master account unavailable.
            // Nếu qrUrlForReplay null (master off) → set QrUrl từ OrderId raw để client có thể render thủ công.
            var replayResult = await CreateTopUpAsync(userId, request);
            if (string.IsNullOrEmpty(replayResult.QrUrl) && !string.IsNullOrEmpty(existingTopUp.OrderId))
            {
                replayResult.QrUrl = $"BVC-{existingTopUp.OrderId}"; // raw identifier — FE render VietQR từ master config
            }
            return replayResult;
        }

        await topUpTx.CommitAsync(default);

        _logger.LogInformation(
            "Top-up quote created. UserId={UserId}, BvcAmount={Bvc}, OrderId={OrderId}, TopUpRequestId={TopUpRequestId}",
            userId, bvcAmount, orderId, topUpRequest.Id);

        // Gap 3.6: Proxy ảnh QR từ vietqr.app về server-side để trả Base64 cho Flutter Web (bypass CORS).
        // Fail thì vẫn trả response, chỉ thiếu QrImageBase64 — client vẫn có QrUrl để load trực tiếp.
        var qrBase64 = await TryFetchQrBase64Async(result.QrImageUrl, userId, orderId);
        var qrUrl = result.QrImageUrl ?? $"BVC-{orderId}"; // Fallback: FE render từ master config

        return new TopUpResponseDto
        {
            PaymentUrl = paymentUrl,
            QrUrl = qrUrl,
            QrImageBase64 = qrBase64,
            OrderId = orderId,
            ExpectedBvc = bvcAmount,
            ExpiresAt = topUpRequest.ExpiresAt,
            IdempotencyKey = request.IdempotencyKey
        };
        }
        catch (Exception ex) when (ex is not PaymentException and not BadRequestException and not ForbiddenException)
        {
            // GAP-R4-A2: Rollback khi có lỗi không mong đợi trong Serializable transaction.
            _logger.LogError(ex, "Top-up failed. UserId={UserId}, Key={Key}", userId, request.IdempotencyKey);
            await topUpTx.RollbackAsync(default);
            throw;
        }
    }

    /// <summary>
    /// GAP-R4-A2: Detect Postgres unique-violation để replay idempotent request.
    /// SqlState 23505 = unique_violation.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";
    }

    /// <summary>
    /// Player chủ động hủy đơn top-up BVC đang Pending.
    /// Set Status = Cancelled; webhook SePay tới sau sẽ bị reject tự động (BR-09).
    /// Ownership: chỉ chính chủ mới hủy được.
    /// </summary>
    public async Task CancelTopUpAsync(Guid topUpId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (topUpId == Guid.Empty)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.TopUpIdInvalid);
        }

        // Gap 3.4: Wrap trong Serializable transaction + FOR UPDATE lock BvcTopUpRequest theo Id.
        // Chống race với UpdateTopUpAmountAsync — 2 request đồng thời có thể cùng thấy
        // Status=Pending, cùng pass check, cùng update → inconsistency.
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var topUp = await _topUpRequestRepository.GetByIdForUpdateAsync(topUpId, cancellationToken);
            if (topUp == null)
            {
                throw new NotFoundException(ApiErrorMessages.Wallet.TopUpNotFound(topUpId));
            }

            if (topUp.UserId != userId)
            {
                throw new ForbiddenException(ApiErrorMessages.Wallet.TopUpNotOwned);
            }

            if (topUp.Status != BvcTopUpStatus.Pending)
            {
                throw new ConflictException(ApiErrorMessages.Wallet.TopUpNotCancellable);
            }

            topUp.Status = BvcTopUpStatus.Cancelled;
            topUp.UpdatedAt = DateTime.UtcNow;
            await _topUpRequestRepository.UpdateAsync(topUp);
            await _topUpRequestRepository.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Top-up cancelled by user. TopUpId={TopUpId}, UserId={UserId}, OrderId={OrderId}",
                topUpId, userId, topUp.OrderId);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Player đổi số tiền đơn top-up BVC đang Pending.
    /// 1. Validate amount (min 10k, bội số 1k).
    /// 2. Set đơn cũ = Cancelled.
    /// 3. Tạo đơn mới với SePay PaymentUrl + OrderId mới.
    /// 4. Trả về TopUpResponseDto cho đơn mới.
    /// </summary>
    public async Task<TopUpResponseDto> UpdateTopUpAmountAsync(
        Guid topUpId,
        Guid userId,
        UpdateTopUpRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (topUpId == Guid.Empty)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.TopUpIdInvalid);
        }

        ValidateTopUpAmount(request.AmountVnd);
        ValidateIdempotencyKey(request.IdempotencyKey);

        long oldExpectedBvc = 0; // Captured trong try-block để dùng sau commit.

        // Gap 3.4: Wrap trong Serializable transaction + FOR UPDATE lock BvcTopUpRequest theo Id.
        // Chống race condition với CancelTopUpAsync + duplicate UpdateTopUpAmountAsync calls.
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var existing = await _topUpRequestRepository.GetByIdForUpdateAsync(topUpId, cancellationToken);
            if (existing == null)
            {
                throw new NotFoundException(ApiErrorMessages.Wallet.TopUpNotFound(topUpId));
            }

            if (existing.UserId != userId)
            {
                throw new ForbiddenException(ApiErrorMessages.Wallet.TopUpNotOwned);
            }

            if (existing.Status != BvcTopUpStatus.Pending)
            {
                throw new ConflictException(ApiErrorMessages.Wallet.TopUpNotUpdateable);
            }

            // Idempotency: nếu IdempotencyKey mới trùng key đơn khác đang Pending → dùng đơn đó.
            var conflictByKey = await _topUpRequestRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey);
            if (conflictByKey != null && conflictByKey.Id != topUpId)
            {
                throw new ConflictException(
                    ApiErrorMessages.Wallet.TopUpIdempotencyKeyConflict(conflictByKey.Id));
            }

            // Đánh dấu đơn cũ = Cancelled.
            existing.Status = BvcTopUpStatus.Cancelled;
            existing.UpdatedAt = DateTime.UtcNow;
            await _topUpRequestRepository.UpdateAsync(existing);
            await _topUpRequestRepository.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            // Capture giá trị cần thiết trước khi ra khỏi block (fix CS0103 'existing' does not exist).
            oldExpectedBvc = existing.ExpectedBvc;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
        // Note: tx đã commit (early-release lock) sau khi đánh dấu đơn cũ Cancelled.
        // Phần tạo đơn mới bên dưới có thể chạy ngoài transaction (no need lock nữa vì đơn cũ đã terminal).

        // Tạo đơn mới (logic giống CreateTopUpAsync từ bước gọi SePay).
        // Skip validate account-status vì đơn cũ đã pass; skip idempotency-key lookup
        // vì key đã được check ở trên.
        var bvcAmount = request.AmountVnd / BvcVndRate;
        if (bvcAmount <= 0)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.TopUpBelowMinimum);
        }

        var master = await _sePayAccountService.GetRawMasterAccountAsync();
        if (master == null
            || !master.IsActive
            || string.IsNullOrWhiteSpace(master.BankCode)
            || string.IsNullOrWhiteSpace(master.AccountNumber))
        {
            throw new PaymentException(ApiErrorMessages.Payment.SePayMasterAccountNotFound);
        }

        var orderId = GenerateOrderId(userId);
        // W-07: Include full 18-char OrderId in transferContent so webhook can do exact lookup.
        // Previously used only 8-char user hash which caused collision risk.
        var transferContent = $"BVC-{orderId}";

        var gatewayRequest = new PaymentGatewayRequest
        {
            OrderId = orderId,
            Amount = request.AmountVnd,
            CustomerEmail = null,
            Description = transferContent,
            Metadata = new Dictionary<string, string?>
            {
                ["ledgerKey"] = request.IdempotencyKey,
                ["userId"] = userId.ToString(),
                ["bvcAmount"] = bvcAmount.ToString(),
                ["kind"] = "bvc_topup",
                ["replacesTopUpId"] = topUpId.ToString()
            },
            BankCode = master.BankCode!,
            AccountNumber = master.AccountNumber!,
            AccountName = master.AccountHolder ?? string.Empty
        };

        var result = await _paymentGateway.CreatePaymentAsync(gatewayRequest);
        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Top-up update gateway failed. UserId={UserId}, Key={Key}, Error={Error}",
                userId, request.IdempotencyKey, result.ErrorMessage);
            throw new PaymentException(ApiErrorMessages.Wallet.TopUpGatewayFailed);
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl
            ?? throw new PaymentException(ApiErrorMessages.Wallet.SePayCheckoutUrlMissing);

        var now = DateTime.UtcNow;
        var newTopUp = new BvcTopUpRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderId = orderId,
            AmountVnd = request.AmountVnd,
            ExpectedBvc = bvcAmount,
            IdempotencyKey = request.IdempotencyKey,
            Status = BvcTopUpStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(TopUpQrExpiryMinutes)
        };
        await _topUpRequestRepository.AddAsync(newTopUp);
        await _topUpRequestRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Top-up amount updated. OldTopUpId={OldTopUpId}, NewTopUpId={NewTopUpId}, UserId={UserId}, OldBvc={OldBvc}, NewBvc={NewBvc}, OrderId={OrderId}",
            topUpId, newTopUp.Id, userId, oldExpectedBvc, bvcAmount, orderId);

        var qrBase64 = await TryFetchQrBase64Async(result.QrImageUrl, userId, orderId);

        return new TopUpResponseDto
        {
            PaymentUrl = paymentUrl,
            QrUrl = result.QrImageUrl,
            QrImageBase64 = qrBase64,
            OrderId = orderId,
            ExpectedBvc = bvcAmount,
            ExpiresAt = newTopUp.ExpiresAt,
            IdempotencyKey = request.IdempotencyKey
        };
    }

    /// <summary>
    /// Proxy ảnh QR từ vietqr.app về Base64. Fail thì trả null (không throw) để không block flow.
    /// </summary>
    private async Task<string?> TryFetchQrBase64Async(string? qrUrl, Guid userId, string orderId)
    {
        if (string.IsNullOrWhiteSpace(qrUrl))
        {
            return null;
        }

        try
        {
            var base64 = await _qrImageProxy.FetchAsBase64Async(qrUrl);
            if (base64 == null)
            {
                _logger.LogInformation(
                    "QR image proxy unavailable, returning response without QrImageBase64. UserId={UserId}, OrderId={OrderId}",
                    userId, orderId);
            }
            return base64;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Unexpected error fetching QR base64. UserId={UserId}, OrderId={OrderId}",
                userId, orderId);
            return null;
        }
    }

    /// <summary>
    /// Build lại VietQR URL từ SePay master account + amount + description.
    /// Dùng cho idempotent replay (BvcTopUpRequest không persist QrUrl).
    /// </summary>
    private static string BuildVietQrUrl(SePayAccount master, long amountVnd, string description)
    {
        var parts = new List<string>
        {
            $"bank={Uri.EscapeDataString(master.BankCode!.Trim())}",
            $"acc={Uri.EscapeDataString(master.AccountNumber!.Trim())}",
            "template=compact",
            $"amount={(int)amountVnd}",
            "showinfo=true",
            "fullacc=true",
            $"des={Uri.EscapeDataString(description.Trim())}"
        };
        if (!string.IsNullOrWhiteSpace(master.AccountHolder))
        {
            parts.Add($"holder={Uri.EscapeDataString(master.AccountHolder.Trim())}");
        }
        return $"https://vietqr.app/img?{string.Join("&", parts)}";
    }

    public async Task<BvcTransactionPageDto> GetTransactionsAsync(Guid userId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = TransactionHistoryDefaultPageSize;
        if (pageSize > TransactionHistoryMaxPageSize) pageSize = TransactionHistoryMaxPageSize;

        // Wallet chỉ cần tồn tại khi user đã có ledger. Nếu chưa có → trả rỗng.
        var wallet = await _walletRepository.GetByUserIdAsync(userId);
        if (wallet == null)
        {
            return new BvcTransactionPageDto
            {
                Items = [],
                Page = page,
                PageSize = pageSize,
                TotalItems = 0
            };
        }

        var total = await _ledgerRepository.CountByUserAsync(userId);
        var items = await _ledgerRepository.GetHistoryAsync(userId, page, pageSize);

        return new BvcTransactionPageDto
        {
            Items = items.Select(MapLedgerToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task<BvcHoldResult> HoldDepositAsync(
        Guid userId,
        long amountBvc,
        Guid? relatedLobbyId,
        Guid? relatedReservationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await ApplyBalanceMutationAsync(
            userId,
            amountBvc,
            LedgerEntryType.DepositHold,
            relatedLobbyId,
            relatedReservationId,
            idempotencyKey,
            (w, amt) =>
            {
                if (w.AvailableBalance < amt)
                {
                    throw new BadRequestException(ApiErrorMessages.Reservation.InsufficientAvailableBalance(
                        w.AvailableBalance, amt));
                }
                w.AvailableBalance -= amt;
                w.HeldBalance += amt;
                w.TotalActiveDeposit += amt;
            },
            cancellationToken);
        return await BuildResultAsync(userId, idempotencyKey, cancellationToken);
    }

    public async Task<BvcHoldResult> ReleaseDepositAsync(
        Guid userId,
        long amountBvc,
        Guid? relatedLobbyId,
        Guid? relatedReservationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // Skip if nothing to release (e.g. merged reservation already processed, depositAmount = 0).
        if (amountBvc == 0) return await BuildResultAsync(userId, idempotencyKey, cancellationToken);

        await ApplyBalanceMutationAsync(
            userId,
            amountBvc,
            LedgerEntryType.DepositRelease,
            relatedLobbyId,
            relatedReservationId,
            idempotencyKey,
            (w, amt) =>
            {
                if (w.HeldBalance < amt)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Wallet.HeldBalanceInsufficient(w.HeldBalance, amt));
                }
                w.HeldBalance -= amt;
                w.AvailableBalance += amt;
                w.TotalActiveDeposit = Math.Max(0, w.TotalActiveDeposit - amt);
            },
            cancellationToken);
        return await BuildResultAsync(userId, idempotencyKey, cancellationToken);
    }

    public async Task UpdateLedgerLobbyIdAsync(
        Guid userId,
        Guid relatedReservationId,
        Guid newLobbyId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var ownsTransaction = _db.Database.CurrentTransaction is null;
        IDbContextTransaction? ownedTx = null;

        if (ownsTransaction)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
        }

        try
        {
            // Lookup ledger entry by relatedReservationId + type DEPOSIT_HOLD.
            // Không dùng idempotency key vì key gốc đã dùng rồi (của HoldDepositAsync).
            // Dùng pattern idempotency key mới: "lobby-bound-{lobbyId}".
            var existing = await _ledgerRepository.GetByIdempotencyKeyAsync(idempotencyKey);
            if (existing != null)
            {
                // Already updated — idempotent replay, skip.
                if (ownedTx != null)
                {
                    await ownedTx.CommitAsync(cancellationToken);
                }
                return;
            }

            // Tìm ledger entry DEPOSIT_HOLD theo reservationId.
            var ledgerEntry = await _db.BvcLedgerEntries
                .Where(e => e.UserId == userId
                    && e.Type == LedgerEntryType.DepositHold
                    // TD-02 fix: đổi từ RelatedBookingId (legacy) sang RelatedReservationId (Reservation flow).
                    && e.RelatedReservationId == relatedReservationId)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (ledgerEntry == null)
            {
                _logger.LogWarning(
                    "UpdateLedgerLobbyIdAsync: no DEPOSIT_HOLD ledger entry found for ReservationId={ReservationId}, UserId={UserId}",
                    relatedReservationId, userId);
            }
            else
            {
                ledgerEntry.RelatedLobbyId = newLobbyId;
                ledgerEntry.IdempotencyKey = idempotencyKey;
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Updated ledger entry '{LedgerEntryId}' with RelatedLobbyId='{LobbyId}'. ReservationId={ReservationId}",
                    ledgerEntry.Id, newLobbyId, relatedReservationId);
            }

            if (ownedTx != null)
            {
                await ownedTx.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (ownedTx != null)
            {
                await ownedTx.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    public async Task<BvcHoldResult> CaptureDepositAsync(
        Guid userId,
        long amountBvc,
        Guid? relatedLobbyId,
        Guid? relatedReservationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await ApplyBalanceMutationAsync(
            userId,
            amountBvc,
            LedgerEntryType.DepositCapture,
            relatedLobbyId,
            relatedReservationId,
            idempotencyKey,
            (w, amt) =>
            {
                if (w.HeldBalance < amt)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Wallet.HeldBalanceInsufficientForCapture(w.HeldBalance, amt));
                }
                w.HeldBalance -= amt;
                w.TotalActiveDeposit = Math.Max(0, w.TotalActiveDeposit - amt);
            },
            cancellationToken);
        return await BuildResultAsync(userId, idempotencyKey, cancellationToken);
    }

    public async Task<BvcHoldResult> ForfeitDepositAsync(
        Guid userId,
        long amountBvc,
        Guid? relatedLobbyId,
        Guid? relatedReservationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // Skip if nothing to forfeit (e.g. merged reservation already processed, depositAmount = 0).
        if (amountBvc == 0) return await BuildResultAsync(userId, idempotencyKey, cancellationToken);

        await ApplyBalanceMutationAsync(
            userId,
            amountBvc,
            LedgerEntryType.DepositForfeit,
            relatedLobbyId,
            relatedReservationId,
            idempotencyKey,
            (w, amt) =>
            {
                if (w.HeldBalance < amt)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Wallet.HeldBalanceInsufficientForForfeit(w.HeldBalance, amt));
                }
                w.HeldBalance -= amt;
                w.TotalActiveDeposit = Math.Max(0, w.TotalActiveDeposit - amt);
            },
            cancellationToken);
        return await BuildResultAsync(userId, idempotencyKey, cancellationToken);
    }

    /// <summary>
    /// M1 / Option A: Refund per-member deposit về wallet khi member merge sang lobby khác (Exception 4).
    /// Di chuyển heldBalance → availableBalance, ghi ledger DEPOSIT_REFUND_MERGE.
    /// docs/design/host-deposit-discount-and-bvc-payment-design.md §B3.3.
    /// </summary>
    /// <remarks>
    /// Tại sao KHÔNG dùng <see cref="ReleaseDepositAsync"/>:
    /// - Release dùng cho host cancel / timeout → có thể áp forfeit policy (BR-REFUND-02/03).
    /// - Refund_Merge 100% cho member (Option A — khuyến nghị, không forfeit).
    /// - Audit trail khác biệt: merge refund có <c>FromSessionId/ToSessionId</c> trong MemberDepositAuditLog,
    ///   release thì chỉ có <c>RelatedReservationId</c>.
    ///
    /// Validation flow (idempotent):
    /// 1. Skip nếu amount = 0 (BR-22 per-member deposit chưa active → member.DepositId null → caller pass 0).
    /// 2. Lookup ledger entry theo idempotencyKey. Nếu đã có → return existing entry.
    /// 3. Lock wallet FOR UPDATE, validate HeldBalance >= amount.
    /// 4. heldBalance -= amount; availableBalance += amount; totalActiveDeposit -= amount.
    /// 5. Insert ledger entry DEPOSIT_REFUND_MERGE.
    /// </remarks>
    public async Task<BvcLedgerEntry> RefundMemberDepositOnMergeAsync(
        Guid userId,
        long amountBvc,
        Guid depositId,
        string idempotencyKey,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        // BR-22 forward-compat: nếu member chưa có per-member deposit → skip silently.
        // Caller đã check member.DepositId != null trước khi gọi, nhưng guard thêm 1 lớp.
        if (amountBvc <= 0)
        {
            _logger.LogInformation(
                "RefundMemberDepositOnMergeAsync: skip amount=0. UserId={UserId}, DepositId={DepositId}",
                userId, depositId);
            // Trả 1 entry "noop" để caller có thể update member.DepositRefundLedgerId mà không null.
            // Actually trả null để caller check và skip luôn.
            return null!;
        }

        await ApplyBalanceMutationAsync(
            userId,
            amountBvc,
            LedgerEntryType.DepositRefund_Merge,
            relatedLobbyId: null,    // Không biết lobby tại thời điểm refund (member đã rời)
            relatedReservationId: null,
            idempotencyKey,
            (w, amt) =>
            {
                if (w.HeldBalance < amt)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Wallet.HeldBalanceInsufficient(w.HeldBalance, amt));
                }
                // Move held → available (giống Release nhưng KHÔNG trừ TotalActiveDeposit nếu BR-22 thật sự có active deposit cho member này).
                // BR-22 forward-compat: TotalActiveDeposit mirror tổng held across sessions.
                // Khi refund merge → giảm held nhưng vẫn mirror tổng.
                w.HeldBalance -= amt;
                w.AvailableBalance += amt;
                w.TotalActiveDeposit = Math.Max(0, w.TotalActiveDeposit - amt);
            },
            cancellationToken);

        // Set note với context merge (sau khi ledger đã insert, update Note là append-only OK vì update field note).
        var entry = await _ledgerRepository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (entry != null && !string.IsNullOrWhiteSpace(notes))
        {
            entry.Note = notes;
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "M1 / Option A: Refunded member deposit on merge. UserId={UserId}, Amount={Amount}BVC, DepositId={DepositId}, IdempotencyKey={Key}",
            userId, amountBvc, depositId, idempotencyKey);

        return entry!;
    }

    /// <summary>
    /// W-07: Resolve OrderId from SePay webhook transferContent.
    /// Uses exact OrderId lookup instead of fragile 8-char hash prefix matching.
    /// Idempotent: cùng OrderId + success → chỉ cộng ví 1 lần.
    /// </summary>
    /// <param name="orderId">18-char hex OrderId extracted from transferContent.</param>
    public async Task<string?> FindPendingTopUpOrderIdAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length != 18)
        {
            return null;
        }

        var pending = await _topUpRequestRepository.GetPendingByExactOrderIdAsync(orderId, cancellationToken);
        return pending?.OrderId;
    }

    /// <summary>
    /// Lookup BvcTopUpRequest theo OrderId cho player hiện tại.
    /// Trả null nếu OrderId rỗng / không tồn tại / không thuộc user.
    /// </summary>
    public async Task<BvcTopUpRequest?> GetTopUpByOrderIdForUserAsync(
        string orderId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

        // OrderId được lưu dạng "BVC-{18hex}" trong DB.
        // Nếu client gửi raw 18-char hex, lookup exact; nếu gửi cả prefix, lookup exact vẫn OK.
        var topUp = await _topUpRequestRepository.GetByOrderIdAsync(orderId, cancellationToken);
        if (topUp == null)
        {
            // Fallback: thử lookup exact 18-char nếu client gửi full "BVC-XXXX".
            var rawHex = orderId.StartsWith("BVC-", StringComparison.OrdinalIgnoreCase)
                ? orderId["BVC-".Length..]
                : orderId;
            if (rawHex.Length == 18)
            {
                var byHex = await _topUpRequestRepository.GetPendingByExactOrderIdAsync(rawHex, cancellationToken);
                topUp = byHex;
            }
        }

        if (topUp == null || topUp.UserId != userId)
        {
            return null;
        }

        return topUp;
    }

    /// <summary>
    /// Proxy ảnh QR cho đơn top-up theo OrderId. Dùng cho endpoint fallback.
    /// Re-construct VietQR URL từ SePay master (BvcTopUpRequest không persist QrUrl),
    /// sau đó fetch base64 từ vietqr.app server-side.
    /// </summary>
    public async Task<Stream?> GetTopUpQrImageStreamAsync(
        string orderId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var topUp = await GetTopUpByOrderIdForUserAsync(orderId, userId, cancellationToken);
        if (topUp == null)
        {
            return null;
        }

        var master = await _sePayAccountService.GetRawMasterAccountAsync();
        if (master == null || !master.IsActive
            || string.IsNullOrWhiteSpace(master.BankCode)
            || string.IsNullOrWhiteSpace(master.AccountNumber))
        {
            _logger.LogWarning(
                "GetTopUpQrImageStreamAsync: SePay master not configured. UserId={UserId}, OrderId={OrderId}",
                userId, orderId);
            return null;
        }

        var qrUrl = BuildVietQrUrl(master, topUp.AmountVnd, $"BVC-{topUp.OrderId}");
        return await _qrImageProxy.FetchAsStreamAsync(qrUrl, cancellationToken);
    }

    /// <summary>
    /// Phase 2: Xử lý SePay webhook cho BVC top-up (OrderId prefix BVC-XXX).
    /// Idempotent theo OrderId. Cùng OrderId + success → chỉ cộng ví 1 lần.
    /// </summary>
    /// <param name="amountBvc">Số BVC thực tế nhận (tính từ webhook amount).</param>
    /// <param name="status">success/failed/cancelled.</param>
    public async Task HandleTopUpWebhookAsync(
        string orderId,
        string gatewayTransactionId,
        long amountBvc,
        string status,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            _logger.LogWarning("Top-up webhook missing OrderId.");
            return;
        }

        if (amountBvc <= 0)
        {
            _logger.LogWarning(
                "Top-up webhook amount invalid. OrderId={OrderId}, Amount={Amount}",
                orderId, amountBvc);
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Lock BvcTopUpRequest theo OrderId (cluster-safe).
            var topUp = await _topUpRequestRepository.GetByOrderIdAsync(orderId, cancellationToken);
            if (topUp == null)
            {
                _logger.LogWarning(
                    "Top-up webhook OrderId not found. OrderId={OrderId}, GatewayTxn={GatewayTxn}",
                    orderId, gatewayTransactionId);
                // Gap 3.2: ghi audit record cho OrderId-not-found để trace raw webhook không map được.
                await TryWriteTopUpAuditAsync(orderId, gatewayTransactionId, amountBvc, status,
                    result: "order_not_found",
                    detail: "BvcTopUpRequest không tồn tại với OrderId này.",
                    isSuccess: false, errorMessage: "OrderId not found", cancellationToken);
                await tx.RollbackAsync(cancellationToken);
                return;
            }

            // Idempotency: nếu đã Paid/Failed/Expired → skip.
            if (topUp.Status != BvcTopUpStatus.Pending)
            {
                _logger.LogInformation(
                    "Top-up webhook duplicate (already terminal). OrderId={OrderId}, Status={Status}",
                    orderId, topUp.Status);
                // Gap 3.2: ghi audit record cho duplicate replay.
                await TryWriteTopUpAuditAsync(orderId, gatewayTransactionId, amountBvc, status,
                    result: "already_terminal",
                    detail: $"BvcTopUpRequest.Status={topUp.Status} (skip idempotent)",
                    isSuccess: true, errorMessage: null, cancellationToken);
                await tx.RollbackAsync(cancellationToken);
                return;
            }

            var normalized = status?.Trim().ToLowerInvariant() ?? string.Empty;
            var now = DateTime.UtcNow;

            if (normalized is "success" or "paid")
            {
                // Amount mismatch → log + skip (BR §V.3).
                if (amountBvc != topUp.ExpectedBvc)
                {
                    _logger.LogWarning(
                        "Top-up webhook amount mismatch. OrderId={OrderId}, Expected={Expected}, Received={Received}",
                        orderId, topUp.ExpectedBvc, amountBvc);
                    // Gap 3.2 + 3.5: ghi audit + đánh BvcTopUpRequest.Status=Failed + FailureReason.
                    var mismatchReason = TruncateFailureReason(
                        $"amount_mismatch expected={topUp.ExpectedBvc} received={amountBvc}");
                    topUp.Status = BvcTopUpStatus.Failed;
                    topUp.FailureReason = mismatchReason;
                    topUp.GatewayTransactionId = gatewayTransactionId;
                    topUp.UpdatedAt = now;
                    await _topUpRequestRepository.UpdateAsync(topUp);

                    await TryWriteTopUpAuditAsync(orderId, gatewayTransactionId, amountBvc, status,
                        result: "amount_mismatch",
                        detail: $"Expected={topUp.ExpectedBvc} BVC, received={amountBvc} BVC",
                        isSuccess: false, errorMessage: mismatchReason, cancellationToken);

                    await tx.CommitAsync(cancellationToken);
                    return;
                }

                // Cộng ví — dùng ApplyBalanceMutationAsync (idempotent ledger theo IdempotencyKey).
                await ApplyBalanceMutationAsync(
                    topUp.UserId,
                    amountBvc,
                    LedgerEntryType.TopUp,
                    relatedLobbyId: null,
                    relatedReservationId: null,
                    idempotencyKey: topUp.IdempotencyKey,
                    mutate: (w, amt) =>
                    {
                        w.AvailableBalance += amt;
                    },
                    cancellationToken);

                // Tìm ledger entry vừa tạo để update topUp.LedgerEntryId.
                var ledgerEntry = await _ledgerRepository.GetByIdempotencyKeyAsync(topUp.IdempotencyKey);
                topUp.LedgerEntryId = ledgerEntry?.Id;
                topUp.GatewayTransactionId = gatewayTransactionId;
                topUp.PaidAt = now;
                topUp.Status = BvcTopUpStatus.Paid;
                topUp.UpdatedAt = now;
                await _topUpRequestRepository.UpdateAsync(topUp);

                // Gap 3.2: ghi audit success trước commit.
                await TryWriteTopUpAuditAsync(orderId, gatewayTransactionId, amountBvc, status,
                    result: "success",
                    detail: $"UserId={topUp.UserId}, LedgerEntryId={ledgerEntry?.Id}",
                    isSuccess: true, errorMessage: null, cancellationToken);

                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Top-up webhook success applied. OrderId={OrderId}, UserId={UserId}, Bvc={Bvc}",
                    orderId, topUp.UserId, amountBvc);
            }
            else if (normalized is "failed" or "canceled" or "cancelled")
            {
                // Gap 3.5: ghi failure reason vào BvcTopUpRequest.FailureReason để debug + audit.
                var reasonText = TruncateFailureReason($"Webhook status={normalized}");
                topUp.Status = BvcTopUpStatus.Failed;
                topUp.GatewayTransactionId = gatewayTransactionId;
                topUp.FailureReason = reasonText;
                topUp.UpdatedAt = now;
                await _topUpRequestRepository.UpdateAsync(topUp);

                // Gap 3.2: ghi audit webhook (failed/cancelled).
                await TryWriteTopUpAuditAsync(orderId, gatewayTransactionId, amountBvc, status,
                    result: normalized == "failed" ? "webhook_failed" : "webhook_cancelled",
                    detail: reasonText ?? "Unknown reason",
                    isSuccess: false, errorMessage: normalized, cancellationToken);

                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Top-up webhook failed/cancelled. OrderId={OrderId}, Status={Status}, Reason={Reason}",
                    orderId, normalized, reasonText);
            }
            else
            {
                _logger.LogWarning(
                    "Top-up webhook unknown status. OrderId={OrderId}, Status={Status}. Ignored.",
                    orderId, normalized);
                // Gap 3.2: ghi audit cho unknown status.
                await TryWriteTopUpAuditAsync(orderId, gatewayTransactionId, amountBvc, status,
                    result: "unknown_status", detail: $"Status={normalized}",
                    isSuccess: false, errorMessage: "Unknown status", cancellationToken);
                await tx.RollbackAsync(cancellationToken);
            }
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Gap 3.2: Ghi PaymentWebhookAudit cho BVC top-up webhook.
    /// Best-effort — nuốt exception để không block webhook flow chính (gap 4.4 hardening).
    /// </summary>
    private async Task TryWriteTopUpAuditAsync(
        string orderId,
        string gatewayTransactionId,
        long amountBvc,
        string rawStatus,
        string result,
        string detail,
        bool isSuccess,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var audit = new PaymentWebhookAudit
            {
                Id = Guid.NewGuid(),
                Endpoint = "wallet/topup-webhook",
                OrderId = orderId,
                GatewayTransactionId = string.IsNullOrWhiteSpace(gatewayTransactionId) ? null : gatewayTransactionId,
                Amount = amountBvc * 1000m, // BVC → VND
                Currency = "BVC",
                Status = rawStatus ?? string.Empty,
                Result = result,
                Detail = TruncateFailureReason(detail),
                Payload = $"{{\"orderId\":\"{orderId}\",\"amountBvc\":{amountBvc},\"status\":\"{rawStatus}\"}}",
                ProcessedAt = DateTime.UtcNow,
                ProcessedBy = "WalletService.HandleTopUpWebhookAsync",
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage
            };
            // AddAsync tự SaveChangesAsync. Không cần gọi riêng.
            await _webhookAuditRepository.AddAsync(audit, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write PaymentWebhookAudit for BVC top-up. OrderId={OrderId}, Result={Result}",
                orderId, result);
        }
    }

    /// <summary>
    /// Admin/support tặng/trừ BVC thủ công (compensation, penalty, manual refund).
    /// Ghi ledger entry AdminCredit (+) hoặc AdminDebit (-), KHÔNG qua SePay.
    /// Idempotent theo <paramref name="idempotencyKey"/>.
    /// </summary>
    public async Task<BvcHoldResult> AdminAdjustBalanceAsync(
        Guid targetUserId,
        long amountBvc,
        bool isCredit,
        Guid adminUserId,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (amountBvc <= 0)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.AmountMustBePositive);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.AdjustmentReasonRequired);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        // Đảm bảo ví tồn tại.
        await GetOrCreateWalletAsync(targetUserId, includeHeld: false);

        var ledgerType = isCredit ? LedgerEntryType.AdminCredit : LedgerEntryType.AdminDebit;
        var directionSymbol = isCredit ? "+" : "-";

        await ApplyBalanceMutationAsync(
            targetUserId,
            amountBvc,
            ledgerType,
            relatedLobbyId: null,
            relatedReservationId: null,
            idempotencyKey,
            (w, amt) =>
            {
                if (isCredit)
                {
                    w.AvailableBalance += amt;
                }
                else
                {
                    if (w.AvailableBalance < amt)
                    {
                        throw new BadRequestException(
                            ApiErrorMessages.Wallet.AvailableBalanceInsufficient(w.AvailableBalance, amt));
                    }
                    w.AvailableBalance -= amt;
                }
            },
            cancellationToken);

        // Ghi note vào ledger entry (audit trail).
        // ApplyBalanceMutationAsync đã tạo entry với Note = null → update note sau.
        var ledgerEntry = await _ledgerRepository.GetByIdempotencyKeyAsync(idempotencyKey);
        if (ledgerEntry != null)
        {
            ledgerEntry.Note = $"[Admin:{adminUserId}] {directionSymbol}{amountBvc} BVC — {reason}";
            await _walletRepository.SaveChangesAsync(); // ledger + wallet share DbContext
        }

        _logger.LogWarning(
            "Admin BVC adjustment. AdminUserId={AdminUserId}, TargetUserId={TargetUserId}, Amount={Amount}, IsCredit={IsCredit}, Reason={Reason}",
            adminUserId, targetUserId, amountBvc, isCredit, reason);

        return await BuildResultAsync(targetUserId, idempotencyKey, cancellationToken);
    }

    /// <summary>
    /// Background job: expire các BVC top-up request status=Pending quá ExpiresAt (mặc định 30 phút).
    /// Idempotent: chỉ chuyển status Pending → Expired, KHÔNG cộng/trừ ví (vì chưa nhận tiền thật).
    /// Cluster-safe: batch transaction + FOR UPDATE SKIP LOCKED.
    /// </summary>
    public async Task<int> ExpirePendingTopUpsAsync(CancellationToken cancellationToken = default)
    {
        const int BatchSize = 50;
        var now = DateTime.UtcNow;

        // Batch transaction cho FOR UPDATE SKIP LOCKED.
        // Mỗi tick load tối đa 50 expired → tránh long-running tx.
        await using var batchTx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var expiredRequests = await _topUpRequestRepository.GetPendingExpiredAsync(now, BatchSize);

        try
        {
            foreach (var topUp in expiredRequests)
            {
                topUp.Status = BvcTopUpStatus.Expired;
                topUp.UpdatedAt = now;
                await _topUpRequestRepository.UpdateAsync(topUp);
            }

            if (expiredRequests.Count > 0)
            {
                await _topUpRequestRepository.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "BVC top-up expiry job expired {Count} pending requests.",
                    expiredRequests.Count);
            }

            await batchTx.CommitAsync(cancellationToken);
            return expiredRequests.Count;
        }
        catch
        {
            await batchTx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Inner mutate method — KHÔNG begin transaction, KHÔNG retry.
    /// Phải gọi trong 1 transaction do caller quản lý (BR §17.4).
    /// Idempotent: nếu entry với key này đã tồn tại → return (no double-mutate).
    /// </summary>
    private async Task ApplyBalanceMutationAsync(
        Guid userId,
        long amountBvc,
        LedgerEntryType ledgerType,
        Guid? relatedLobbyId,
        Guid? relatedReservationId,
        string idempotencyKey,
        Action<Wallet, long> mutate,
        CancellationToken cancellationToken)
    {
        if (amountBvc <= 0)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.AmountMustBePositive);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        // GAP #22 fix: detect outer transaction. Nếu caller đã wrap transaction
        // (vd: ReservationService.ConfirmAsync) → chỉ mutate, KHÔNG begin mới.
        // Nếu chưa có → wrap riêng (cho path standalone như CancelOutsideTransaction).
        var ownsTransaction = _db.Database.CurrentTransaction is null;
        IDbContextTransaction? ownedTx = null;

        if (ownsTransaction)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
        }

        try
        {
            // Idempotency guard FOR UPDATE — lock row ledger theo key.
            // Nếu row đã tồn tại → return (idempotent), transaction commit no-op.
            // Nếu 2 request đồng thời → chỉ 1 acquire được lock, request còn lại đợi.
            var existing = await _ledgerRepository.GetByIdempotencyKeyForUpdateAsync(idempotencyKey);
            if (existing != null)
            {
                if (existing.UserId != userId || existing.Type != ledgerType || existing.Amount != amountBvc)
                {
                    throw new ConflictException(ApiErrorMessages.Reservation.IdempotencyKeyConflict);
                }

                if (ownedTx != null)
                {
                    await ownedTx.CommitAsync(cancellationToken);
                }
                return;
            }

            // Lock wallet row để tránh race condition khi concurrent hold + release.
            var wallet = await _walletRepository.GetByUserIdForUpdateAsync(userId);
            if (wallet == null)
            {
                // FIX #500: Wallet might not exist - try to create it first
                // This handles the case where wallet was deleted or never created
                _logger.LogWarning(
                    "Wallet not found for UserId={UserId} in BVC mutation. Attempting to create...",
                    userId);

                wallet = new Wallet
                {
                    UserId = userId,
                    AvailableBalance = 0,
                    HeldBalance = 0,
                    TotalActiveDeposit = 0,
                    RiskMultiplier = 1.0m,
                    RiskScore = 0,
                    RiskLevel = RiskLevel.Low,
                    IsCoolingOff = false,
                    AccountStatus = AccountStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _walletRepository.AddAsync(wallet);
                await _walletRepository.SaveChangesAsync();

                _logger.LogInformation(
                    "Auto-created wallet for UserId={UserId} during BVC mutation",
                    userId);
            }

            mutate(wallet, amountBvc);
            wallet.UpdatedAt = DateTime.UtcNow;

            var entry = new BvcLedgerEntry
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = ledgerType,
                Amount = amountBvc,
                RelatedLobbyId = relatedLobbyId,
                // TD-02 fix: gán vào RelatedReservationId (column DB), không phải RelatedBookingId (legacy).
                // Trước đây gán nhầm RelatedBookingId khiến RelatedReservationId NULL mãi mãi → ledger không link được reservation.
                RelatedReservationId = relatedReservationId,
                RelatedPaymentRef = null,
                IdempotencyKey = idempotencyKey,
                BalanceSnapshot = wallet.AvailableBalance,
                Note = null,
                CreatedAt = DateTime.UtcNow
            };

            await _walletRepository.UpdateAsync(wallet);
            await _ledgerRepository.AddAsync(entry);
            await _walletRepository.SaveChangesAsync();

            if (ownedTx != null)
            {
                await ownedTx.CommitAsync(cancellationToken);
            }

            _logger.LogInformation(
                "BVC {Type} applied. UserId={UserId}, Amount={Amount}, Available={Available}, Held={Held}, IdempotencyKey={Key}",
                ledgerType, userId, amountBvc, wallet.AvailableBalance, wallet.HeldBalance, idempotencyKey);
        }
        catch
        {
            if (ownedTx != null)
            {
                await ownedTx.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    /// <summary>
    /// Anti-flake wrapper cho path standalone (không nằm trong transaction gọi).
    /// Retry 3 lần với delay nếu bị serialization failure (Serializable isolation).
    /// </summary>
    private async Task ExecuteWithAntiFlakeAsync<T>(Func<Task<T>> action, Guid userId, string idempotencyKey)
    {
        const int MaxRetries = 3;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "WalletService serialization failure attempt {Attempt}/{Max}. UserId={UserId}, Key={Key}. Retrying...",
                    attempt, MaxRetries, userId, idempotencyKey);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));
            }
        }
    }

    private static bool IsSerializationFailure(DbUpdateException ex)
    {
        // Postgres SQLSTATE 40001 = serialization_failure (Serializable + optimistic conflict).
        // 40P01 = deadlock_detected.
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("40001", StringComparison.Ordinal)
            || msg.Contains("40P01", StringComparison.Ordinal)
            || msg.Contains("could not serialize", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("deadlock", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<BvcHoldResult> BuildResultAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var wallet = await _walletRepository.GetByUserIdAsync(userId)
            ?? throw new NotFoundException(ApiErrorMessages.Wallet.NotFoundForUser(userId));

        var entry = await _ledgerRepository.GetByIdempotencyKeyAsync(idempotencyKey);

        return new BvcHoldResult
        {
            LedgerEntryId = entry?.Id ?? Guid.Empty,
            NewAvailableBalance = wallet.AvailableBalance,
            NewHeldBalance = wallet.HeldBalance,
            BalanceSnapshot = wallet.AvailableBalance,
            WasIdempotentReplay = false
        };
    }

    private static void ValidateTopUpRequest(TopUpRequestDto request)
    {
        ValidateTopUpAmount(request.AmountVnd);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }
    }

    private static void ValidateTopUpAmount(long amountVnd)
    {
        if (amountVnd < MinimumTopUpVnd)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.TopUpBelowMinimum);
        }
        if (amountVnd % BvcVndRate != 0)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.TopUpInvalidMultiple);
        }
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }
    }

    private static WalletDto MapToDto(Wallet wallet, bool includeHeld)
    {
        return new WalletDto
        {
            UserId = wallet.UserId,
            AvailableBalance = wallet.AvailableBalance,
            HeldBalance = includeHeld ? wallet.HeldBalance : null,
            RiskMultiplier = wallet.RiskMultiplier,
            RiskLevel = wallet.RiskLevel,
            IsCoolingOff = wallet.IsCoolingOff,
            AccountStatus = wallet.AccountStatus
        };
    }

    private static BvcTransactionDto MapLedgerToDto(BvcLedgerEntry entry)
    {
        return new BvcTransactionDto
        {
            Id = entry.Id,
            Type = entry.Type,
            Amount = entry.Amount,
            RelatedLobbyId = entry.RelatedLobbyId,
            RelatedBookingId = entry.RelatedBookingId,
            // TD-02 fix: project RelatedReservationId để mobile nhận được FK reservation.
            RelatedReservationId = entry.RelatedReservationId,
            RelatedPaymentRef = entry.RelatedPaymentRef,
            BalanceSnapshot = entry.BalanceSnapshot,
            // GAP-R6-WAL-PII Fix: redact PII (email/phone) in Note before returning to client.
            // Ledger có thể chứa raw email/phone từ các integration cũ. Truncate email local-part + mask phone.
            Note = PiiMasker.MaskNote(entry.Note),
            CreatedAt = entry.CreatedAt
        };
    }

    // ============================================================
    // Admin methods — BR-RISK-04, BR-RISK-05, BR-RISK-06
    // ============================================================

    public async Task<AdminWalletPageDto> GetAllWalletsAsync(
        int page,
        int pageSize,
        string? searchTerm = null,
        AccountStatus? statusFilter = null,
        RiskLevel? riskLevelFilter = null, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var (wallets, totalCount) = await _walletRepository.GetAllWalletsPagedAsync(
            page, pageSize, searchTerm, statusFilter, riskLevelFilter);

        var items = wallets.Select(w => new AdminWalletSummaryDto
        {
            UserId = w.UserId,
            UserEmail = w.User?.Email,
            AvailableBalance = w.AvailableBalance,
            HeldBalance = w.HeldBalance,
            TotalActiveDeposit = w.TotalActiveDeposit,
            RiskMultiplier = w.RiskMultiplier,
            RiskLevel = w.RiskLevel,
            IsCoolingOff = w.IsCoolingOff,
            AccountStatus = w.AccountStatus,
            CreatedAt = w.CreatedAt
        }).ToList();

        return new AdminWalletPageDto
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount
        };
    }

    public async Task<AdminWalletDetailDto?> GetWalletDetailAsync(Guid userId)
    {
        var wallet = await _walletRepository.GetWalletWithUserAsync(userId);
        if (wallet == null) return null;

        return new AdminWalletDetailDto
        {
            UserId = wallet.UserId,
            UserEmail = wallet.User?.Email,
            UserPhoneNumber = wallet.User?.PhoneNumber,
            AvailableBalance = wallet.AvailableBalance,
            HeldBalance = wallet.HeldBalance,
            TotalActiveDeposit = wallet.TotalActiveDeposit,
            RiskMultiplier = wallet.RiskMultiplier,
            RiskScore = wallet.RiskScore,
            RiskLevel = wallet.RiskLevel,
            IsCoolingOff = wallet.IsCoolingOff,
            CoolingOffExpiresAt = wallet.CoolingOffExpiresAt,
            AccountStatus = wallet.AccountStatus,
            CreatedAt = wallet.CreatedAt,
            UpdatedAt = wallet.UpdatedAt
        };
    }

    public async Task<AdminUserTransactionsPageDto> GetUserTransactionsAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var wallet = await _walletRepository.GetWalletWithUserAsync(userId);
        var displayName = wallet?.User?.Email ?? userId.ToString();

        var total = await _ledgerRepository.CountByUserAsync(userId);
        var items = await _ledgerRepository.GetHistoryAsync(userId, page, pageSize);

        return new AdminUserTransactionsPageDto
        {
            UserId = userId,
            UserDisplayName = displayName,
            Items = items.Select(MapLedgerToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task<AdminSetStatusResultDto> SetAccountStatusAsync(
        Guid targetUserId,
        AccountStatus newStatus,
        string reason,
        DateTime? expiresAt,
        Guid adminUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.AdjustmentReasonRequired);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        // Lấy ví hiện tại
        var wallet = await _walletRepository.GetByUserIdAsync(targetUserId);
        if (wallet == null)
        {
            // Auto-create nếu chưa có
            await GetOrCreateWalletAsync(targetUserId, includeHeld: false);
            wallet = await _walletRepository.GetByUserIdAsync(targetUserId);
        }

        if (wallet == null)
        {
            throw new NotFoundException(ApiErrorMessages.Wallet.NotFoundForTargetUser(targetUserId));
        }

        var previousStatus = wallet.AccountStatus;

        // Validate: chỉ Senior admin mới được ban vĩnh viễn (nếu cần, implement role check ở controller)
        if (newStatus == AccountStatus.Banned && expiresAt == null)
        {
            _logger.LogWarning(
                "Admin {AdminId} setting BANNED without expiry for user {UserId}. Reason: {Reason}",
                adminUserId, targetUserId, reason);
        }

        // Update wallet
        wallet.AccountStatus = newStatus;
        wallet.UpdatedAt = DateTime.UtcNow;

        // Nếu là cooling-off → set expiresAt
        if (newStatus == AccountStatus.Suspended && expiresAt.HasValue)
        {
            wallet.IsCoolingOff = true;
            wallet.CoolingOffExpiresAt = expiresAt.Value;
        }
        else if (newStatus == AccountStatus.Active)
        {
            wallet.IsCoolingOff = false;
            wallet.CoolingOffExpiresAt = null;
        }

        await _walletRepository.UpdateAsync(wallet);

        // Ghi audit log vào PlayerActionHistory
        var actionType = newStatus switch
        {
            AccountStatus.Active => AdminActionType.AccountStatusChange,
            AccountStatus.Warning => AdminActionType.Warning,
            AccountStatus.Suspended => AdminActionType.Suspend,
            AccountStatus.Banned => AdminActionType.Ban,
            _ => AdminActionType.AccountStatusChange
        };

        var metadata = new Dictionary<string, object?>
        {
            ["previousStatus"] = previousStatus.ToString(),
            ["newStatus"] = newStatus.ToString(),
            ["reason"] = reason
        };

        var historyEntry = new PlayerActionHistory
        {
            Id = Guid.NewGuid(),
            UserId = targetUserId,
            ActionType = actionType,
            ActionBy = adminUserId,
            Reason = reason,
            Metadata = JsonSerializer.Serialize(metadata),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        _db.PlayerActionHistories.Add(historyEntry);
        await _walletRepository.SaveChangesAsync();

        _logger.LogWarning(
            "Admin account status change. AdminId={AdminId}, TargetUserId={TargetUserId}, PreviousStatus={Prev}, NewStatus={New}, ExpiresAt={ExpiresAt}, Reason={Reason}",
            adminUserId, targetUserId, previousStatus, newStatus, expiresAt, reason);

        return new AdminSetStatusResultDto
        {
            TargetUserId = targetUserId,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            ExpiresAt = expiresAt,
            ChangedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// W-05: Verify SUM(ledger entries) = wallet.availableBalance.
    /// Credits: TopUp + AdminCredit + DepositRelease (release từ held về available).
    /// Debits:  DepositHold + AdminDebit + DepositCapture + DepositForfeit.
    /// </summary>
    /// <remarks>
    /// GAP-R6-WAL-01 Fix: trước đây <c>DepositRelease</c> bị bỏ sót khỏi creditTypes,
    /// khiến wallet/ledger drift khi player cancel lobby → BVC refund về available.
    /// Đã thêm DepositRelease vì release làm tăng available (HeldBalance → AvailableBalance).
    /// </remarks>
    public async Task<WalletReconcileResultDto> ReconcileWalletAsync(Guid userId)
    {
        var wallet = await _walletRepository.GetByUserIdAsync(userId);
        if (wallet == null)
        {
            throw new NotFoundException(ApiErrorMessages.Wallet.NotFound(userId));
        }

        // GAP-R6-WAL-01: DepositRelease là credit — nó hoàn tiền từ held về available
        // (HeldBalance giảm, AvailableBalance tăng). Bỏ sót → reconcile luôn mismatch.
        var creditTypes = new[]
        {
            LedgerEntryType.TopUp,
            LedgerEntryType.AdminCredit,
            LedgerEntryType.DepositRelease,
        };
        var debitTypes = new[]
        {
            LedgerEntryType.DepositHold,
            LedgerEntryType.AdminDebit,
            LedgerEntryType.DepositCapture,
            LedgerEntryType.DepositForfeit,
        };

        var credits = await _ledgerRepository.SumAmountByTypesAsync(userId, creditTypes);
        var debits = await _ledgerRepository.SumAmountByTypesAsync(userId, debitTypes);
        var computed = credits - debits;

        return new WalletReconcileResultDto
        {
            UserId = userId,
            WalletAvailableBalance = wallet.AvailableBalance,
            LedgerCredits = credits,
            LedgerDebits = debits,
            ComputedAvailableBalance = computed,
            IsBalanced = computed == wallet.AvailableBalance,
            ReconciledAt = DateTime.UtcNow
        };
    }

    // ============================================================
    // M2 — Member BVC bill payment (Case 2 — docs §C2.1, §C2.5, §C2.8, §C2.12)
    // ============================================================

    /// <summary>
    /// M2 / Task C2.1 + C2.5 + C2.12: Trừ trực tiếp BVC từ <c>availableBalance</c> cho member bill payment.
    /// Idempotent theo <paramref name="idempotencyKey"/>.
    /// </summary>
    /// <remarks>
    /// Khác với <see cref="HoldDepositAsync"/>:
    /// - Hold đẩy tiền sang heldBalance (giữ cho reservation) — vẫn thuộc wallet user.
    /// - DirectDebit trừ thẳng availableBalance — tiền đi về doanh thu quán (settlement).
    /// <para>
    /// Flow:
    /// </para>
    /// <list type="number">
    ///   <item>Validate amount &gt; 0, idempotency key không rỗng.</item>
    ///   <item>Idempotency: lookup ledger entry theo key. Nếu có → return existing.</item>
    ///   <item>Begin Serializable transaction (hoặc dùng ambient nếu caller đã wrap).</item>
    ///   <item>Lock wallet FOR UPDATE; validate availableBalance &gt;= amount.</item>
    ///   <item>availableBalance -= amount (KHÔNG touch heldBalance).</item>
    ///   <item>Insert ledger entry <c>MemberBillDebit</c> với Note = "BVC payment for member {memberId} bill {billId}".</item>
    ///   <item>C2.12: Nếu availableBalance vượt constant <c>HeldBalanceCapBvc</c> (1.000.000 BVC),
    ///         log warning (no notification ở M2 — notification là C2.9).</item>
    /// </list>
    /// </remarks>
    public async Task<BvcLedgerEntry> DirectDebitForBillAsync(
        Guid userId,
        long amountBvc,
        Guid memberId,
        Guid billId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (amountBvc <= 0)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.AmountMustBePositive);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        await ApplyBalanceMutationAsync(
            userId,
            amountBvc,
            LedgerEntryType.MemberBillDebit,
            relatedLobbyId: null,        // MemberBill không liên kết lobby trực tiếp
            relatedReservationId: null,
            idempotencyKey,
            (w, amt) =>
            {
                if (w.AvailableBalance < amt)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Wallet.BvcBillPaymentExceedsBalance(w.AvailableBalance, amt));
                }
                // Direct debit: availableBalance -= amount. KHÔNG đẩy vào heldBalance
                // (tiền đi thẳng về doanh thu quán, settlement handled ở Phase khác).
                w.AvailableBalance -= amt;

                // C2.12: HeldBalance cap warning (Gap #9 BR-USER-LIMIT-03).
                // Sau debit, nếu balance vượt constant cap → log warning. Notification là C2.9 (defer).
                if (w.AvailableBalance > HeldBalanceCapBvc)
                {
                    _logger.LogWarning(
                        "M2 C2.12: User {UserId} available balance {Current}BVC exceeds cap {Cap}BVC after debit. " +
                        "Manual review recommended. MemberId={MemberId}, DebitAmount={Amount}BVC.",
                        userId, w.AvailableBalance, HeldBalanceCapBvc, memberId, amt);
                }
            },
            cancellationToken);

        // Cập nhật Note với context sau khi ledger đã insert (append-only OK cho field note).
        var entry = await _ledgerRepository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (entry != null)
        {
            entry.Note = $"BVC payment for member {memberId} bill {billId}";
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "M2 C2.1: Direct debit for member bill. UserId={UserId}, MemberId={MemberId}, BillId={BillId}, " +
            "Amount={Amount}BVC, IdempotencyKey={Key}",
            userId, memberId, billId, amountBvc, idempotencyKey);

        return entry!;
    }

    /// <summary>
    /// M2 / Task C2.8: Refund BVC bill payment về <c>availableBalance</c>.
    /// Idempotent theo <paramref name="idempotencyKey"/>.
    /// </summary>
    /// <remarks>
    /// Đối ứng với <see cref="DirectDebitForBillAsync"/> — dùng cho flow refund bill sai / dispute (Gap #11).
    /// Khác với <see cref="RefundMemberDepositOnMergeAsync"/> (refund deposit khi merge, BR § III.2):
    /// - RefundMemberDepositOnMerge chuyển heldBalance → available (deposit refund).
    /// - RefundMemberBill cộng vào availableBalance từ ledger <c>MemberBillDebit</c> trước đó.
    /// <para>
    /// Flow:
    /// </para>
    /// <list type="number">
    ///   <item>Validate amount &gt; 0.</item>
    ///   <item>Idempotency: lookup ledger entry theo key. Nếu có → return existing.</item>
    ///   <item>Begin transaction.</item>>
    ///   <item>Lock wallet; availableBalance += amount.</item>
    ///   <item>Insert ledger entry <c>MemberBillRefund</c>.</item>
    /// </list>
    /// </remarks>
    public async Task<BvcLedgerEntry> RefundMemberBillAsync(
        Guid userId,
        long amountBvc,
        Guid memberId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (amountBvc <= 0)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.AmountMustBePositive);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        await ApplyBalanceMutationAsync(
            userId,
            amountBvc,
            LedgerEntryType.MemberBillRefund,
            relatedLobbyId: null,
            relatedReservationId: null,
            idempotencyKey,
            (w, amt) =>
            {
                // Refund: cộng vào availableBalance (KHÔNG đụng heldBalance).
                w.AvailableBalance += amt;
            },
            cancellationToken);

        // Update Note sau insert (append-only OK cho field note).
        var entry = await _ledgerRepository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (entry != null)
        {
            entry.Note = $"BVC refund for member {memberId} bill (dispute / correction)";
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "M2 C2.8: Refund member bill BVC. UserId={UserId}, MemberId={MemberId}, Amount={Amount}BVC, IdempotencyKey={Key}",
            userId, memberId, amountBvc, idempotencyKey);

        return entry!;
    }

    /// <summary>
    /// C2.12: HeldBalance cap — log warning khi wallet vượt ngưỡng an toàn (BR-USER-LIMIT-03).
    /// Wallet entity hiện KHÔNG có <c>MaxAvailableBalanceBvc</c> (forward-compat) → dùng constant tạm.
    /// Threshold 1.000.000 BVC = 1 tỷ VND — vượt ngưỡng này cần manual review.
    /// </summary>
    private const long HeldBalanceCapBvc = 1_000_000L;

    /// <summary>
    /// BUGFIX (subagent audit #8): Generate OrderId dạng hash 18 char từ GUID + nanoseconds + userId.
    /// Trước đây dùng Substring(0, 18) trên GUID:N (32 chars) → collision risk cao +
    /// SHA256 hash an toàn hơn dù GUID vẫn unique.
    /// </summary>
    private static string GenerateOrderId(Guid userId)
    {
        // Gap 3.3: OrderId = SHA256 9 bytes (18 hex chars uppercase).
        // Birthday paradox: xác suất collision trong 1 năm với ~1M top-up = 1M² / 2^72 ≈ 5.4e-11.
        // → Cứ ~ 5.4 tỷ top-up mới có ~50% collision. An toàn cho MVP + production scale hiện tại.
        // DB có UNIQUE constraint trên BvcTopUpRequests.OrderId → collision bị reject + replay.
        // (Verified by tests: WalletServiceTests.GenerateOrderId_HasExpectedFormat).
        var input = $"{userId:N}-{DateTime.UtcNow.Ticks}-{Guid.NewGuid():N}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        // Lấy 9 bytes → 18 hex chars uppercase
        var sb = new System.Text.StringBuilder(18);
        for (int i = 0; i < 9; i++)
        {
            sb.Append(hash[i].ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Gap 3.5: Trim failure reason về 500 chars trước khi lưu DB.
    /// Tránh spam reason dài từ SePay (gateway có thể trả raw HTML / stack trace).
    /// </summary>
    private static string? TruncateFailureReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        const int MaxLen = 500;
        var trimmed = reason.Trim();
        return trimmed.Length > MaxLen ? trimmed[..MaxLen] : trimmed;
    }
}
