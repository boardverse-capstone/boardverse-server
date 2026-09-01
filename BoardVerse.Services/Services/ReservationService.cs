using System.Diagnostics;

using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// Service orchestration cho Reservation flow (BR §21A.2..21A.6).
/// Layer 3 trong kế hoạch 7 phase.
///
/// Triển khai:
/// - 21A.2 Quote: validate + tính cọc + trả quote (Idempotent).
/// - 21A.3 Confirm: atomic transaction — hold BVC + seat + game + create reservation + lobby.
/// - 21A.6 Cancel: host hủy, áp dụng BR-REFUND-02/03.
/// - 21A.9 No-show: scheduler invoke.
/// - BR-NEW-11: cafe approve/reject.
///
/// BR-REQUIRED §17.4: toàn bộ side-effect DB trong 1 transaction.
/// </summary>
public class ReservationService : IReservationService
{
    private const int QuoteExpiryMinutes = 5;
    private const int NoShowGraceMinutes = 30;
    private const int ActiveLobbyStatusesCounted = 1; // active host lobby limit
    private const int MaxAdvanceBookingDays = 7; // BR-NEW-01 + Q#19: playDate tối đa 7 ngày trong tương lai

    private readonly BoardVerseDbContext _db;
    private readonly IWalletService _walletService;
    private readonly IWalletRepository _walletRepository;
    private readonly IReservationRepository _reservationRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly ISeatInventoryRepository _seatInventoryRepository;
    private readonly IGameInventoryRepository _gameInventoryRepository;
    private readonly ICafeInventoryRepository _cafeInventoryRepository;
    private readonly ICafeConfigRepository _cafeConfigRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly IUserManagementRepository _userRepository;
    private readonly IGameTemplateRepository _gameRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly DepositCalculator _depositCalculator;
    private readonly EligibilityValidator _eligibilityValidator;
    private readonly IScheduleResolver _scheduleResolver;
    private readonly ILogger<ReservationService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IBookingRatingService _bookingRatingService;
    private readonly RefundCalculationService _refundCalc;
    private readonly IWalkInService _walkInService;
    private readonly IPlayerKarmaService _karmaService;
    private readonly ISystemConfigurationProvider _configProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISettlementService _settlementService;

    public ReservationService(
        BoardVerseDbContext db,
        IWalletService walletService,
        IWalletRepository walletRepository,
        IReservationRepository reservationRepository,
        ILobbyRepository lobbyRepository,
        ISeatInventoryRepository seatInventoryRepository,
        IGameInventoryRepository gameInventoryRepository,
        ICafeInventoryRepository cafeInventoryRepository,
        ICafeConfigRepository cafeConfigRepository,
        ICafeRepository cafeRepository,
        IUserManagementRepository userRepository,
        IGameTemplateRepository gameRepository,
        IOutboxRepository outboxRepository,
        IActiveSessionRepository activeSessionRepository,
        DepositCalculator depositCalculator,
        EligibilityValidator eligibilityValidator,
        IScheduleResolver scheduleResolver,
        ILogger<ReservationService> logger,
        TimeProvider timeProvider,
        IBookingRatingService bookingRatingService,
        RefundCalculationService refundCalc,
        IWalkInService walkInService,
        IPlayerKarmaService karmaService,
        ISystemConfigurationProvider configProvider = null!,
        IHttpContextAccessor httpContextAccessor = null!,
        ISettlementService settlementService = null!)
    {
        _db = db;
        _walletService = walletService;
        _walletRepository = walletRepository;
        _reservationRepository = reservationRepository;
        _lobbyRepository = lobbyRepository;
        _seatInventoryRepository = seatInventoryRepository;
        _gameInventoryRepository = gameInventoryRepository;
        _cafeInventoryRepository = cafeInventoryRepository;
        _cafeConfigRepository = cafeConfigRepository;
        _cafeRepository = cafeRepository;
        _userRepository = userRepository;
        _gameRepository = gameRepository;
        _outboxRepository = outboxRepository;
        _activeSessionRepository = activeSessionRepository;
        _depositCalculator = depositCalculator;
        _eligibilityValidator = eligibilityValidator;
        _scheduleResolver = scheduleResolver;
        _logger = logger;
        _timeProvider = timeProvider;
        _bookingRatingService = bookingRatingService;
        _refundCalc = refundCalc;
        _walkInService = walkInService;
        _karmaService = karmaService;
        _configProvider = configProvider;
        _httpContextAccessor = httpContextAccessor;
        _settlementService = settlementService ?? throw new ArgumentNullException(nameof(settlementService));
    }

    // ===== 21A.2 QUOTE =====

    public async Task<ReservationQuoteDto> CreateQuoteAsync(Guid hostId, ReservationQuoteRequestDto request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        ValidatePlayDate(request.PlayDate, now);

        // Validate preferredStartTime + preferredEndTime hợp lệ.
        var (timeValid, timeError) = CafeSchedule.ValidatePreferredTimeRange(
            request.PreferredStartTime, request.PreferredEndTime);
        if (!timeValid)
        {
            throw new BadRequestException(timeError!);
        }

        // BR-NEW-15: Validate với giờ mở/đóng thực tế của cafe (theo CafeScheduleOverride).
        // Xử lý cả overnight sessions (validate preferredEnd với schedule ngày kế tiếp).
        await ValidatePreferredTimesWithCafeScheduleAsync(
            request.CafeId,
            request.PlayDate,
            request.PreferredStartTime,
            request.PreferredEndTime,
            cancellationToken);

        // Validate cafe + game tồn tại.
        await ValidateCafeAndGameAsync(request);

        // Load cafe config (BR-NEW-12).
        var cafeConfig = await _cafeConfigRepository.GetOrCreateDefaultAsync(request.CafeId);

        // Ensure SeatInventory tồn tại.
        await _seatInventoryRepository.EnsureRowAsync(
            request.CafeId,
            request.PlayDate,
            request.PreferredStartTime,
            request.PreferredEndTime,
            cafeConfig.Capacity);

        // Ensure GameInventory tồn tại.
        var cafeInventory = await _cafeInventoryRepository.GetByCafeAndGameTemplateAsync(
            request.CafeId, request.GameId);
        var totalCopies = cafeInventory?.BoxQuantity ?? 1;
        await _gameInventoryRepository.EnsureRowAsync(
            request.CafeId,
            request.GameId,
            request.PlayDate,
            request.PreferredStartTime,
            request.PreferredEndTime,
            totalCopies);

        // Load wallet để lấy riskMultiplier.
        var wallet = await GetOrCreateWalletEntityAsync(hostId, now);

        // Load cafe BasePrice.
        var cafe = await _cafeRepository.GetActiveByIdAsync(request.CafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(request.CafeId));

        // G3 fix: Validate preferred times với cafe schedule TRƯỚC khi tính deposit.
        // Nếu preferredStartTime/preferredEndTime nằm ngoài giờ mở/đóng thực tế,
        // không có nghĩa lý gì khi tính quote → reject sớm.
        var resolvedScheduleForQuote = await _scheduleResolver.ResolveAsync(request.CafeId, request.PlayDate, cancellationToken);
        if (resolvedScheduleForQuote.IsClosed)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.CafeScheduleClosedForPlayDate);
        }
        await ValidatePreferredTimesWithCafeScheduleAsync(
            request.CafeId, request.PlayDate,
            request.PreferredStartTime, request.PreferredEndTime, cancellationToken);

        // Tính quote.
        var quote = _depositCalculator.Calculate(
            request,
            cafeConfig,
            cafe.BasePrice,
            wallet.RiskMultiplier,
            wallet.IsCoolingOff,
            request.IsPrivate,
            now);

        // BR-LOBBY-01a/b: buffer check.
        var (isAllowed, _) = DepositCalculator.EvaluateBuffer(quote.BufferMinutes);
        if (!isAllowed && !await ShouldBypassLobbyBufferAsync())
        {
            throw new BadRequestException(
                ApiErrorMessages.Reservation.BufferTooShort(quote.BufferMinutes, 60));
        }

        // Build eligibility context + validate BR-USER-LIMIT-* / BR-NEW-*.
        var eligibilityContext = await BuildHostEligibilityContextAsync(
            hostId,
            request,
            quote,
            wallet,
            now);

        await _eligibilityValidator.ValidateHostCanCreateAsync(
            eligibilityContext, _httpContextAccessor, _configProvider, _logger);

        // BR-RESV-02: build ScheduledStart/End từ user-chosen preferred times.
        var (scheduledStartTime, scheduledEndTime) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            request.PlayDate, request.PreferredStartTime, request.PreferredEndTime);

        // G13 fix: defensive assertion — scheduledStartTime phải thuộc playDate.
        Debug.Assert(
            DateOnly.FromDateTime(scheduledStartTime) == request.PlayDate,
            $"[G13] scheduledStartTime {scheduledStartTime:yyyy-MM-dd} không thuộc playDate {request.PlayDate}. " +
            "CafeSchedule.BuildScheduledStartEndFromPreferred có bug.");

        // Validate cafe mở cửa.
        var resolvedSchedule = await _scheduleResolver.ResolveAsync(request.CafeId, request.PlayDate);
        if (resolvedSchedule.IsClosed)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.CafeScheduleClosedForPlayDate);
        }

        // Validate reservation time window.
        ValidateReservationTimeWindow(scheduledStartTime, scheduledEndTime, now);

        var recruitmentDeadline = scheduledStartTime.AddMinutes(-20);

        var warnings = new List<string>();
        if (quote.BufferWarning)
        {
            warnings.Add($"Thời gian đệm đến deadline chỉ còn {quote.BufferMinutes} phút. Hãy chọn khung giờ khác nếu có thể.");
        }

        return new ReservationQuoteDto
        {
            ReservationId = null,
            CafeId = request.CafeId,
            GameId = request.GameId,
            PlayDate = request.PlayDate,
            PreferredStartTime = request.PreferredStartTime,
            PreferredEndTime = request.PreferredEndTime,
            ScheduledStartTime = scheduledStartTime,
            ScheduledEndTime = scheduledEndTime,
            RecruitmentDeadline = recruitmentDeadline,
            MinPlayers = request.MinPlayers,
            MaxPlayers = quote.MaxPlayersApplied,
            DepositRatePerPerson = cafeConfig.DepositRatePerPerson,
            BaseDeposit = quote.BaseDeposit,
            RiskMultiplier = quote.RiskMultiplier,
            MinDepositApplied = quote.MinDepositApplied,
            FinalDeposit = quote.FinalDeposit,
            CurrentBalance = wallet.AvailableBalance,
            MissingAmount = Math.Max(0, quote.FinalDeposit - wallet.AvailableBalance),
            BufferMinutes = quote.BufferMinutes,
            BufferWarning = quote.BufferWarning,
            RequiresCafeApproval = quote.RequiresCafeApproval,
            ExpiresAt = now.AddMinutes(QuoteExpiryMinutes),
            Warnings = warnings
        };
    }

    // ===== 21A.3 CONFIRM =====

    public async Task<ReservationConfirmResponseDto> ConfirmAsync(Guid hostId, ReservationConfirmRequestDto request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // 1. Idempotency check: BR §XVII.1 — same IdempotencyKey trả cùng kết quả.
        var existing = await _reservationRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey);
        if (existing != null)
        {
            if (existing.HostId != hostId)
            {
                throw new ConflictException(ApiErrorMessages.Reservation.IdempotencyKeyConflict);
            }

            // Fix #Bug-IdempotentStrictParams: So sánh tất cả params để chống replay với params khác.
            // Nếu client gửi request với params khác nhưng cùng IdempotencyKey → 409 Conflict.
            var paramsMismatch = new List<string>();
            if (existing.CafeId != request.CafeId)
                paramsMismatch.Add($"CafeId (existing={existing.CafeId}, request={request.CafeId})");
            if (existing.GameId != request.GameId)
                paramsMismatch.Add($"GameId (existing={existing.GameId}, request={request.GameId})");
            if (existing.PlayDate != request.PlayDate)
                paramsMismatch.Add($"PlayDate (existing={existing.PlayDate}, request={request.PlayDate})");
            if (existing.PreferredStartTime != request.PreferredStartTime)
                paramsMismatch.Add($"PreferredStartTime (existing={existing.PreferredStartTime}, request={request.PreferredStartTime})");
            if (existing.PreferredEndTime != request.PreferredEndTime)
                paramsMismatch.Add($"PreferredEndTime (existing={existing.PreferredEndTime}, request={request.PreferredEndTime})");
            if (existing.MaxPlayers != request.MaxPlayers)
                paramsMismatch.Add($"MaxPlayers (existing={existing.MaxPlayers}, request={request.MaxPlayers})");
            if (existing.MinPlayers != request.MinPlayers)
                paramsMismatch.Add($"MinPlayers (existing={existing.MinPlayers}, request={request.MinPlayers})");
            if (existing.DepositAmount != request.ExpectedFinalDeposit)
                paramsMismatch.Add($"ExpectedFinalDeposit (existing={existing.DepositAmount}, request={request.ExpectedFinalDeposit})");

            if (paramsMismatch.Count > 0)
            {
                _logger.LogWarning(
                    "IdempotencyKey '{Key}' reused with different params: {Mismatches}. " +
                    "Existing ReservationId={ReservationId}, LobbyId={LobbyId}, Status={Status}. " +
                    "Rejecting replay with 409 Conflict.",
                    request.IdempotencyKey,
                    string.Join("; ", paramsMismatch),
                    existing.Id,
                    existing.LobbyId,
                    existing.Status);

                throw new ConflictException(
                    ApiErrorMessages.System.IdempotencyKeyParamsMismatch(
                        request.IdempotencyKey, string.Join(", ", paramsMismatch)));
            }

            // Params khớp → kiểm tra self-heal (R-Bug-029) như cũ
            // Fix #Bug-IdempotentLobbyNull-3 + R-Bug-029: Self-heal — nếu LobbyId = null nhưng reservation tồn tại
            // (partial completion từ request trước), tìm lobby theo ReservationId ngược.
            if (existing.LobbyId == null)
            {
                _logger.LogWarning(
                    "Reservation idempotent '{Id:N}' has null LobbyId. Attempting self-heal by searching lobby by ReservationId.",
                    existing.Id);

                // Bước 1: Thử reload với relations (nếu FK đã được set nhưng chưa reload)
                var healed = await _reservationRepository.GetByIdAsync(existing.Id, includeRelations: true);
                if (healed?.LobbyId != null)
                {
                    existing = healed;
                    _logger.LogInformation(
                        "Self-healed: Reservation '{Id:N}' now has LobbyId='{LobbyId}'.",
                        existing.Id, existing.LobbyId);
                }
                else
                {
                    // Bước 2: Tìm lobby theo ReservationId ngược (R-Bug-029)
                    var orphanLobby = await _lobbyRepository.GetByReservationIdAsync(existing.Id);
                    if (orphanLobby != null)
                    {
                        existing = await _reservationRepository.GetByIdAsync(existing.Id, includeRelations: true);
                        if (existing != null)
                        {
                            // Bind FK để heal
                            existing.LobbyId = orphanLobby.Id;
                            await _reservationRepository.UpdateAsync(existing);
                            await _db.SaveChangesAsync(cancellationToken); // Lưu bind
                            _logger.LogInformation(
                                "Self-healed orphan: Reservation '{Id:N}' bound to LobbyId='{LobbyId}'.",
                                existing.Id, orphanLobby.Id);
                        }
                    }
                }
            }

            var existingLobbyId = existing!.LobbyId
                ?? throw new InternalServerErrorException(
                    ApiErrorMessages.System.ReservationLobbyMissingOnIdempotent(existing.Id));

            return new ReservationConfirmResponseDto
            {
                ReservationId = existing.Id,
                LobbyId = existingLobbyId,
                RecruitmentDeadline = existing.RecruitmentDeadline,
                RequiresCafeApproval = existing.Lobby?.Status == LobbyStatus.PendingCafeApproval,
                CafeApprovalDeadline = existing.Lobby?.CafeApprovalDeadline,
                HeldBvc = existing.DepositAmount
            };
        }

        // 2. Re-validate cafe + game + window.
        ValidatePlayDate(request.PlayDate, now);
        ValidatePlayersWindowRaw(request.MinPlayers, request.MaxPlayers);

        // Validate preferredStartTime + preferredEndTime nằm trong giờ mở/đóng cửa cafe.
        var (timeValid, timeError) = CafeSchedule.ValidatePreferredTimeRange(
            request.PreferredStartTime, request.PreferredEndTime);
        if (!timeValid)
        {
            throw new BadRequestException(timeError!);
        }

        // BR-NEW-15: Validate với giờ mở/đóng thực tế của cafe (theo CafeScheduleOverride).
        // Xử lý cả overnight sessions (validate preferredEnd với schedule ngày kế tiếp).
        await ValidatePreferredTimesWithCafeScheduleAsync(
            request.CafeId,
            request.PlayDate,
            request.PreferredStartTime,
            request.PreferredEndTime,
            cancellationToken);

        var quoteRequest = new ReservationQuoteRequestDto
        {
            CafeId = request.CafeId,
            GameId = request.GameId,
            PlayDate = request.PlayDate,
            PreferredStartTime = request.PreferredStartTime,
            PreferredEndTime = request.PreferredEndTime,
            MinPlayers = request.MinPlayers,
            MaxPlayers = request.MaxPlayers,
            IsPrivate = request.IsPrivate,
            IdempotencyKey = request.IdempotencyKey
        };

        await ValidateCafeAndGameAsync(quoteRequest);

        // 3. Load cafe config + wallet (entity để thao tác trực tiếp với DB).
        var cafeConfig = await _cafeConfigRepository.GetOrCreateDefaultAsync(request.CafeId);
        var wallet = await GetOrCreateWalletEntityAsync(hostId, now);

        // Ensure SeatInventory tồn tại trước khi bắt đầu transaction.
        // BR-NEW-15: Dùng PreferredStartTime/PreferredEndTime thay vì TimeSlot.
        await _seatInventoryRepository.EnsureRowAsync(
            request.CafeId,
            request.PlayDate,
            request.PreferredStartTime,
            request.PreferredEndTime,
            cafeConfig.Capacity);

        // Ensure GameInventory tồn tại (BR-RESERVATION-02) — fix bug GameInventoryNotFound 409.
        // TotalCopies lấy từ CafeGameInventory.BoxQuantity.
        var cafeInventory = await _cafeInventoryRepository.GetByCafeAndGameTemplateAsync(
            request.CafeId, request.GameId);
        var totalCopies = cafeInventory?.BoxQuantity ?? 1;
        await _gameInventoryRepository.EnsureRowAsync(
            request.CafeId,
            request.GameId,
            request.PlayDate,
            request.PreferredStartTime,
            request.PreferredEndTime,
            totalCopies);

        // 4. Tính lại quote (server authoritative — BR §XVII.2) — có áp dụng CafeScheduleOverride.
        var cafe = await _cafeRepository.GetActiveByIdAsync(request.CafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(request.CafeId));
        // BR-NEW-15: Calculate nhận ReservationQuoteRequestDto; map từ ConfirmDto.
        var quoteRequestDto = new ReservationQuoteRequestDto
        {
            CafeId = request.CafeId,
            GameId = request.GameId,
            PlayDate = request.PlayDate,
            PreferredStartTime = request.PreferredStartTime,
            PreferredEndTime = request.PreferredEndTime,
            MaxPlayers = request.MaxPlayers,
            MinPlayers = request.MinPlayers,
            IsPrivate = request.IsPrivate,
            IdempotencyKey = request.IdempotencyKey
        };
        var quote = _depositCalculator.Calculate(
            quoteRequestDto,
            cafeConfig,
            cafe.BasePrice,
            wallet.RiskMultiplier,
            wallet.IsCoolingOff,
            request.IsPrivate,
            now);

        // 5. BR-LOBBY-01a/b: buffer check.
        var (isAllowed, _) = DepositCalculator.EvaluateBuffer(quote.BufferMinutes);
        if (!isAllowed && !await ShouldBypassLobbyBufferAsync())
        {
            throw new BadRequestException(
                ApiErrorMessages.Reservation.BufferTooShort(quote.BufferMinutes, 60));
        }

        // 6. BR §XVII.2: chống client gửi sai số BVC.
        if (quote.FinalDeposit != request.ExpectedFinalDeposit)
        {
            throw new BadRequestException(
                ApiErrorMessages.Reservation.FinalDepositMismatch(quote.FinalDeposit, request.ExpectedFinalDeposit));
        }

        // 7. BR-USER-LIMIT-* / BR-NEW-* validate.
        var eligibilityContext = await BuildHostEligibilityContextAsync(
            hostId, quoteRequest, quote, wallet, now);
        await _eligibilityValidator.ValidateHostCanCreateAsync(
            eligibilityContext, _httpContextAccessor, _configProvider, _logger);

        // 8. BR-RESERVATION-01: đủ ghế? BR-RESERVATION-02: đủ game copy?
        if (wallet.AvailableBalance < quote.FinalDeposit)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.InsufficientAvailableBalance(
                wallet.AvailableBalance, quote.FinalDeposit));
        }

        // Validate cafe mở cửa slot này.
        // BR-NEW-15: ResolveAsync now takes (cafeId, playDate) without TimeSlot.
        var resolvedSchedule = await _scheduleResolver.ResolveAsync(request.CafeId, quoteRequest.PlayDate);
        if (resolvedSchedule.IsClosed)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.CafeScheduleClosedForPlayDate);
        }

        // BR-RESV-02: build ScheduledStart/End từ user-chosen preferred times.
        var (scheduledStartTime, scheduledEndTime) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            quoteRequest.PlayDate, quoteRequest.PreferredStartTime, quoteRequest.PreferredEndTime);

        // G13 fix: defensive assertion — scheduledStartTime phải thuộc playDate.
        Debug.Assert(
            DateOnly.FromDateTime(scheduledStartTime) == quoteRequest.PlayDate,
            $"[G13] scheduledStartTime {scheduledStartTime:yyyy-MM-dd} không thuộc playDate {quoteRequest.PlayDate}. " +
            "CafeSchedule.BuildScheduledStartEndFromPreferred có bug.");

        // G3 fix: Validate preferred times với cafe schedule trước khi hold BVC.
        await ValidatePreferredTimesWithCafeScheduleAsync(
            request.CafeId, quoteRequest.PlayDate,
            quoteRequest.PreferredStartTime, quoteRequest.PreferredEndTime, cancellationToken);

        // BR-RES-07/08/09: validate reservation có startTime+endTime, cùng ngày, preferred times hợp lệ.
        ValidateReservationTimeWindow(scheduledStartTime, scheduledEndTime, now);

        var recruitmentDeadline = scheduledStartTime.AddMinutes(-20); // BR-LOBBY-01 default leadTimeMinutes = 20

        // ===== Atomic transaction (BR-REQUIRED §17.4) =====
        // Dùng Serializable Isolation để chống race condition overbooking (BR §17.3).
        // Postgres sẽ throw DbUpdateException với SqlState=40001 nếu conflict → retry tối đa 3 lần.
        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await ExecuteConfirmTransactionAsync(
                    hostId, request, quoteRequestDto, cafeConfig, wallet, quote, scheduledStartTime, scheduledEndTime, recruitmentDeadline, now, cancellationToken);
            }
            catch (DbUpdateException dbx) when (IsSerializationFailure(dbx) && attempt < maxRetries)
            {
                _logger.LogWarning(
                    "ConfirmAsync serialization failure attempt {Attempt}/{Max}. Retrying. HostId={HostId}, IdempotencyKey={Key}",
                    attempt, maxRetries, hostId, request.IdempotencyKey);

                // Reset EF change tracker để retry với snapshot mới.
                _db.ChangeTracker.Clear();

                // Nạp lại wallet + cafe config (tracked instance cũ đã detached).
                wallet = await GetOrCreateWalletEntityAsync(hostId, now);
                cafeConfig = await _cafeConfigRepository.GetOrCreateDefaultAsync(request.CafeId);
                var cafeRetry = await _cafeRepository.GetActiveByIdAsync(request.CafeId)
                    ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(request.CafeId));
                quoteRequestDto = new ReservationQuoteRequestDto
                {
                    CafeId = request.CafeId,
                    GameId = request.GameId,
                    PlayDate = request.PlayDate,
                    PreferredStartTime = request.PreferredStartTime,
                    PreferredEndTime = request.PreferredEndTime,
                    MaxPlayers = request.MaxPlayers,
                    MinPlayers = request.MinPlayers,
                    IsPrivate = request.IsPrivate,
                    IdempotencyKey = request.IdempotencyKey
                };
                quote = _depositCalculator.Calculate(
                    quoteRequestDto,
                    cafeConfig,
                    cafeRetry.BasePrice,
                    wallet.RiskMultiplier,
                    wallet.IsCoolingOff,
                    request.IsPrivate,
                    now);
            }
            catch (Exception ex)
            {
                // Log non-serialization exceptions for debugging, then let it propagate
                _logger.LogWarning(ex,
                    "ConfirmAsync non-serialization error on attempt {Attempt}/{Max}. HostId={HostId}, IdempotencyKey={Key}. Exception: {ExceptionType}",
                    attempt, maxRetries, hostId, request.IdempotencyKey, ex.GetType().FullName);

                // Only retry for serialization failures; other exceptions should propagate
                if (attempt >= maxRetries)
                {
                    throw;
                }

                // Reset EF change tracker để retry với snapshot mới.
                _db.ChangeTracker.Clear();

                // Nạp lại wallet + cafe config
                wallet = await GetOrCreateWalletEntityAsync(hostId, now);
                cafeConfig = await _cafeConfigRepository.GetOrCreateDefaultAsync(request.CafeId);
                var cafeRetry = await _cafeRepository.GetActiveByIdAsync(request.CafeId)
                    ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(request.CafeId));
                quoteRequestDto = new ReservationQuoteRequestDto
                {
                    CafeId = request.CafeId,
                    GameId = request.GameId,
                    PlayDate = request.PlayDate,
                    PreferredStartTime = request.PreferredStartTime,
                    PreferredEndTime = request.PreferredEndTime,
                    MaxPlayers = request.MaxPlayers,
                    MinPlayers = request.MinPlayers,
                    IsPrivate = request.IsPrivate,
                    IdempotencyKey = request.IdempotencyKey
                };
                quote = _depositCalculator.Calculate(
                    quoteRequestDto,
                    cafeConfig,
                    cafeRetry.BasePrice,
                    wallet.RiskMultiplier,
                    wallet.IsCoolingOff,
                    request.IsPrivate,
                    now);
            }
        }

        // Không bao giờ đến đây, nhưng compiler cần.
        throw new InternalServerErrorException(ApiErrorMessages.Reservation.ConfirmRetryExhausted);
    }

    /// <summary>
    /// Body của atomic transaction — extract để retry isolation failure.
    /// </summary>
    private async Task<ReservationConfirmResponseDto> ExecuteConfirmTransactionAsync(
        Guid hostId,
        ReservationConfirmRequestDto request,
        ReservationQuoteRequestDto quoteRequest,
        CafeConfig cafeConfig,
        Wallet wallet,
        DepositQuoteResult quote,
        DateTime scheduledStartTime,
        DateTime scheduledEndTime,
        DateTime recruitmentDeadline,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var (_, tx) = await BeginTransactionIfNeededAsync();

        try
        {
            // 9. Lock seat inventory + game inventory (BR §17.3 — SELECT FOR UPDATE).
            // BR-NEW-15: Dùng PreferredStartTime/PreferredEndTime thay vì TimeSlot.
            var seatInventory = await _seatInventoryRepository.GetForUpdateAsync(
                request.CafeId, request.PlayDate, request.PreferredStartTime, request.PreferredEndTime);
            if (seatInventory == null)
            {
                throw new BadRequestException(ApiErrorMessages.Reservation.SeatInventoryNotConfigured);
            }

            if (seatInventory.AvailableSeats < quote.MaxPlayersApplied)
            {
                // AvailableSeats là computed property (TotalSeats - HeldSeats - InUseSeats) — column không tồn tại
                // trong DB, nên EF set = 0 sau raw SELECT *. Tính lại từ 3 cột để tránh race-condition LEAK.
                var seatsAvail = seatInventory.TotalSeats - seatInventory.HeldSeats - seatInventory.InUseSeats;
                throw new ConflictException(
                    ApiErrorMessages.Reservation.SeatsNotAvailable(seatsAvail, quote.MaxPlayersApplied));
            }

            var gameInventory = await _gameInventoryRepository.GetForUpdateAsync(
                request.CafeId, request.GameId, request.PlayDate, request.PreferredStartTime, request.PreferredEndTime);
            // AvailableCopies cũng là computed property — column không tồn tại trong DB, EF set = 0 sau raw SELECT.
            var copiesAvail = gameInventory == null
                ? 0
                : gameInventory.TotalCopies - gameInventory.HeldCopies - gameInventory.InUseCopies;
            if (gameInventory == null || copiesAvail < 1)
            {
                throw new ConflictException(
                    gameInventory == null
                        ? ApiErrorMessages.Reservation.GameInventoryNotFound
                        : ApiErrorMessages.Reservation.GameCopyNotAvailable(copiesAvail));
            }

            // 11. Snapshot cấu hình cọc (BR-NEW-12 + 21F.9).
            var depositSnapshot = new DepositSnapshot
            {
                DepositRatePerPerson = cafeConfig.DepositRatePerPerson,
                MaxPlayers = quote.MaxPlayersApplied,
                BaseDeposit = quote.BaseDeposit,
                RiskMultiplier = quote.RiskMultiplier,
                FinalDeposit = quote.FinalDeposit,
                MinDepositApplied = quote.MinDepositApplied,
                PricingModel = null // Có thể đọc từ Cafe nếu cần (BR-01), để null cho MVP.
            };

            // 12. Insert Reservation (BR-REQUIRED §17.4 — bước 6).
            // BR §21A.7: generate ReservationCode unique để POS scan QR.
            var reservationCode = ShareCodeGenerator.Generate();
            var reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                HostId = hostId,
                CafeId = request.CafeId,
                GameId = request.GameId,
                PlayDate = request.PlayDate,
                PreferredStartTime = request.PreferredStartTime,
                PreferredEndTime = request.PreferredEndTime,
                RecruitmentDeadline = recruitmentDeadline,
                ScheduledStartTime = scheduledStartTime,
                ScheduledEndTime = scheduledEndTime,
                MinPlayers = request.MinPlayers,
                MaxPlayers = quote.MaxPlayersApplied,
                DepositConfigSnapshot = depositSnapshot,
                DepositAmount = quote.FinalDeposit,
                MinDepositApplied = quote.MinDepositApplied,
                RiskMultiplier = quote.RiskMultiplier,
                Status = ReservationStatus.Holding,
                CurrentPlayers = 1,
                IdempotencyKey = request.IdempotencyKey,
                ReservationCode = reservationCode,
                SeatInventoryId = seatInventory.Id,
                GameInventoryId = gameInventory.Id,
                LobbyId = null, // R-Bug-029 Fix: insert NULL first để EF batching
                                   // không phải xử lý Reservation↔Lobby FK cycle.
                CreatedAt = now,
                UpdatedAt = now
            };
            await _reservationRepository.AddAsync(reservation);

            // 13. Insert Lobby (BR-REQUIRED §17.4 — bước 7).
            var initialLobbyStatus = DetermineInitialLobbyStatus(reservation, cafeConfig, now, request.IsPrivate);
            var lobby = new Lobby
            {
                Id = Guid.NewGuid(),
                HostUserId = hostId,
                GameTemplateId = request.GameId,
                CafeId = request.CafeId,
                ReservationId = reservation.Id,
                PlayDate = request.PlayDate,
                PreferredStartTime = request.PreferredStartTime,
                PreferredEndTime = request.PreferredEndTime,
                RecruitmentDeadline = recruitmentDeadline,
                ScheduledStartTime = scheduledStartTime,
                MaxMembers = quote.MaxPlayersApplied,
                MinPlayers = request.MinPlayers,
                MinDeposit = quote.FinalDeposit,
                DepositSnapshot = depositSnapshot,
                Status = initialLobbyStatus,
                ShareCode = ShareCodeGenerator.Generate(),
                IsPrivate = request.IsPrivate,
                CancellationLeadTimeMinutes = cafeConfig.RecruitmentDeadlineBufferMinutes,
                CreatedAt = now,
                UpdatedAt = now
            };

            if (initialLobbyStatus == LobbyStatus.PendingCafeApproval)
            {
                lobby.CafeApprovalDeadline = now.AddHours(cafeConfig.ApprovalTimeoutHours);
            }

            await _lobbyRepository.AddAsync(lobby);

            // R-Bug-029 Fix: tách SaveChangesAsync thành 2 giai đoạn để tránh
            // EF batch Reservation↔Lobby FK cycle.
            // Giai đoạn 1: insert Reservation (LobbyId=null) + Lobby (ReservationId=...).
            // Giai đoạn 2: update Reservation.LobbyId + insert LobbyMembers + các cập nhật khác.
            await _db.SaveChangesAsync(cancellationToken);

            // 14. Hold BVC (ledger + wallet mutation) — phải gọi SAU SaveChangesAsync đầu tiên
            // để có reservation.Id gán vào ledger entry.
            // Signature: HoldDepositAsync(userId, amount, relatedLobbyId?, relatedReservationId?, idempotencyKey)
            try
            {
                await _walletService.HoldDepositAsync(
                    hostId,
                    quote.FinalDeposit,
                    null,                    // relatedLobbyId: chưa có (sẽ update sau step 16)
                    reservation.Id,          // relatedReservationId: có sau SaveChangesAsync đầu tiên
                    request.IdempotencyKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "HoldDepositAsync failed. HostId={HostId}, ReservationId={ReservationId}, Amount={Amount}, IdempotencyKey={Key}",
                    hostId, reservation.Id, quote.FinalDeposit, request.IdempotencyKey);
                throw;
            }

            // 15. Bind FK reservation ↔ lobby (chỉ sau khi cả 2 đã insert thành công).
            reservation.LobbyId = lobby.Id;
            reservation.UpdatedAt = now;

            // 16. Update ledger entry với lobby.Id (sau khi lobby đã có ID).
            await _walletService.UpdateLedgerLobbyIdAsync(
                hostId,
                reservation.Id,
                lobby.Id,
                $"lobby-bound-{lobby.Id:N}");

            // 17. Update inventory counters.
            seatInventory.HeldSeats += quote.MaxPlayersApplied;
            seatInventory.UpdatedAt = now;
            await _seatInventoryRepository.UpdateAsync(seatInventory);

            gameInventory.HeldCopies += 1;
            gameInventory.UpdatedAt = now;
            await _gameInventoryRepository.UpdateAsync(gameInventory);

            // 18. Insert Host as first lobby member (BR-DEPOSIT-01).
            if (lobby.Status != LobbyStatus.PendingCafeApproval)
            {
                var hostMember = new LobbyMember
                {
                    Id = Guid.NewGuid(),
                    LobbyId = lobby.Id,
                    UserId = hostId,
                    JoinedAt = now,
                    IsActive = true,
                    IsHost = true,
                    Status = LobbyMemberStatus.Joined
                };
                await _lobbyRepository.AddMemberAsync(hostMember);
            }

            // BR-REQUIRED §17.4 + §21A.3: Sau khi atomic transaction commit,
            // lobby "pendingActivation" phải được promote sang Open ngay trong cùng transaction.
            // Tránh trạng thái stuck PendingActivation vĩnh viễn khi OutboxPublisher chỉ log
            // (LoggingOutboxPublisher) chứ không update DB.
            // PendingCafeApproval thì GIỮ NGUYÊN — chờ HandleCafeApprovalAsync xử lý.
            if (lobby.Status == LobbyStatus.PendingActivation)
            {
                lobby.Status = LobbyStatus.Open;
                lobby.UpdatedAt = now;
                await _lobbyRepository.UpdateAsync(lobby);
            }

            // 19. BR-REQUIRED §17.5: Transactional Outbox — 3 event trong cùng transaction.
            // Nếu commit fail → tất cả rollback; nếu SignalR fail sau commit → worker retry.
            var lobbyActivatedPayload = SerializeLobbyActivatedPayload(lobby, reservation, hostId);
            await _outboxRepository.AddAsync(new OutboxEvent
            {
                Id = Guid.NewGuid(),
                EventType = OutboxEventType.LobbyActivated,
                Payload = lobbyActivatedPayload,
                IdempotencyKey = $"lobby-activated-{lobby.Id:N}",
                ReservationId = reservation.Id,
                LobbyId = lobby.Id,
                UserId = hostId,
                CreatedAt = now
            });

            var reservationHeldPayload = SerializeReservationHeldPayload(reservation, quote);
            await _outboxRepository.AddAsync(new OutboxEvent
            {
                Id = Guid.NewGuid(),
                EventType = OutboxEventType.ReservationHeld,
                Payload = reservationHeldPayload,
                IdempotencyKey = $"reservation-held-{reservation.Id:N}",
                ReservationId = reservation.Id,
                LobbyId = lobby.Id,
                UserId = hostId,
                CreatedAt = now
            });

            var depositHeldPayload = SerializeDepositHeldPayload(hostId, quote.FinalDeposit, reservation.Id, lobby.Id);
            await _outboxRepository.AddAsync(new OutboxEvent
            {
                Id = Guid.NewGuid(),
                EventType = OutboxEventType.DepositHeld,
                Payload = depositHeldPayload,
                IdempotencyKey = $"deposit-held-{reservation.Id:N}",
                ReservationId = reservation.Id,
                LobbyId = lobby.Id,
                UserId = hostId,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync();

            _logger.LogInformation(
                "Reservation confirmed. ReservationId={ReservationId}, LobbyId={LobbyId}, Status={LobbyStatus}, HeldBvc={HeldBvc}",
                reservation.Id, lobby.Id, lobby.Status, quote.FinalDeposit);

            return new ReservationConfirmResponseDto
            {
                ReservationId = reservation.Id,
                LobbyId = lobby.Id,
                RecruitmentDeadline = recruitmentDeadline,
                RequiresCafeApproval = initialLobbyStatus == LobbyStatus.PendingCafeApproval,
                CafeApprovalDeadline = lobby.CafeApprovalDeadline,
                HeldBvc = quote.FinalDeposit
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ExecuteConfirmTransactionAsync FAILED. HostId={HostId}, CafeId={CafeId}, GameId={GameId}, PlayDate={PlayDate}, PreferredStartTime={PreferredStartTime}, PreferredEndTime={PreferredEndTime}, IdempotencyKey={IdempotencyKey}. Exception: {ExceptionType} - {ExceptionMessage}",
                hostId, request.CafeId, request.GameId, request.PlayDate, request.PreferredStartTime, request.PreferredEndTime, request.IdempotencyKey,
                ex.GetType().FullName, ex.Message);
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Check Postgres serialization failure (SQLSTATE 40001) hoặc deadlock (40P01).
    /// </summary>
    private static bool IsSerializationFailure(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        while (inner != null)
        {
            // Postgres Npgsql provider.
            if (inner is Npgsql.PostgresException pgEx)
            {
                if (pgEx.SqlState == "40001" || pgEx.SqlState == "40P01")
                {
                    return true;
                }
            }

            inner = inner.InnerException;
        }

        return false;
    }

    /// <summary>
    /// Helper: load hoặc auto-create wallet entity (không qua DTO mapper).
    /// </summary>
    private async Task<Wallet> GetOrCreateWalletEntityAsync(Guid userId, DateTime now)
    {
        var wallet = await _walletRepository.GetByUserIdForUpdateAsync(userId);
        if (wallet != null)
        {
            return wallet;
        }

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
            CreatedAt = now,
            UpdatedAt = now
        };
        await _walletRepository.AddAsync(wallet);
        await _walletRepository.SaveChangesAsync();
        return wallet;
    }

    private static string SerializeLobbyActivatedPayload(Lobby lobby, Reservation reservation, Guid hostId)
    {
        // Minimal JSON: System.Text.Json không cần helper ngoài.
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            lobbyId = lobby.Id,
            reservationId = reservation.Id,
            hostId,
            cafeId = lobby.CafeId,
            gameId = lobby.GameTemplateId,
            playDate = lobby.PlayDate.ToString(),
            preferredStartTime = lobby.PreferredStartTime?.ToString("HH:mm") ?? "",
            preferredEndTime = lobby.PreferredEndTime?.ToString("HH:mm") ?? "",
            maxPlayers = lobby.MaxMembers,
            recruitmentDeadline = lobby.RecruitmentDeadline,
            lobbyStatus = lobby.Status.ToString(),
            requiresCafeApproval = lobby.Status == LobbyStatus.PendingCafeApproval
        });
    }

    private static string SerializeReservationHeldPayload(Reservation reservation, DepositQuoteResult quote)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            reservationId = reservation.Id,
            lobbyId = reservation.LobbyId,
            hostId = reservation.HostId,
            cafeId = reservation.CafeId,
            heldBvc = reservation.DepositAmount,
            minDepositApplied = reservation.MinDepositApplied,
            riskMultiplier = reservation.RiskMultiplier,
            finalDeposit = quote.FinalDeposit
        });
    }

    private static string SerializeDepositHeldPayload(Guid userId, long amount, Guid reservationId, Guid? lobbyId)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            userId,
            amount,
            reservationId,
            lobbyId,
            entryType = "DEPOSIT_HOLD"
        });
    }

    // ===== 21A.6 CANCEL =====

    /// <summary>
    /// GAP #9 fix: Wrap toàn bộ cancel flow trong 1 Serializable transaction.
    ///
    /// Trước đây: refund BVC → update status → release inventory → SaveChanges rời rạc.
    /// Nếu fail giữa chừng (network, deadlock) → BVC đã refund nhưng status vẫn Holding.
    ///
    /// Sau fix:
    ///   1. BeginTransaction(IsolationLevel.Serializable)
    ///   2. Lock inventory rows (SELECT FOR UPDATE) — tránh race với cancel/timeout khác.
    ///   3. Lock lobby + reservation rows.
    ///   4. Refund BVC → update status → release inventory → outbox event.
    ///   5. Commit.
    ///   Nếu fail → rollback toàn bộ.
    ///
    /// Idempotency: cancellation idempotency key dựa trên reservationId (stable).
    /// Nếu cancel bị retry, idempotency ở wallet layer (refund-{reservationId}) chặn double-refund.
    /// </summary>
    public async Task<CancelReservationResponseDto> CancelAsync(Guid hostId, CancelReservationRequestDto request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        const int MaxRetries = 3;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await ExecuteCancelTransactionAsync(hostId, request, now);
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "CancelAsync serialization failure on attempt {Attempt}/{Max}. ReservationId={ReservationId}. Retrying...",
                    attempt, MaxRetries, request.ReservationId);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));
            }
        }

        throw new InternalServerErrorException(
            ApiErrorMessages.System.CancelRetryExhausted(request.ReservationId, MaxRetries));
    }

    private async Task<CancelReservationResponseDto> ExecuteCancelTransactionAsync(
        Guid hostId,
        CancelReservationRequestDto request,
        DateTime now)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        try
        {
            var reservation = await _reservationRepository.GetByIdAsync(request.ReservationId, includeRelations: true)
                ?? throw new NotFoundException(ApiErrorMessages.Reservation.ReservationNotFound(request.ReservationId));

            if (reservation.HostId != hostId)
            {
                throw new ForbiddenException(ApiErrorMessages.Reservation.OnlyHostCanCancel);
            }

            if (reservation.Status != ReservationStatus.Holding &&
                reservation.Status != ReservationStatus.Confirmed)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Reservation.ReservationStatusInvalidForCancel(reservation.Id, reservation.Status));
            }

            var lobby = reservation.Lobby
                ?? throw new InternalServerErrorException(ApiErrorMessages.Reservation.ReservationMissingLobby(reservation.Id));

            if (lobby.Status != LobbyStatus.PendingActivation &&
                lobby.Status != LobbyStatus.PendingCafeApproval &&
                lobby.Status != LobbyStatus.Open &&
                lobby.Status != LobbyStatus.Viable)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Reservation.LobbyStatusInvalidForCancel(lobby.Id, lobby.Status));
            }

            // GAP #12 fix: Lock inventory rows TRƯỚC khi tính refund để tránh race.
            await ReleaseInventoriesAsync(reservation, now);

            // Tính refund policy (BR-REFUND-02/03).
            // H7 Fix: hasMembers phải check "có member khác host đã tham gia" chứ không phải tổng số row.
            // Trước đây `members.Count > 1` đếm cả host → sai khi host có 2 bản ghi hoặc soft-delete không đúng.
            // BR-REFUND-03 áp dụng khi CHƯA có thành viên nào tham gia = non-host & IsActive.
            var members = await _lobbyRepository.GetMembersAsync(lobby.Id);
            var minutesSinceCreated = (now - reservation.CreatedAt).TotalMinutes;
            var hasMembers = members.Any(m => !m.IsHost && m.IsActive);

            var scheduledStart = reservation.ScheduledStartTime;
            if (scheduledStart == default)
                throw new InternalServerErrorException(
                    ApiErrorMessages.Reservation.CancelMissingScheduledStartTime);
            var refundPolicy = await ComputeRefundPolicyAsync(
                scheduledStart,
                now,
                hasMembers,
                minutesSinceCreated);

            var refundAmount = (long)Math.Round(reservation.DepositAmount * refundPolicy.RefundPercent, MidpointRounding.AwayFromZero);
            var forfeitAmount = reservation.DepositAmount - refundAmount;

            // Idempotency key dựa trên reservationId — stable. Nếu retry → wallet chặn.
            var refundIdempotencyKey = $"refund-{reservation.Id:N}";
            var forfeitIdempotencyKey = $"forfeit-{reservation.Id:N}";

            if (refundAmount > 0)
            {
                await _walletService.ReleaseDepositAsync(
                    hostId,
                    refundAmount,
                    lobby.Id,
                    reservation.Id,
                    refundIdempotencyKey);
            }

            if (forfeitAmount > 0)
            {
                await _walletService.ForfeitDepositAsync(
                    hostId,
                    forfeitAmount,
                    lobby.Id,
                    reservation.Id,
                    forfeitIdempotencyKey);
            }

            // Update reservation + lobby trong cùng transaction.
            reservation.Status = ReservationStatus.CancelledByPlayer;
            reservation.UpdatedAt = now;

            lobby.Status = LobbyStatus.HostCancelled;
            lobby.ClosedAt = now;
            lobby.ClosedReason = request.Reason ?? $"Host hủy - {refundPolicy.PolicyName}";
            lobby.UpdatedAt = now;

            MarkLobbyMembersInactive(lobby, now);

            await _reservationRepository.UpdateAsync(reservation);
            await _lobbyRepository.UpdateAsync(lobby);

            await _reservationRepository.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "Reservation cancelled. ReservationId={ReservationId}, Policy={Policy}, Refund={Refund}, Forfeit={Forfeit}",
                reservation.Id, refundPolicy.PolicyName, refundAmount, forfeitAmount);

            return new CancelReservationResponseDto
            {
                ReservationId = reservation.Id,
                LobbyId = lobby.Id,
                RefundBvc = refundAmount,
                ForfeitBvc = forfeitAmount,
                RefundPolicyApplied = refundPolicy.PolicyName
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// BR-REFUND-08 (walk-in-override-design §2.3):
    /// Host hủy reservation SAU khi đã check-in tại quán (late cancel).
    ///
    /// Workflow:
    /// <list type="number">
    ///   <item><description>Validate Reservation.Status == CheckedIn (đã check-in mới cho cancel).</description></item>
    ///   <item><description>Validate user == Reservation.HostId (chỉ host).</description></item>
    ///   <item><description>Query ActiveSession (link qua Lobby) để lấy StartedAt.</description></item>
    ///   <item><description>Tính playedRatio = (now - StartedAt) / (ScheduledEndTime - ScheduledStartTime).</description></item>
    ///   <item><description>Áp dụng soft-release refund 30% nếu playedRatio ≥ 0.5, ngược lại forfeit 100%.</description></item>
    ///   <item><description>Update Reservation.Status CheckedIn → CancelledByPlayer + Lobby status.</description></item>
    ///   <item><description>Lưu audit log vào PlayerActionHistory.</description></item>
    /// </list>
    /// </summary>
    public async Task<CancelAfterCheckinResponseDto> CancelAfterCheckinAsync(
        Guid hostId,
        CancelAfterCheckinRequestDto request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Load Reservation (kèm Lobby) để check ownership + status.
        var reservation = await _reservationRepository.GetByIdAsync(request.ReservationId, includeRelations: true)
            ?? throw new NotFoundException(ApiErrorMessages.Reservation.NotFound(request.ReservationId));

        // Authorization: chỉ host mới được cancel-after-checkin.
        if (reservation.HostId != hostId)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.OnlyHostCanLateCancelAfterCheckin);
        }

        // Status guard: chỉ cancel khi đã check-in.
        // Cancel từ Holding/Confirmed (chưa check-in) → dùng CancelAsync (BR-REFUND-02/03).
        if (reservation.Status != ReservationStatus.CheckedIn)
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.MustBeCheckedInToLateCancel(reservation.Id, reservation.Status));
        }

        var lobby = reservation.Lobby
            ?? throw new InternalServerErrorException(ApiErrorMessages.Reservation.ReservationMissingLobby(reservation.Id));

        // Scheduled times — Required cho BR-REFUND-08 (BR-RESV-02 §6).
        var scheduledStart = reservation.ScheduledStartTime;
        var scheduledEnd = reservation.ExtendedEndTime ?? reservation.ScheduledEndTime;

        // Query ActiveSession qua Lobby để có StartedAt (BR-05 scan QR timestamp).
        // ActiveSession không có FK trực tiếp → query qua LobbyId.
        var session = await _activeSessionRepository.GetByLobbyIdWithMembersAsync(lobby.Id);
        if (session == null)
        {
            // Đã check-in nhưng chưa start session → coi như played = 0 → forfeit 100%.
            // Edge case này xảy ra nếu reservation.Status bị update thủ công hoặc workflow bug.
            // Vẫn cho cancel nhưng refund = 0 (đúng BR-REFUND-08 fair semantics).
            var zeroRefund = 0L;
            var zeroForfeit = reservation.DepositAmount;

            return await ExecuteCancelAfterCheckinAsync(
                reservation, lobby, session,
                scheduledStart, scheduledEnd,
                now,
                playedMinutes: 0,
                scheduledDurationMinutes: (int)(scheduledEnd - scheduledStart).TotalMinutes,
                refundAmount: zeroRefund,
                forfeitAmount: zeroForfeit,
                policyName: "BR-REFUND-08 < 0.5 (no session)",
                reason: request.Reason,
                ct: default).ConfigureAwait(false);
        }

        var playedMinutes = (int)Math.Max(0, (now - session.StartedAt).TotalMinutes);
        var scheduledDurationMinutes = (int)Math.Max(1, (scheduledEnd - scheduledStart).TotalMinutes);

        // BR-REFUND-08: playedRatio ≥ 0.5 → refund 30%, ngược lại forfeit 100%.
        var calcResult = LateCancelRefundCalculator.Compute(
            reservation.DepositAmount,
            playedMinutes,
            scheduledDurationMinutes);
        var playedRatio = calcResult.PlayedRatio;
        var refundAmount = calcResult.RefundBvc;
        var forfeitAmount = calcResult.ForfeitBvc;
        var policyName = calcResult.PolicyName;

        return await ExecuteCancelAfterCheckinAsync(
            reservation, lobby, session,
            scheduledStart, scheduledEnd,
            now,
            playedMinutes,
            scheduledDurationMinutes,
            refundAmount,
            forfeitAmount,
            policyName,
            request.Reason,
            ct: default).ConfigureAwait(false);
    }

    /// <summary>
    /// Helper cho <see cref="CancelAfterCheckinAsync"/>: thực thi transactional logic.
    /// Tách riêng để giữ method chính dễ đọc + dễ unit test.
    /// </summary>
    private async Task<CancelAfterCheckinResponseDto> ExecuteCancelAfterCheckinAsync(
        Reservation reservation,
        Lobby lobby,
        ActiveSession? session,
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime now,
        int playedMinutes,
        int scheduledDurationMinutes,
        long refundAmount,
        long forfeitAmount,
        string policyName,
        string? reason,
        CancellationToken ct)
    {
        const int MaxRetries = 3;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await ExecuteCancelAfterCheckinTransactionAsync(
                    reservation, lobby, session,
                    scheduledStart, scheduledEnd, now,
                    playedMinutes, scheduledDurationMinutes,
                    refundAmount, forfeitAmount, policyName, reason, ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "CancelAfterCheckinAsync serialization failure on attempt {Attempt}/{Max}. ReservationId={ReservationId}. Retrying...",
                    attempt, MaxRetries, reservation.Id);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), ct).ConfigureAwait(false);
            }
        }

        throw new InternalServerErrorException(
            ApiErrorMessages.System.CancelAfterCheckinRetryExhausted(reservation.Id, MaxRetries));
    }

    private async Task<CancelAfterCheckinResponseDto> ExecuteCancelAfterCheckinTransactionAsync(
        Reservation reservation,
        Lobby lobby,
        ActiveSession? session,
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime now,
        int playedMinutes,
        int scheduledDurationMinutes,
        long refundAmount,
        long forfeitAmount,
        string policyName,
        string? reason,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        try
        {
            // Idempotency key dựa trên reservationId — stable qua retry.
            var refundIdempotencyKey = $"latecancel-refund-{reservation.Id:N}";
            var forfeitIdempotencyKey = $"latecancel-forfeit-{reservation.Id:N}";

            // Refund BVC về host (nếu có).
            if (refundAmount > 0)
            {
                await _walletService.ReleaseDepositAsync(
                    reservation.HostId,
                    refundAmount,
                    lobby.Id,
                    reservation.Id,
                    refundIdempotencyKey).ConfigureAwait(false);

                _logger.LogInformation(
                    "CancelAfterCheckin: Refund {Refund} BVC to host {HostId} for ReservationId={ReservationId} (policy={Policy})",
                    refundAmount, reservation.HostId, reservation.Id, policyName);
            }

            // Forfeit BVC về doanh thu quán (nếu có).
            if (forfeitAmount > 0)
            {
                await _walletService.ForfeitDepositAsync(
                    reservation.HostId,
                    forfeitAmount,
                    lobby.Id,
                    reservation.Id,
                    forfeitIdempotencyKey).ConfigureAwait(false);

                _logger.LogInformation(
                    "CancelAfterCheckin: Forfeit {Forfeit} BVC from host {HostId} for ReservationId={ReservationId} (policy={Policy})",
                    forfeitAmount, reservation.HostId, reservation.Id, policyName);
            }

            // Update Reservation + Lobby status.
            reservation.Status = ReservationStatus.CancelledByPlayer;
            reservation.UpdatedAt = now;
            await _reservationRepository.UpdateAsync(reservation).ConfigureAwait(false);

            lobby.Status = LobbyStatus.HostCancelled;
            lobby.ClosedAt = now;
            lobby.ClosedReason = reason ?? $"Host hủy sau check-in - {policyName}";
            lobby.UpdatedAt = now;
            MarkLobbyMembersInactive(lobby, now);
            await _lobbyRepository.UpdateAsync(lobby).ConfigureAwait(false);

            // Đóng ActiveSession (nếu có) — set Paid + EndedAt.
            if (session != null && session.Status != GroupSessionStatus.Paid)
            {
                session.Status = GroupSessionStatus.Paid;
                session.EndedAt = now;
                session.PaidAt = now;
                session.UpdatedAt = now;
                await _activeSessionRepository.UpdateAsync(session).ConfigureAwait(false);
            }

            await _reservationRepository.SaveChangesAsync().ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);

            var playedRatio = scheduledDurationMinutes > 0
                ? Math.Round((decimal)playedMinutes / scheduledDurationMinutes, 2)
                : 0m;

            return new CancelAfterCheckinResponseDto
            {
                ReservationId = reservation.Id,
                LobbyId = lobby.Id,
                ActiveSessionId = session?.Id,
                PlayedMinutes = playedMinutes,
                ScheduledDurationMinutes = scheduledDurationMinutes,
                PlayedRatio = playedRatio,
                RefundBvc = refundAmount,
                ForfeitBvc = forfeitAmount,
                RefundPolicyApplied = policyName,
                CancelledAt = now
            };
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    // ===== BR-NEW-11 Cafe approval =====

/// <summary>
    /// GAP #10 fix: BR-NEW-11 — Cafe approve/reject lobby trong 1 Serializable transaction.
    ///
    /// Trước đây: approve chỉ update lobby (không cần transaction vì chỉ 1 row).
    /// Reject: refund BVC → update status → release inventory rời rạc.
    /// Nếu fail giữa chừng → BVC đã refund nhưng lobby vẫn ở PendingCafeApproval.
    ///
    /// Sau fix: cả approve + reject đều wrap trong Serializable transaction.
    /// </summary>
    public async Task<CafeApprovalResponseDto> HandleCafeApprovalAsync(
        Guid cafeManagerUserId,
        CafeApprovalRequestDto request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        const int MaxRetries = 3;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await ExecuteCafeApprovalTransactionAsync(cafeManagerUserId, request, now);
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "HandleCafeApprovalAsync serialization failure on attempt {Attempt}/{Max}. ReservationId={ReservationId}. Retrying...",
                    attempt, MaxRetries, request.ReservationId);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));
            }
        }

        throw new InternalServerErrorException(
            ApiErrorMessages.System.CafeApprovalRetryExhausted(request.ReservationId, MaxRetries));
    }

    private async Task<CafeApprovalResponseDto> ExecuteCafeApprovalTransactionAsync(
        Guid cafeManagerUserId,
        CafeApprovalRequestDto request,
        DateTime now)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        try
        {
            var reservation = await _reservationRepository.GetByIdAsync(request.ReservationId, includeRelations: true)
                ?? throw new NotFoundException(ApiErrorMessages.Reservation.ReservationNotFound(request.ReservationId));

            var lobby = reservation.Lobby
                ?? throw new InternalServerErrorException(ApiErrorMessages.Reservation.ReservationMissingLobby(reservation.Id));

            if (lobby.Status != LobbyStatus.PendingCafeApproval)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Reservation.LobbyNotPendingCafeApproval(reservation.Id, lobby.Status));
            }

            // Validate cafe manager quản lý cafe này.
            var cafe = await _db.Cafes.FirstOrDefaultAsync(c => c.Id == reservation.CafeId)
                ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(reservation.CafeId));

            if (cafe.ManagerId != cafeManagerUserId)
            {
                throw new ForbiddenException(ApiErrorMessages.Reservation.NoManagerForCafe(reservation.CafeId));
            }

            if (request.Approve)
            {
                lobby.Status = LobbyStatus.Open;
                lobby.CafeApprovedByUserId = cafeManagerUserId;
                lobby.CafeApprovedAt = now;
                lobby.CafeApprovalDeadline = null;
                lobby.CafeRejectionReason = null;
                lobby.UpdatedAt = now;

                await _lobbyRepository.UpdateAsync(lobby);
                await _lobbyRepository.SaveChangesAsync();
                await tx.CommitAsync();

                _logger.LogInformation(
                    "Lobby approved by cafe. LobbyId={LobbyId}, CafeManager={CafeManager}",
                    lobby.Id, cafeManagerUserId);

                return new CafeApprovalResponseDto
                {
                    ReservationId = reservation.Id,
                    LobbyId = lobby.Id,
                    LobbyStatus = lobby.Status.ToString(),
                    Approved = true,
                    RefundBvc = 0
                };
            }

            // Reject → refund 100% BVC + chuyển reservation/lobby status.
            // Idempotency key dựa trên reservationId (stable).
            var refundIdempotencyKey = $"cafe-reject-{reservation.Id:N}";

            // GAP #12 fix: Lock inventory rows trước khi refund.
            await ReleaseInventoriesAsync(reservation, now);

            await _walletService.ReleaseDepositAsync(
                reservation.HostId,
                reservation.DepositAmount,
                lobby.Id,
                reservation.Id,
                refundIdempotencyKey);

            lobby.Status = LobbyStatus.RejectedByCafe;
            lobby.CafeRejectionReason = request.Reason ?? "Cafe từ chối duyệt lobby.";
            lobby.ClosedAt = now;
            lobby.UpdatedAt = now;
            MarkLobbyMembersInactive(lobby, now);

            reservation.Status = ReservationStatus.CancelledByCafe;
            reservation.UpdatedAt = now;

            await _lobbyRepository.UpdateAsync(lobby);
            await _reservationRepository.UpdateAsync(reservation);

            await _reservationRepository.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "Lobby rejected by cafe. LobbyId={LobbyId}, Reason={Reason}, RefundBvc={RefundBvc}",
                lobby.Id, lobby.CafeRejectionReason, reservation.DepositAmount);

            return new CafeApprovalResponseDto
            {
                ReservationId = reservation.Id,
                LobbyId = lobby.Id,
                LobbyStatus = lobby.Status.ToString(),
                Approved = false,
                RefundBvc = reservation.DepositAmount
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ===== Scheduler: process deadline =====

    public async Task<int> ProcessDeadlineReservationsAsync(DateTime cutoff, int batchSize, CancellationToken ct)
    {
        // GAP #23 fix: wrap batch transaction để FOR UPDATE SKIP LOCKED có hiệu lực.
        // Reservation row đã lock tại GetDueForDeadlineAsync → commit/rollback giải phóng lock.
        await using var batchTx = await _db.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        var reservations = await _reservationRepository.GetDueForDeadlineAsync(cutoff, batchSize);
        var processed = 0;
        var failed = 0;

        try
        {
            foreach (var reservation in reservations)
            {
                ct.ThrowIfCancellationRequested();

                // Per-reservation try/catch: 1 reservation fail KHÔNG rollback toàn batch.
                try
                {
                    await ProcessSingleDeadlineAsync(reservation, cutoff);
                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex,
                        "[Deadline] ReservationId={ReservationId} failed. Continuing with next reservation.",
                        reservation.Id);
                    _db.ChangeTracker.Clear();
                }
            }

            await batchTx.CommitAsync(ct);
        }
        catch
        {
            await batchTx.RollbackAsync(ct);
            throw;
        }

        if (failed > 0)
        {
            _logger.LogWarning(
                "[Deadline] Batch completed with {Failed} failures out of {Total} reservations.",
                failed, reservations.Count);
        }

        return processed;
    }

    /// <summary>
    /// GAP #11 fix: Wrap từng deadline processing trong 1 Serializable transaction.
    /// Trước: update status + refund + release inventory rời rạc → race với member join / cancel.
    /// Lưu ý: transaction cấp batch đã được mở ở <see cref="ProcessDeadlineReservationsAsync"/>
    /// (FOR UPDATE SKIP LOCKED cần transaction bao ngoài). Không mở thêm transaction
    /// con ở đây — Npgsql không cho phép nested transaction.
    /// </summary>
    private async Task ProcessSingleDeadlineAsync(Reservation reservation, DateTime now)
    {
        if (reservation.Status != ReservationStatus.Holding)
        {
            return;
        }

        var lobby = await _lobbyRepository.GetByIdAsync(reservation.LobbyId ?? Guid.Empty);
        if (lobby == null)
        {
            return;
        }

        const int MaxRetries = 3;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (reservation.CurrentPlayers >= reservation.MinPlayers)
                {
                    // BR-LOBBY-READY-01: Deadline đến mà đủ minPlayers → lobby Viable/Full nhưng KHÔNG chuyển
                    // reservation sang Confirmed. Reservation chỉ Confirmed khi lobby đạt WaitingCheckIn
                    // (tất cả members ready). Trước deadline, lobby có thể vẫn đang tuyển thêm.
                    if (reservation.CurrentPlayers >= reservation.MaxPlayers)
                    {
                        lobby.Status = LobbyStatus.Full;
                    }
                    else if (lobby.Status == LobbyStatus.PendingCafeApproval
                             || lobby.Status == LobbyStatus.PendingActivation)
                    {
                        // Cafe chưa duyệt nhưng đã đạt minPlayers → Viable chờ duyệt.
                        lobby.Status = LobbyStatus.Viable;
                    }
                    else
                    {
                        lobby.Status = LobbyStatus.Viable;
                    }

                    lobby.UpdatedAt = now;
                    reservation.Status = ReservationStatus.Holding; // giữ Holding chờ ready

                    await _reservationRepository.UpdateAsync(reservation);
                    await _lobbyRepository.UpdateAsync(lobby);
                    await _reservationRepository.SaveChangesAsync();

                    _logger.LogInformation(
                        "Reservation Holding at deadline (chờ WaitingCheckIn). ReservationId={ReservationId}, Players={Players}, LobbyStatus={LobbyStatus}",
                        reservation.Id, reservation.CurrentPlayers, lobby.Status);
                }
                else
                {
                    // Timeout → refund 100% BVC.
                    // GAP #12 fix: Lock inventory rows trước khi refund.
                    await ReleaseInventoriesAsync(reservation, now);

                    var refundIdempotencyKey = $"timeout-{reservation.Id:N}";
                    await _walletService.ReleaseDepositAsync(
                        reservation.HostId,
                        reservation.DepositAmount,
                        lobby.Id,
                        reservation.Id,
                        refundIdempotencyKey);

                    reservation.Status = ReservationStatus.Expired;
                    lobby.Status = LobbyStatus.TimeoutFailed;
                    lobby.ClosedAt = now;
                    lobby.ClosedReason = "Phòng không đủ thành viên tối thiểu trước thời hạn tuyển người. Tiền cọc đã hoàn về ví của bạn.";
                    lobby.UpdatedAt = now;
                    MarkLobbyMembersInactive(lobby, now);

                    await _reservationRepository.UpdateAsync(reservation);
                    await _lobbyRepository.UpdateAsync(lobby);
                    await _reservationRepository.SaveChangesAsync();

                    _logger.LogInformation(
                        "Reservation timeout. ReservationId={ReservationId}, RefundBvc={RefundBvc}",
                        reservation.Id, reservation.DepositAmount);
                }
                return;
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "ProcessSingleDeadlineAsync serialization failure on attempt {Attempt}/{Max}. ReservationId={ReservationId}. Retrying...",
                    attempt, MaxRetries, reservation.Id);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));

                // Reload fresh state cho retry.
                var fresh = await _reservationRepository.GetByIdAsync(reservation.Id, includeRelations: true);
                if (fresh == null || fresh.Status != ReservationStatus.Holding)
                {
                    return;
                }
                reservation = fresh;
                lobby = fresh.Lobby ?? lobby;
            }
        }
    }

    // ===== Scheduler: process cafe approval expiry =====

    public async Task<int> ProcessCafeApprovalExpiryAsync(DateTime cutoff, int batchSize, CancellationToken ct)
    {
        // GAP #23 fix: batch transaction cho FOR UPDATE SKIP LOCKED.
        await using var batchTx = await _db.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        var reservations = await _reservationRepository.GetDueForCafeApprovalExpiryAsync(cutoff, batchSize);
        var processed = 0;
        var failed = 0;

        try
        {
            foreach (var reservation in reservations)
            {
                ct.ThrowIfCancellationRequested();

                // Per-reservation try/catch: 1 reservation fail KHÔNG rollback toàn batch.
                try
                {
                    await ProcessSingleCafeApprovalExpiryAsync(reservation, cutoff);
                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex,
                        "[CafeApprovalExpiry] ReservationId={ReservationId} failed. Continuing with next reservation.",
                        reservation.Id);
                    _db.ChangeTracker.Clear();
                }
            }

            await batchTx.CommitAsync(ct);
        }
        catch
        {
            await batchTx.RollbackAsync(ct);
            throw;
        }

        if (failed > 0)
        {
            _logger.LogWarning(
                "[CafeApprovalExpiry] Batch completed with {Failed} failures out of {Total} reservations.",
                failed, reservations.Count);
        }

        return processed;
    }

    /// <summary>
    /// GAP #11 fix: Wrap cafe approval expiry trong Serializable transaction.
    /// </summary>
    private async Task ProcessSingleCafeApprovalExpiryAsync(Reservation reservation, DateTime now)
    {
        var lobby = await _lobbyRepository.GetByIdAsync(reservation.LobbyId ?? Guid.Empty);
        if (lobby == null || lobby.Status != LobbyStatus.PendingCafeApproval)
        {
            return;
        }

        const int MaxRetries = 3;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // GAP #12 fix: Lock inventory rows trước khi refund.
                await ReleaseInventoriesAsync(reservation, now);

                var refundIdempotencyKey = $"cafe-expired-{reservation.Id:N}";
                await _walletService.ReleaseDepositAsync(
                    reservation.HostId,
                    reservation.DepositAmount,
                    lobby.Id,
                    reservation.Id,
                    refundIdempotencyKey);

                reservation.Status = ReservationStatus.Expired;
                lobby.Status = LobbyStatus.ExpiredByCafe;
                lobby.ClosedAt = now;
                lobby.ClosedReason = "Cafe không duyệt lobby trong 24 giờ.";
                lobby.UpdatedAt = now;
                MarkLobbyMembersInactive(lobby, now);

                await _reservationRepository.UpdateAsync(reservation);
                await _lobbyRepository.UpdateAsync(lobby);
                await _reservationRepository.SaveChangesAsync();

                _logger.LogInformation(
                    "Reservation expired by cafe no-approval. ReservationId={ReservationId}",
                    reservation.Id);
                return;
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "ProcessSingleCafeApprovalExpiryAsync serialization failure on attempt {Attempt}/{Max}. ReservationId={ReservationId}. Retrying...",
                    attempt, MaxRetries, reservation.Id);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));

                var fresh = await _lobbyRepository.GetByIdAsync(reservation.LobbyId ?? Guid.Empty);
                if (fresh == null || fresh.Status != LobbyStatus.PendingCafeApproval)
                {
                    return;
                }
                lobby = fresh;
            }
        }
    }

    // ===== Scheduler: process no-show =====

    public async Task<int> ProcessNoShowAsync(DateTime cutoff, int batchSize, CancellationToken ct)
    {
        // GAP #23 fix: batch transaction cho FOR UPDATE SKIP LOCKED.
        await using var batchTx = await _db.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        var reservations = await _reservationRepository.GetDueForNoShowAsync(cutoff, batchSize);
        var processed = 0;
        var failed = 0;

        try
        {
            foreach (var reservation in reservations)
            {
                ct.ThrowIfCancellationRequested();

                // Per-reservation try/catch: 1 reservation fail KHÔNG rollback toàn batch
                // (trước đây outer catch rollback → mất hết công sức các reservation OK khác).
                // Trước fix: stack trace 956-967 — `Cần 0 BVC nhưng chỉ có 50 BVC`
                // khiến toàn bộ batch rollback, retry mãi → log spam.
                try
                {
                    await ProcessSingleNoShowAsync(reservation, cutoff);
                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex,
                        "[NoShow] ReservationId={ReservationId} failed. Continuing with next reservation.",
                        reservation.Id);
                    // Detach để EF không track entity này nữa trong batch tx.
                    _db.ChangeTracker.Clear();
                }
            }

            await batchTx.CommitAsync(ct);
        }
        catch
        {
            await batchTx.RollbackAsync(ct);
            throw;
        }

        if (failed > 0)
        {
            _logger.LogWarning(
                "[NoShow] Batch completed with {Failed} failures out of {Total} reservations.",
                failed, reservations.Count);
        }

        return processed;
    }

    /// <summary>
    /// GAP #11 fix: Wrap no-show processing trong Serializable transaction.
    /// Lưu ý: transaction cấp batch đã được mở ở <see cref="ProcessNoShowAsync"/>
    /// (FOR UPDATE SKIP LOCKED cần transaction bao ngoài). Không mở thêm transaction
    /// con ở đây — Npgsql không cho phép nested transaction.
    /// </summary>
    private async Task ProcessSingleNoShowAsync(Reservation reservation, DateTime now)
    {
        if (reservation.Status != ReservationStatus.Confirmed)
        {
            return;
        }

        var lobby = await _lobbyRepository.GetByIdAsync(reservation.LobbyId ?? Guid.Empty);
        if (lobby == null)
        {
            return;
        }

        // GAP-R6-RT-NEW fix v3: LobbyRepository.GetByIdAsync include Lobby.Reservation
        // navigation → Reservation instance đã tracked qua nav. Input `reservation`
        // parameter là instance khác (cùng Id) — gọi _db.Reservations.Update()/Entry().State
        // đều throw identity conflict. Lấy instance đã tracked từ lobby.Reservation nav
        // và apply thay đổi trên đó. Nếu nav null (lobby không include), dùng input.
        var trackedReservation = lobby.Reservation ?? reservation;

        const int MaxRetries = 3;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // Forfeit 100% (no-show).
                // Idempotency key dựa trên reservationId (stable).
                var forfeitIdempotencyKey = $"no-show-{reservation.Id:N}";

                trackedReservation.Status = ReservationStatus.NoShow;
                trackedReservation.UpdatedAt = now;
                lobby.Status = LobbyStatus.Closed;
                lobby.ClosedAt = now;
                lobby.ClosedReason = "No-show (không check-in sau grace period).";
                lobby.UpdatedAt = now;
                MarkLobbyMembersInactive(lobby, now);

                await _reservationRepository.UpdateAsync(trackedReservation);
                await _lobbyRepository.UpdateAsync(lobby);

                // BR-REFUND-03: no-show forfeit 100%. Nếu DepositAmount = 0
                // (test edge case hoặc quote đặc biệt) thì bỏ qua — không có gì để forfeit.
                if (trackedReservation.DepositAmount > 0)
                {
                    try
                    {
                        await _walletService.ForfeitDepositAsync(
                            trackedReservation.HostId,
                            trackedReservation.DepositAmount,
                            lobby.Id,
                            trackedReservation.Id,
                            forfeitIdempotencyKey);
                    }
                    catch (BadRequestException forfeitEx)
                    {
                        // GAP-R6-RT-NEW fix v4: data inconsistency defense. Nếu wallet
                        // HeldBalance < DepositAmount (vd. đã release bởi timeout job,
                        // manual refund, partial refund trước đó), KHÔNG fail cả
                        // no-show pipeline — vẫn commit status change. Log warning để
                        // admin investigate data drift.
                        _logger.LogWarning(
                            "[NoShow] Forfeit skipped due to wallet data inconsistency. " +
                            "ReservationId={ReservationId}, DepositAmount={DepositAmount}, " +
                            "Reason={Reason}. Status change vẫn được commit.",
                            trackedReservation.Id, trackedReservation.DepositAmount, forfeitEx.Message);
                    }
                }

                await _reservationRepository.SaveChangesAsync();

                _logger.LogInformation(
                    "Reservation no-show. ReservationId={ReservationId}, ForfeitBvc={ForfeitBvc}",
                    trackedReservation.Id, trackedReservation.DepositAmount);
                return;
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "ProcessSingleNoShowAsync serialization failure on attempt {Attempt}/{Max}. ReservationId={ReservationId}. Retrying...",
                    attempt, MaxRetries, reservation.Id);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));

                var fresh = await _reservationRepository.GetByIdAsync(reservation.Id, includeRelations: true);
                if (fresh == null || fresh.Status != ReservationStatus.Confirmed)
                {
                    return;
                }
                reservation = fresh;
                lobby = fresh.Lobby ?? lobby;
            }
        }
    }

    /// <summary>
    /// GAP-9 Fix: Retry BVC capture cho các ActiveSession đã PAID nhưng chưa capture thành công.
    /// Chạy qua background job mỗi 5 phút.
    /// </summary>
    public async Task<int> ProcessBvcCaptureRetryAsync(DateTime cutoff, int batchSize, CancellationToken ct)
    {
        var sessions = await _activeSessionRepository.GetSessionsNeedingBvcCaptureRetryAsync(batchSize);
        var processed = 0;

        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (session.LobbyId.HasValue)
                {
                    await CompleteAndCaptureAsync(session.LobbyId.Value, session.Id, ct);
                    _logger.LogInformation(
                        "BvcCaptureRetry: SessionId={SessionId}, LobbyId={LobbyId} captured successfully",
                        session.Id, session.LobbyId.Value);
                }
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "BvcCaptureRetry: Failed for SessionId={SessionId}, LobbyId={LobbyId}. Will retry next cycle.",
                    session.Id, session.LobbyId);
            }
        }

        return processed;
    }

    // ===== BR §21A.7 Check-in =====

    public async Task<ReservationCheckInResponseDto> CheckInAsync(Guid staffUserId, ReservationCheckInRequestDto request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // 1. Lookup reservation bằng ReservationCode.
        var reservation = await _reservationRepository.GetByReservationCodeAsync(request.ReservationCode.Trim());
        if (reservation == null)
        {
            throw new NotFoundException(ApiErrorMessages.Reservation.ReservationNotFoundByCode);
        }

        // GAP #1 fix: validate ownership — staff cafe A không được scan QR của cafe B.
        if (reservation.CafeId != request.CafeId)
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.CafeMismatchOnCheckIn(reservation.CafeId, request.CafeId));
        }

        // GAP #2 fix: validate time window theo BR §21A.7 step 3.
        // Cho phép check-in từ scheduledTime - 30 phút (early grace) đến endTime + 30 phút (late grace).
        await ValidateCheckInTimeWindowAsync(reservation, now);

        // 2. Idempotency: nếu đã CheckedIn → trả kết quả cũ.
        if (reservation.Status == ReservationStatus.CheckedIn)
        {
            _logger.LogInformation(
                "Check-in idempotent replay. ReservationCode={Code}, ReservationId={ReservationId}",
                request.ReservationCode, reservation.Id);

            var existingLobby = await _lobbyRepository.GetByIdAsync(reservation.LobbyId ?? Guid.Empty);
            return new ReservationCheckInResponseDto
            {
                ReservationId = reservation.Id,
                LobbyId = reservation.LobbyId ?? Guid.Empty,
                ActiveSessionId = request.ActiveSessionId,
                ReservationStatus = reservation.Status.ToString(),
                LobbyStatus = existingLobby?.Status.ToString() ?? LobbyStatus.InProgress.ToString(),
                CheckedInAt = existingLobby?.UpdatedAt ?? now,
                HeldBvc = reservation.DepositAmount,
                TableNumber = reservation.TableNumber
            };
        }

        // 3. Validate status — chỉ Confirmed mới được check-in.
        if (reservation.Status != ReservationStatus.Confirmed)
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.OnlyConfirmedCanCheckIn(reservation.Id, reservation.Status));
        }

        // 4. Atomic transaction: status flip + inventory move + outbox.
        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await ExecuteCheckInTransactionAsync(reservation, staffUserId, request, now, cancellationToken);
            }
            catch (DbUpdateException dbx) when (IsSerializationFailure(dbx) && attempt < maxRetries)
            {
                _logger.LogWarning(
                    "CheckInAsync serialization failure attempt {Attempt}/{Max}. Retrying. ReservationCode={Code}",
                    attempt, maxRetries, request.ReservationCode);

                _db.ChangeTracker.Clear();
                reservation = await _reservationRepository.GetByReservationCodeAsync(request.ReservationCode.Trim())
                    ?? throw new NotFoundException(ApiErrorMessages.Reservation.ReservationNotFoundByCode);
            }
        }

        throw new InternalServerErrorException(
            ApiErrorMessages.System.CheckInRetryExhausted(reservation.Id, maxRetries));
    }

    public async Task<ReservationCheckInResponseDto> CheckInByCodeAsync(
        Guid staffUserId,
        string reservationCode,
        CheckInByCodeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // Chuyển đổi sang ReservationCheckInRequestDto để reuse logic
        var checkInRequest = new ReservationCheckInRequestDto
        {
            CafeId = request.CafeId,
            ReservationCode = reservationCode,
            ActiveSessionId = request.ActiveSessionId,
            TableNumber = request.TableNumber,
            IdempotencyKey = request.IdempotencyKey
                ?? $"pos-checkin:{reservationCode}:{Guid.NewGuid():N}"
        };

        return await CheckInAsync(staffUserId, checkInRequest);
    }

    private async Task<ReservationCheckInResponseDto> ExecuteCheckInTransactionAsync(
        Reservation reservation,
        Guid staffUserId,
        ReservationCheckInRequestDto request,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        try
        {
            // 5. Lock seat inventory + game inventory (BR §17.3).
            // BR-NEW-15: Dùng SeatInventoryId/GameInventoryId FK thay vì query theo TimeSlot.
            SeatInventory? seatInventory;
            GameInventory? gameInventory;
            if (reservation.SeatInventoryId != null)
            {
                seatInventory = await _seatInventoryRepository.GetByIdForUpdateAsync(reservation.SeatInventoryId.Value);
            }
            else
            {
                seatInventory = await _seatInventoryRepository.GetForUpdateAsync(
                    reservation.CafeId, reservation.PlayDate,
                    reservation.PreferredStartTime ?? TimeOnly.MinValue,
                    reservation.PreferredEndTime ?? TimeOnly.MaxValue);
            }
            if (seatInventory == null)
            {
                throw new ConflictException(
                    ApiErrorMessages.System.SeatInventoryMissingForReservation(
                        reservation.CafeId, reservation.PlayDate,
                        $"{reservation.ScheduledStartTime:HH:mm}-{reservation.ScheduledEndTime:HH:mm}"));
            }

            if (reservation.GameInventoryId != null)
            {
                gameInventory = await _gameInventoryRepository.GetByIdForUpdateAsync(reservation.GameInventoryId.Value);
            }
            else
            {
                gameInventory = await _gameInventoryRepository.GetForUpdateAsync(
                    reservation.CafeId, reservation.GameId, reservation.PlayDate,
                    reservation.PreferredStartTime ?? TimeOnly.MinValue,
                    reservation.PreferredEndTime ?? TimeOnly.MaxValue);
            }
            if (gameInventory == null)
            {
                throw new ConflictException(
                    ApiErrorMessages.System.GameInventoryMissingForReservation(
                        reservation.CafeId, reservation.PlayDate,
                        $"{reservation.ScheduledStartTime:HH:mm}-{reservation.ScheduledEndTime:HH:mm}"));
            }

            // 6. Validate inventory state — must be Held.
            if (seatInventory.HeldSeats < reservation.MaxPlayers)
            {
                throw new ConflictException(
                    ApiErrorMessages.Reservation.SeatInventoryStateInvalid(seatInventory.HeldSeats, reservation.MaxPlayers));
            }

            if (gameInventory.HeldCopies < 1)
            {
                throw new ConflictException(ApiErrorMessages.Reservation.GameInventoryStateInvalid);
            }

            // 7. Move seat: held → inUse.
            seatInventory.HeldSeats -= reservation.MaxPlayers;
            seatInventory.InUseSeats += reservation.MaxPlayers;
            seatInventory.UpdatedAt = now;
            await _seatInventoryRepository.UpdateAsync(seatInventory);

            // 8. Move game copy: held → inUse.
            gameInventory.HeldCopies -= 1;
            gameInventory.InUseCopies += 1;
            gameInventory.UpdatedAt = now;
            await _gameInventoryRepository.UpdateAsync(gameInventory);

            // 9. Update reservation.
            // FIX 2026-08-27: set CheckedInAt để downstream (CompleteAndCaptureAsync,
            // EndAndSettleAsync, Karma aggregation) tính playedRatio chính xác.
            // Trước đây field này không được set → reservation.CheckedInAt = NULL trong DB
            // → playedRatio fallback về ScheduledStartTime, âm hoặc sai semantic khi
            // check-in muộn hơn scheduledStart. Đây là root cause của "bàn tự đóng".
            reservation.Status = ReservationStatus.CheckedIn;
            reservation.CheckedInAt = now;
            reservation.TableNumber = request.TableNumber;
            reservation.UpdatedAt = now;
            await _reservationRepository.UpdateAsync(reservation);

            // 10. Update lobby.
            var lobby = await _lobbyRepository.GetByIdAsync(reservation.LobbyId ?? Guid.Empty);
            if (lobby == null)
            {
                throw new InternalServerErrorException(
                    ApiErrorMessages.Reservation.ReservationMissingLobby(reservation.Id));
            }

            lobby.Status = LobbyStatus.InProgress;
            lobby.UpdatedAt = now;
            await _lobbyRepository.UpdateAsync(lobby);

            // 11. Outbox event LobbyCheckedIn (BR-REQUIRED §17.5).
            // FIX 2026-08-27: dùng reservation.CheckedInAt (đã set ở step 9) thay vì raw `now`
            // để outbox payload nhất quán với entity, tránh trường hợp `now` bị drift.
            var checkedInAt = reservation.CheckedInAt ?? now;
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                reservationId = reservation.Id,
                lobbyId = lobby.Id,
                activeSessionId = request.ActiveSessionId,
                staffUserId,
                hostId = reservation.HostId,
                cafeId = reservation.CafeId,
                checkedInAt,
                heldBvc = reservation.DepositAmount,
                maxPlayers = reservation.MaxPlayers
            });

            await _outboxRepository.AddAsync(new OutboxEvent
            {
                Id = Guid.NewGuid(),
                EventType = OutboxEventType.LobbyCheckedIn,
                Payload = payload,
                IdempotencyKey = request.IdempotencyKey,
                ReservationId = reservation.Id,
                LobbyId = lobby.Id,
                UserId = reservation.HostId,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync();

            _logger.LogInformation(
                "Reservation checked-in. ReservationId={ReservationId}, LobbyId={LobbyId}, ActiveSessionId={ActiveSessionId}",
                reservation.Id, lobby.Id, request.ActiveSessionId);

            return new ReservationCheckInResponseDto
            {
                ReservationId = reservation.Id,
                LobbyId = lobby.Id,
                ActiveSessionId = request.ActiveSessionId,
                ReservationStatus = reservation.Status.ToString(),
                LobbyStatus = lobby.Status.ToString(),
                CheckedInAt = checkedInAt,
                HeldBvc = reservation.DepositAmount,
                TableNumber = request.TableNumber
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// GAP #2 fix: BR §21A.7 step 3 — "Thời gian nằm trong khung giờ cho phép".
    ///
    /// Time window:
    /// - Early grace: scheduledTime - 15 phút (BR-CHECKIN-01: cho phép khách đến sớm).
    /// - Late grace: scheduledEndTime + 30 phút (BR-END-05: grace period, không tính extra).
    ///
    /// Trả 400 Bad Request qua ApiExceptionMiddleware nếu ngoài window.
    /// </summary>
    private async Task ValidateCheckInTimeWindowAsync(Reservation reservation, DateTime now)
    {
        // BR-CHECKIN-01: Check-in trong [-15 min, +30 min] quanh [ScheduledStartTime, ScheduledEndTime].
        const int EarlyGraceMinutes = 15;
        const int LateGraceMinutes = 30;

        var scheduledStart = reservation.ScheduledStartTime;
        var scheduledEnd = reservation.ScheduledEndTime;
        // BR-NEW-15: ResolveAsync takes (cafeId, playDate) without TimeSlot.
        var resolvedSchedule = await _scheduleResolver.ResolveAsync(reservation.CafeId, reservation.PlayDate);

        var windowStart = scheduledStart.AddMinutes(-EarlyGraceMinutes);
        var windowEnd = scheduledEnd.AddMinutes(LateGraceMinutes);

        var bypassCheckInWindow = await TimeWindowGuard.ShouldBypassAsync(
            _httpContextAccessor?.HttpContext, _configProvider, _logger,
            operation: "Reservation.CheckInWindow", entityId: reservation.Id);
        if (bypassCheckInWindow)
        {
            return;
        }

        // BR-DEMO-04: Demo mode → cho phép check-in sớm bất kỳ (không giới hạn early grace).
        var bypassCheckInDemo = await DemoGuard.ShouldBypassDemoLocksAsync(
            _httpContextAccessor?.HttpContext, _configProvider, _logger,
            operation: "Reservation.CheckInWindow.Demo", entityId: reservation.Id);
        if (bypassCheckInDemo)
        {
            return;
        }

        if (now < windowStart)
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.CheckInTimeWindowInvalid(
                    reservation.Id, scheduledStart, windowStart, windowEnd));
        }

        if (now > windowEnd)
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.CheckInTimeWindowLate(
                    reservation.Id, scheduledEnd, windowEnd));
        }
    }

    /// <summary>
    /// Wrapper TimeWindowGuard cho lobby buffer (BR-LOBBY-01a/b) và các deadline khác.
    /// </summary>
    private Task<bool> ShouldBypassLobbyBufferAsync()
    {
        // Lớp 1: TimeWindowGuard (bypass_time_window_validations DB hoặc per-request).
        // Lớp 2: DemoGuard (demo_loosen_lobby_constraints) — BR-DEMO-02.
        // Trả true nếu 1 trong 2 bật.
        return ShouldBypassLobbyBufferCombinedAsync();
    }

    private async Task<bool> ShouldBypassLobbyBufferCombinedAsync()
    {
        var bypassTw = await TimeWindowGuard.ShouldBypassAsync(
            _httpContextAccessor?.HttpContext, _configProvider, _logger,
            operation: "Reservation.LobbyBuffer");
        if (bypassTw) return true;

        return await DemoGuard.ShouldBypassDemoLocksAsync(
            _httpContextAccessor?.HttpContext, _configProvider, _logger,
            operation: "Reservation.LobbyBuffer.Demo");
    }

    // ===== Helpers =====

    private async Task<HostReservationContext> BuildHostEligibilityContextAsync(
        Guid hostId,
        ReservationQuoteRequestDto request,
        DepositQuoteResult quote,
        Wallet wallet,
        DateTime now)
    {
        // BR-NEW-15: Dùng PreferredStartTime/PreferredEndTime thay vì TimeSlot.
        var overlapList = await _lobbyRepository.GetOverlappingLobbiesAsync(
            hostId, request.PlayDate, request.PreferredStartTime, request.PreferredEndTime, now);
        var firstOverlap = overlapList.FirstOrDefault();

        var activeLobbyByHost = await _lobbyRepository.GetActiveLobbiesByHostAsync(hostId);
        var activeLobbyByMember = await _lobbyRepository.GetActiveLobbiesByMemberAsync(hostId);

        var activeLobbyOnPlayDate = await _lobbyRepository.GetActiveLobbiesByHostAsync(hostId, request.PlayDate);
        var activeLobbyOnCafeSlot = await _lobbyRepository.GetActiveLobbiesByCafeDateSlotAsync(
            hostId, request.CafeId, request.PlayDate, request.PreferredStartTime, request.PreferredEndTime);

        var hostCreateOrCancelCount = await _reservationRepository.CountHostActionsForPlayDateAsync(hostId, request.PlayDate);

        var (scheduledStartTime, scheduledEndTime) = CafeSchedule.BuildScheduledStartEndFromPreferred(
            request.PlayDate, request.PreferredStartTime, request.PreferredEndTime);
        var recruitmentDeadline = scheduledStartTime.AddMinutes(-20);

        return new HostReservationContext
        {
            HostId = hostId,
            CafeId = request.CafeId,
            PlayDate = request.PlayDate,
            RecruitmentDeadline = recruitmentDeadline,
            Now = now,
            PreferredScheduledStart = scheduledStartTime,
            PreferredScheduledEnd = scheduledEndTime,
            IsVip = false,
            IsRiskMultiplierHigh = wallet.RiskMultiplier >= 1.25m,
            IsCoolingOff = wallet.IsCoolingOff,
            IsAccountSuspended = wallet.AccountStatus == AccountStatus.Suspended
                || wallet.AccountStatus == AccountStatus.Restricted,
            IsAccountBanned = wallet.AccountStatus == AccountStatus.Banned,
            WalletHeldBalance = wallet.HeldBalance,
            FinalDeposit = quote.FinalDeposit,
            HasActiveHostLobby = activeLobbyByHost.Count >= 1,
            HasActiveMemberLobby = activeLobbyByMember.Count >= 1,
            HasOverlapHostLobby = overlapList.Any(),
            HasActiveLobbyOnPlayDate = activeLobbyOnPlayDate.Count >= 1,
            HasActiveLobbyOnCafeSlot = activeLobbyOnCafeSlot.Count >= 1,
            HostCreateOrCancelCount = hostCreateOrCancelCount,
            OverlapOtherDeadline = firstOverlap?.RecruitmentDeadline,
            OverlapOtherStart = firstOverlap?.ScheduledStartTime,
            CoolingOffExpiresAt = wallet.IsCoolingOff ? wallet.CoolingOffExpiresAt : null
        };
    }

    private async Task ValidateCafeAndGameAsync(ReservationQuoteRequestDto request)
    {
        var cafe = await _db.Cafes.FirstOrDefaultAsync(c => c.Id == request.CafeId);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(request.CafeId));
        }

        if (!cafe.IsActive)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.CafeNotActive);
        }

        var game = await _gameRepository.GetByIdAsync(request.GameId);
        if (game == null)
        {
            throw new NotFoundException(ApiErrorMessages.BoardGame.MasterNotFound(request.GameId));
        }

        // Check cafe có game này chưa.
        var hasGame = await _db.CafeGameInventories
            .AnyAsync(cgi => cgi.CafeId == request.CafeId && cgi.GameTemplateId == request.GameId);
        if (!hasGame)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.GameNotInCafeInventory);
        }
    }

    private static void ValidatePlayDate(DateOnly playDate, DateTime now)
    {
        var today = DateOnly.FromDateTime(now.Date);
        var maxDate = today.AddDays(MaxAdvanceBookingDays);
        if (playDate < today || playDate > maxDate)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.PlayDateOutOfRange(MaxAdvanceBookingDays));
        }
    }

    private static void ValidatePlayersWindow(ReservationQuoteRequestDto request)
    {
        ValidatePlayersWindowRaw(request.MinPlayers, request.MaxPlayers);
    }

    private static void ValidatePlayersWindowRaw(int minPlayers, int maxPlayers)
    {
        // Solo play (MinPlayers = 1) được phép.
        if (minPlayers < 1)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.MinPlayersLessThanTwo);
        }

        if (maxPlayers < minPlayers)
        {
            throw new BadRequestException(
                ApiErrorMessages.Reservation.MinGreaterThanMaxPlayers(minPlayers, maxPlayers));
        }
    }

    /// <summary>
    /// Validate preferredStartTime/preferredEndTime nằm trong giờ mở/đóng thực tế của cafe (theo CafeScheduleOverride).
    /// BR-NEW-15: Dùng IScheduleResolver để lấy giờ resolved cho playDate.
    /// Xử lý overnight: nếu preferredEnd < preferredStart, validate preferredEnd với schedule ngày kế tiếp.
    /// </summary>
    private async Task ValidatePreferredTimesWithCafeScheduleAsync(
        Guid cafeId,
        DateOnly playDate,
        TimeOnly preferredStart,
        TimeOnly preferredEnd,
        CancellationToken cancellationToken = default)
    {
        // Delegate to shared helper
        await Helpers.CafeScheduleValidator.ValidatePreferredTimesWithCafeScheduleAsync(
            _scheduleResolver,
            cafeId,
            playDate,
            preferredStart,
            preferredEnd,
            cancellationToken);
    }

    private static LobbyStatus DetermineInitialLobbyStatus(
        Reservation reservation,
        CafeConfig cafeConfig,
        DateTime now,
        bool isPrivate)
    {
        // BR-LOBBY-PRIVACY-01 + BR-NEW-11: private lobby bỏ qua cafe approval.
        // Chỉ public lobby có playDate >= DistantThresholdDays mới cần duyệt.
        if (isPrivate)
        {
            return LobbyStatus.PendingActivation;
        }

        var daysInFuture = (reservation.PlayDate.ToDateTime(TimeOnly.MinValue) - now.Date).TotalDays;
        var requiresApproval = daysInFuture >= cafeConfig.DistantThresholdDays
            && (reservation.MaxPlayers > 10 || cafeConfig.RequireApprovalForDistant);

        return requiresApproval ? LobbyStatus.PendingCafeApproval : LobbyStatus.PendingActivation;
    }

    private async Task<(string PolicyName, decimal RefundPercent)> ComputeRefundPolicyAsync(
        DateTime scheduledTime,
        DateTime now,
        bool hasMembers,
        double minutesSinceCreated)
    {
        // Bypass time-window: hoàn 100% regardless of milestone (dev/test only).
        if (await ShouldBypassLobbyBufferAsync())
        {
            return ("BypassTimeWindow", 1.0m);
        }

        // BR-REFUND-03: grace 15 phút + chưa có member → hoàn 100%.
        if (minutesSinceCreated <= 15 && !hasMembers)
        {
            return ("Grace-15p-NoMember", 1.0m);
        }

        var hoursUntilPlay = (scheduledTime - now).TotalHours;

        // BR-REFUND-02.
        if (hoursUntilPlay >= 24)
        {
            return ("Cancel-24h", 1.0m);
        }

        if (hoursUntilPlay >= 6)
        {
            return ("Cancel-6h", 0.5m);
        }

        return ("Cancel-Under6h", 0.0m);
    }

    /// <summary>
    /// GAP #12 fix: BR §17.3 — Release inventory PHẢI dùng SELECT FOR UPDATE
    /// trong cùng transaction với status flip + refund. Nếu không, 2 reservation
    /// cùng cancel/timeout tại cùng (cafe, playDate, timeSlot) có thể race:
    /// cả 2 đọc HeldSeats=4, cả 2 trừ 4 → HeldSeats=-4 (Math.Max chặn âm nhưng logic sai).
    ///
    /// Caller PHẢI đang trong một transaction (Serializable hoặc RepeatableRead).
    /// </summary>
    private async Task ReleaseInventoriesAsync(Reservation reservation, DateTime now)
    {
        if (reservation.SeatInventoryId != null)
        {
            var seatInv = await _seatInventoryRepository.GetByIdForUpdateAsync(reservation.SeatInventoryId.Value);
            if (seatInv != null)
            {
                seatInv.HeldSeats = Math.Max(0, seatInv.HeldSeats - reservation.MaxPlayers);
                seatInv.UpdatedAt = now;
                await _seatInventoryRepository.UpdateAsync(seatInv);
            }
        }

        if (reservation.GameInventoryId != null)
        {
            var gameInv = await _gameInventoryRepository.GetByIdForUpdateAsync(reservation.GameInventoryId.Value);
            if (gameInv != null)
            {
                gameInv.HeldCopies = Math.Max(0, gameInv.HeldCopies - 1);
                gameInv.UpdatedAt = now;
                await _gameInventoryRepository.UpdateAsync(gameInv);
            }
        }
    }

    /// <summary>
    /// Cleanup tất cả LobbyMembers khi lobby chuyển sang terminal status
    /// (TimeoutFailed / HostCancelled / RejectedByCafe / ExpiredByCafe / Closed).
    /// Mục đích: tránh member bị "kẹt" trong lobby đã đóng và làm sai
    /// BR-USER-LIMIT-01/02 + BR-NEW-02/08 eligibility check.
    ///
    /// Lưu �: Query backend (GetActiveLobbiesByMemberAsync, GetOverlappingLobbiesAsync…)
    /// đều filter theo Lobby.Status nên không thật sự block user tạo/join lobby mới.
    /// Nhưng LobbyMember.IsActive=true trên lobby terminal gây:
    /// - FE tab "Lobby của tôi" hiển thị lobby đã đóng như còn active.
    /// - KarmaRatingService / MatchResultService.IsLobbyMember() trả về true sai.
    /// - Audit trail không rõ member đã "out" lúc nào.
    ///
    /// Phải gọi TRƯỚC khi SaveChangesAsync để EF track change.
    /// </summary>
    private static void MarkLobbyMembersInactive(Lobby lobby, DateTime now)
    {
        if (lobby.Members == null || lobby.Members.Count == 0)
        {
            return;
        }

        foreach (var member in lobby.Members.Where(m => m.IsActive))
        {
            member.IsActive = false;
            member.Status = LobbyMemberStatus.LobbyTerminated;
            member.LeftAt ??= now;
        }
    }

    // ===== BR §21A.8 + BR-REVENUE-01: Capture BVC về doanh thu quán =====

    /// <summary>
    /// POS đóng phiên (ActiveSession → Paid) → capture BVC đã giữ về doanh thu quán.
    ///
    /// BR §21A.8: Booking.status = completed + ghi ledger DEPOSIT_CAPTURE.
    /// BR-REVENUE-01: Tiền cọc 100% về quán, admin/platform không thu phí.
    /// BR §XVII.5: Outbox event SessionCompleted để worker publish.
    ///
    /// Idempotent theo (lobbyId): nếu reservation đã Completed → skip (log warning).
    /// No-op nếu lobbyId không có reservation (legacy ActiveSession không qua BVC flow).
    /// </summary>
    public async Task CompleteAndCaptureAsync(Guid lobbyId, Guid activeSessionId, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var reservation = await _reservationRepository.GetByLobbyIdAsync(lobbyId);
        if (reservation == null)
        {
            _logger.LogInformation(
                "CompleteAndCaptureAsync: lobby {LobbyId} không liên kết Reservation (legacy flow) → skip capture.",
                lobbyId);
            return;
        }

        if (reservation.Status == ReservationStatus.Completed)
        {
            _logger.LogInformation(
                "CompleteAndCaptureAsync: Reservation {ReservationId} đã Completed → idempotent skip.",
                reservation.Id);
            return;
        }

        if (reservation.Status != ReservationStatus.CheckedIn)
        {
            // GAP #20 fix: không capture nếu đã terminal khác (NoShow, Cancelled...).
            // NoShow scheduler đã ghi DEPOSIT_FORFEIT — nếu capture thêm → quán nhận 2 lần.
            // Cancelled thì BVC đã release về available — không capture.
            throw new ConflictException(
                ApiErrorMessages.Reservation.CompleteCaptureInvalidStatus(reservation.Id, reservation.Status));
        }

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await ExecuteCompleteAndCaptureTransactionAsync(reservation, activeSessionId, now, ct);

                // GAP-KARMA-AGGREGATE fix: Sau khi capture BVC thành công → tự động aggregate
                // cross-rating + no-show Karma (theo docs/api/booking.md §Aggregate Karma).
                // BookingService.CheckOutAsync đã deprecated; aggregate phải chạy ở đây.
                // Wrap try/catch riêng — aggregate fail KHÔNG block capture (đã commit).
                await TriggerKarmaAggregationAsync(lobbyId, activeSessionId, ct);

                // BR-KARMA-01: Track short-play nếu playedRatio < 0.5 và scheduled >= 4h.
                // Wrap try/catch riêng — track fail KHÔNG block capture (đã commit).
                await TriggerShortPlayTrackingAsync(reservation, activeSessionId, ct);

                // GAP #1 FIX: Sau khi capture BVC thành công → gọi SettlementService
                // để chuyển tiền cọc qua SePay vào tài khoản cafe manager.
                // Settlement fail KHÔNG block flow — sẽ được retry bởi SettlementRetryJob.
                await TriggerSettlementTransferAsync(reservation, activeSessionId, ct);

                return;
            }
            catch (DbUpdateException dbx) when (IsSerializationFailure(dbx) && attempt < maxRetries)
            {
                _logger.LogWarning(
                    "CompleteAndCaptureAsync serialization failure attempt {Attempt}/{Max}. Retrying. LobbyId={LobbyId}",
                    attempt, maxRetries, lobbyId);

                _db.ChangeTracker.Clear();
                reservation = await _reservationRepository.GetByLobbyIdAsync(lobbyId)
                    ?? throw new InternalServerErrorException(
                        ApiErrorMessages.System.ReservationByLobbyNotFoundAfterRetry(lobbyId, maxRetries));
            }
        }

        throw new InternalServerErrorException(
            ApiErrorMessages.System.BvcCaptureRetryExhausted(lobbyId, maxRetries));
    }

    /// <summary>
    /// GAP-KARMA-AGGREGATE: Sau khi capture BVC thành công → trigger
    /// <see cref="IBookingRatingService.AggregateBookingOutcomesAsync"/> để:
    /// 1. Cross-rating Karma delta (attitude/sportsmanship/punctuality).
    /// 2. No-show confirmed penalty + forfeit deposit.
    /// 3. Idempotent — chỉ xử lý rows chưa aggregate (IsAggregated = false).
    ///
    /// Lookup chain: LobbyId → Lobby.BookingId (= BookingDeposit.Id) → BookingDeposit.BookingId (= Booking.Id).
    /// Skip nếu booking không tồn tại (walk-in, chưa link với Booking entity).
    /// Aggregate fail KHÔNG throw — chỉ log warning. Capture BVC là critical, aggregate là nice-to-have.
    /// </summary>
    private async Task TriggerKarmaAggregationAsync(Guid lobbyId, Guid activeSessionId, CancellationToken ct)
    {
        try
        {
            var bookingDeposit = await _db.BookingDeposits
                .AsNoTracking()
                .FirstOrDefaultAsync(bd => bd.Id == _db.Lobbies
                    .Where(l => l.Id == lobbyId)
                    .Select(l => l.BookingId)
                    .FirstOrDefault(), ct);

            if (bookingDeposit == null)
            {
                _logger.LogInformation(
                    "TriggerKarmaAggregationAsync: LobbyId={LobbyId} không liên kết BookingDeposit → skip aggregate.",
                    lobbyId);
                return;
            }

            if (!bookingDeposit.BookingId.HasValue)
            {
                _logger.LogInformation(
                    "TriggerKarmaAggregationAsync: BookingDepositId={DepositId} không liên kết Booking → skip aggregate.",
                    bookingDeposit.Id);
                return;
            }

            var bookingId = bookingDeposit.BookingId.Value;
            var result = await _bookingRatingService.AggregateBookingOutcomesAsync(bookingId);

            _logger.LogInformation(
                "TriggerKarmaAggregationAsync: BookingId={BookingId} → processed {Ratings} ratings, " +
                "{NoShows} no-shows, {Forfeits} deposits forfeited, totalKarmaDelta={Delta}.",
                bookingId, result.RatingsProcessed, result.NoShowConfirmedMembers.Count,
                result.ForfeitedDepositIds.Count, result.TotalKarmaDelta);
        }
        catch (Exception ex)
        {
            // Non-critical: aggregate fail không block check-out flow.
            // Có thể re-run thủ công qua admin endpoint hoặc chờ scheduler (nếu có).
            _logger.LogWarning(ex,
                "TriggerKarmaAggregationAsync failed cho LobbyId={LobbyId}, ActiveSessionId={ActiveSessionId}. " +
                "Capture BVC vẫn thành công nhưng Karma aggregation bị skip — cần re-run thủ công.",
                lobbyId, activeSessionId);
        }
    }

    /// <summary>
    /// BR-KARMA-01: Track short-play violation cho Reservation flow.
    /// Được gọi sau khi <see cref="CompleteAndCaptureAsync"/> commit thành công.
    ///
    /// Nếu lịch chơi (Reservation) >= 4h và playedRatio &lt; 50% → ghi nhận vi phạm Karma.
    /// Wrap try/catch đơn lẻ — track fail KHÔNG block capture (đã commit).
    /// </summary>
    private async Task TriggerShortPlayTrackingAsync(
        Reservation reservation, Guid activeSessionId, CancellationToken ct)
    {
        try
        {
            // Lấy actual end time từ ActiveSession
            var session = await _db.ActiveSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == activeSessionId, ct);

            if (session == null)
            {
                _logger.LogInformation(
                    "TriggerShortPlayTrackingAsync: ActiveSession {ActiveSessionId} not found → skip.",
                    activeSessionId);
                return;
            }

            // Tính playedRatio
            var scheduledEnd = reservation.ExtendedEndTime ?? reservation.ScheduledEndTime;
            var scheduledStart = reservation.ScheduledStartTime;
            var scheduledMinutes = (int)(scheduledEnd - scheduledStart).TotalMinutes;

            // BR-KARMA-01: Chỉ track nếu scheduled >= 4h (240 phút)
            if (scheduledMinutes < 240)
            {
                _logger.LogInformation(
                    "TriggerShortPlayTrackingAsync: ReservationId={Id}, scheduledMinutes={Min} < 240 → skip.",
                    reservation.Id, scheduledMinutes);
                return;
            }

            // Tính playedMinutes từ session members
            var sessionMembers = await _db.ActiveSessionMembers
                .AsNoTracking()
                .Where(m => m.ActiveSessionId == activeSessionId)
                .ToListAsync(ct);

            if (sessionMembers.Count == 0)
            {
                _logger.LogInformation(
                    "TriggerShortPlayTrackingAsync: No session members for ActiveSession {ActiveSessionId} → skip.",
                    activeSessionId);
                return;
            }

            // Track short-play cho từng member (chỉ user có tài khoản, không track guest slot)
            foreach (var member in sessionMembers)
            {
                // BR-13: Guest slot không có UserId, bỏ qua
                if (!member.UserId.HasValue || member.TotalMinutesPlayed == 0) continue;

                var userId = member.UserId.Value;
                var playedMinutes = member.TotalMinutesPlayed;
                // Clamp to [0, 1] để consistent với các nơi khác (line 3203 trong CompleteAndCaptureAsync,
                // line 3549 trong EndAndSettleAsync). Tránh data corruption khi TotalMinutesPlayed
                // > scheduledMinutes (sessions merge/multi-group) → false positive short-play.
                var rawRatio = scheduledMinutes > 0 ? (decimal)playedMinutes / scheduledMinutes : 0m;
                var playedRatio = Math.Max(0m, Math.Min(1m, rawRatio));

                // BR-KARMA-01: Chỉ ghi nhận nếu playedRatio < 0.5
                if (playedRatio >= 0.5m) continue;

                // Kiểm tra đã có record chưa (idempotent)
                var existingRecord = await _db.KarmaShortPlayRecords
                    .AnyAsync(r => r.ReservationId == reservation.Id
                                   && r.UserId == userId, ct);

                if (existingRecord)
                {
                    _logger.LogInformation(
                        "TriggerShortPlayTrackingAsync: KarmaShortPlayRecord already exists for ReservationId={ResId}, UserId={UserId} → skip.",
                        reservation.Id, userId);
                    continue;
                }

                // Lấy tổng karma hiện tại của user từ UserProfile
                var userProfile = await _db.UserProfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == userId, ct);

                var totalKarma = userProfile?.KarmaPoints ?? 100;
                var karmaDelta = -5; // BR-KARMA-01: default -5 cho ratio < 0.5
                var karmaPointsAdded = karmaDelta; // Trong short-play, điểm bị trừ

                var record = new KarmaShortPlayRecord
                {
                    Id = Guid.NewGuid(),
                    ReservationId = reservation.Id,
                    UserId = userId,
                    PlayedMinutes = playedMinutes,
                    ScheduledMinutes = scheduledMinutes,
                    PlayedRatio = playedRatio,
                    KarmaDelta = karmaDelta,
                    KarmaPointsAdded = karmaPointsAdded,
                    TotalKarmaScore = totalKarma + karmaDelta,
                    Status = KarmaRecordStatus.Active,
                    CreatedAt = DateTime.UtcNow
                };

                _db.KarmaShortPlayRecords.Add(record);

                _logger.LogInformation(
                    "BR-KARMA-01: Created KarmaShortPlayRecord. ReservationId={ResId}, UserId={UserId}, " +
                    "PlayedMinutes={Played}/{Scheduled}, Ratio={Ratio:P1}, KarmaDelta={Delta}",
                    reservation.Id, userId, playedMinutes, scheduledMinutes, playedRatio, karmaDelta);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "TriggerShortPlayTrackingAsync failed for ReservationId={ResId}, ActiveSessionId={ActiveSessionId}. " +
                "Capture BVC vẫn thành công nhưng Karma short-play tracking bị skip.",
                reservation.Id, activeSessionId);
        }
    }

    /// <summary>
    /// GAP #1 FIX: Sau khi capture BVC thành công → gọi SettlementService
    /// để chuyển tiền cọc qua SePay vào tài khoản cafe manager.
    ///
    /// Tiền flow:
    /// 1. Khách đặt cọc → BVC giữ trong ví BoardVerse (heldBalance)
    /// 2. Khách check-in → phiên chơi bắt đầu
    /// 3. Khách checkout → BVC được capture (DepositCapture ledger entry)
    /// 4. Bước này: SettlementService gọi SePay transfer → tiền vào tài khoản cafe manager
    ///
    /// Settlement fail KHÔNG block flow — đã capture BVC thành công,
    /// tiền nằm trong ví BoardVerse. SettlementRetryJob sẽ retry.
    /// </summary>
    private async Task TriggerSettlementTransferAsync(
        Reservation reservation, Guid activeSessionId, CancellationToken ct)
    {
        try
        {
            // Chỉ settlement nếu có deposit thực sự
            if (reservation.DepositAmount <= 0)
            {
                _logger.LogInformation(
                    "TriggerSettlementTransferAsync: DepositAmount={Amount} ≤ 0, skip settlement. ReservationId={ReservationId}",
                    reservation.DepositAmount, reservation.Id);
                return;
            }

            _logger.LogInformation(
                "TriggerSettlementTransferAsync: Starting settlement transfer. ReservationId={ReservationId}, " +
                "ActiveSessionId={ActiveSessionId}, CafeId={CafeId}, DepositAmount={Amount}",
                reservation.Id, activeSessionId, reservation.CafeId, reservation.DepositAmount);

            var settlement = await _settlementService.ReleaseSessionDepositAsync(
                reservation.CafeId,
                reservation.Id, // sessionId = reservationId (Reservation IS the session concept here)
                activeSessionId,
                ct);

            _logger.LogInformation(
                "TriggerSettlementTransferAsync: Settlement completed. SettlementId={SettlementId}, " +
                "Status={Status}, NetTransferAmount={Amount}, SePayTransferId={TransferId}",
                settlement.Id, settlement.Status, settlement.NetTransferAmount, settlement.SePayTransferId);
        }
        catch (Exception ex)
        {
            // Settlement fail KHÔNG block flow — đã capture BVC thành công.
            // SettlementRetryJob sẽ retry các settlement failed.
            _logger.LogWarning(ex,
                "TriggerSettlementTransferAsync FAILED for ReservationId={ReservationId}, ActiveSessionId={ActiveSessionId}. " +
                "Settlement will be retried by SettlementRetryJob. BVC capture đã thành công.",
                reservation.Id, activeSessionId);
        }
    }

    // ===== GET LIST / DETAIL =====

    public async Task<ReservationDetailDto?> GetByIdAsync(Guid userId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        var reservation = await _reservationRepository.GetByIdAsync(reservationId, includeRelations: true);
        if (reservation == null)
        {
            return null;
        }

        // Validate access: user phải là host hoặc member
        var isHost = reservation.HostId == userId;
        var isMember = reservation.Lobby?.Members?.Any(m => m.UserId == userId && m.IsActive) ?? false;
        if (!isHost && !isMember)
        {
            return null;
        }

        var canCancel = reservation.Status == ReservationStatus.Holding
            || (reservation.Status == ReservationStatus.Confirmed
                && reservation.Lobby?.Status is LobbyStatus.Open
                    or LobbyStatus.Viable
                    or LobbyStatus.PendingCafeApproval);

        return new ReservationDetailDto
        {
            Id = reservation.Id,
            HostId = reservation.HostId,
            HostName = GetDisplayName(reservation.Host, reservation.Host?.Profile),
            CafeId = reservation.CafeId,
            CafeName = reservation.Cafe?.Name ?? string.Empty,
            CafeAddress = reservation.Cafe?.Address ?? string.Empty,
            GameId = reservation.GameId,
            GameName = reservation.Game?.Name ?? string.Empty,
            PlayDate = reservation.PlayDate,
            PreferredStartTime = reservation.PreferredStartTime ?? TimeOnly.MinValue,
            PreferredEndTime = reservation.PreferredEndTime ?? TimeOnly.MinValue,
            ScheduledStartTime = reservation.ScheduledStartTime,
            ScheduledEndTime = reservation.ScheduledEndTime,
            RecruitmentDeadline = reservation.RecruitmentDeadline,
            MinPlayers = reservation.MinPlayers,
            MaxPlayers = reservation.MaxPlayers,
            CurrentPlayers = reservation.CurrentPlayers,
            Status = reservation.Status.ToString(),
            DepositAmount = reservation.DepositAmount,
            RiskMultiplier = reservation.RiskMultiplier,
            RefundPolicyApplied = string.Empty,
            LobbyId = reservation.LobbyId,
            LobbyShareCode = reservation.Lobby?.ShareCode,
            LobbyStatus = reservation.Lobby?.Status.ToString() ?? string.Empty,
            CafeRejectionReason = reservation.Lobby?.CafeRejectionReason,
            ReservationCode = reservation.ReservationCode,
            CreatedAt = reservation.CreatedAt,
            UpdatedAt = reservation.UpdatedAt,
            IsHost = isHost,
            CanCancel = canCancel,
            CheckedInAt = reservation.CheckedInAt,
            ActualEndAt = reservation.ActualEndAt,
            PlayedRatio = reservation.PlayedRatio,
            EndReason = reservation.EndReason?.ToString(),
            WalkInWindowId = reservation.WalkInWindowId,
            CancelledBy = reservation.CancelledBy,
            CancelReason = reservation.CancelReason,
            TableNumber = reservation.TableNumber
        };
    }

    public async Task<ReservationListResponseDto> GetListAsync(Guid userId, ReservationListRequestDto request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _reservationRepository.GetListAsync(
            userId,
            request.HostedByMe,
            request.JoinedByMe,
            request.Statuses,
            request.PlayDate,
            request.CafeId,
            page,
            pageSize);

        var dtos = items.Select(r => new ReservationListItemDto
        {
            Id = r.Id,
            CafeId = r.CafeId,
            CafeName = r.Cafe?.Name ?? string.Empty,
            GameId = r.GameId,
            GameName = r.Game?.Name ?? string.Empty,
            PlayDate = r.PlayDate,
            PreferredStartTime = r.PreferredStartTime ?? TimeOnly.MinValue,
            PreferredEndTime = r.PreferredEndTime ?? TimeOnly.MinValue,
            CurrentPlayers = r.CurrentPlayers,
            MaxPlayers = r.MaxPlayers,
            Status = r.Status.ToString(),
            DepositAmount = r.DepositAmount,
            LobbyId = r.LobbyId,
            LobbyStatus = r.Lobby?.Status.ToString() ?? null,
            ReservationCode = r.ReservationCode,
            ScheduledStartTime = r.ScheduledStartTime,
            ScheduledEndTime = r.ScheduledEndTime,
            RecruitmentDeadline = r.RecruitmentDeadline,
            CreatedAt = r.CreatedAt,
            IsHost = r.HostId == userId,
            TableNumber = r.TableNumber
        }).ToList();

        return new ReservationListResponseDto
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// BR-NEW-11: Lấy chi tiết một reservation pending cafe approval cho manager.
    /// </summary>
    public async Task<LobbyPendingApprovalItemDto?> GetPendingCafeApprovalDetailAsync(
        Guid managerUserId,
        Guid reservationId, CancellationToken cancellationToken = default)
    {
        // Lấy danh sách cafe mà manager này quản lý
        var managedCafes = await _cafeRepository.GetCafesByManagerIdAsync(managerUserId);
        var cafeIds = managedCafes.Select(c => c.Id).ToHashSet();

        if (cafeIds.Count == 0)
        {
            return null;
        }

        var reservation = await _reservationRepository.GetPendingCafeApprovalByIdAsync(reservationId);

        if (reservation == null)
        {
            return null;
        }

        // Kiểm tra reservation có thuộc cafe của manager không
        if (!cafeIds.Contains(reservation.CafeId))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        return new LobbyPendingApprovalItemDto
        {
            ReservationId = reservation.Id,
            LobbyId = reservation.LobbyId ?? Guid.Empty,
            HostId = reservation.HostId,
            HostName = GetHostDisplayName(reservation.Host),
            CafeId = reservation.CafeId,
            CafeName = reservation.Cafe?.Name ?? string.Empty,
            GameId = reservation.GameId,
            GameName = reservation.Game?.Name ?? string.Empty,
            PlayDate = reservation.PlayDate,
            PreferredStartTime = reservation.PreferredStartTime ?? TimeOnly.MinValue,
            PreferredEndTime = reservation.PreferredEndTime ?? TimeOnly.MinValue,
            MinPlayers = reservation.MinPlayers,
            MaxPlayers = reservation.MaxPlayers,
            CurrentPlayers = reservation.CurrentPlayers,
            DepositAmount = reservation.DepositAmount,
            ScheduledStartTime = reservation.ScheduledStartTime,
            ScheduledEndTime = reservation.ScheduledEndTime,
            CafeApprovalDeadline = reservation.Lobby?.CafeApprovalDeadline ?? DateTime.MinValue,
            RemainingApprovalHours = reservation.Lobby?.CafeApprovalDeadline.HasValue == true
                ? (int)Math.Max(0, (reservation.Lobby.CafeApprovalDeadline.Value - now).TotalHours)
                : 0,
            CreatedAt = reservation.CreatedAt
        };
    }

    /// <summary>
    /// BR-NEW-11: Lấy danh sách lobby pending cafe approval cho manager.
    /// </summary>
    public async Task<LobbyPendingApprovalListResponseDto> GetPendingCafeApprovalAsync(
        Guid managerUserId,
        LobbyPendingApprovalRequestDto request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        // Lấy danh sách cafe mà manager này quản lý
        var managedCafes = await _cafeRepository.GetCafesByManagerIdAsync(managerUserId);
        var cafeIds = managedCafes.Select(c => c.Id).ToList();

        if (cafeIds.Count == 0)
        {
            return new LobbyPendingApprovalListResponseDto
            {
                Items = [],
                TotalCount = 0,
                Page = page,
                PageSize = pageSize
            };
        }

        var (items, totalCount) = await _reservationRepository.GetPendingCafeApprovalAsync(
            cafeIds,
            request.CafeId,
            request.PlayDate,
            page,
            pageSize);

        var now = DateTime.UtcNow;
        var dtos = items.Select(r => new LobbyPendingApprovalItemDto
        {
            ReservationId = r.Id,
            LobbyId = r.LobbyId ?? Guid.Empty,
            HostId = r.HostId,
            HostName = GetHostDisplayName(r.Host),
            CafeId = r.CafeId,
            CafeName = r.Cafe?.Name ?? string.Empty,
            GameId = r.GameId,
            GameName = r.Game?.Name ?? string.Empty,
            PlayDate = r.PlayDate,
            PreferredStartTime = r.PreferredStartTime ?? TimeOnly.MinValue,
            PreferredEndTime = r.PreferredEndTime ?? TimeOnly.MinValue,
            MinPlayers = r.MinPlayers,
            MaxPlayers = r.MaxPlayers,
            CurrentPlayers = r.CurrentPlayers,
            DepositAmount = r.DepositAmount,
            ScheduledStartTime = r.ScheduledStartTime,
            ScheduledEndTime = r.ScheduledEndTime,
            CafeApprovalDeadline = r.Lobby?.CafeApprovalDeadline ?? DateTime.MinValue,
            RemainingApprovalHours = r.Lobby?.CafeApprovalDeadline.HasValue == true
                ? (int)Math.Max(0, (r.Lobby.CafeApprovalDeadline.Value - now).TotalHours)
                : 0,
            CreatedAt = r.CreatedAt
        }).ToList();

        return new LobbyPendingApprovalListResponseDto
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Lấy display name cho host: ưu tiên FirstName + LastName, fallback Username.
    /// </summary>
    private static string GetHostDisplayName(User? host)
    {
        if (host == null) return string.Empty;
        var profile = host.Profile;
        if (profile != null)
        {
            var firstName = profile.FirstName?.Trim() ?? "";
            var lastName = profile.LastName?.Trim() ?? "";
            if (!string.IsNullOrEmpty(firstName) || !string.IsNullOrEmpty(lastName))
            {
                return $"{firstName} {lastName}".Trim();
            }
        }
        return host.Username ?? string.Empty;
    }

    // P2 Fix (2026-08-19): Shared helper for ambient transaction pattern.
    // If already in a transaction (ambient), reuse it. Otherwise, create new transaction.
    // Pattern: adopted from ExecuteCompleteAndCaptureTransactionAsync.
    private async Task<(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? OwnedTx, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction Tx)> BeginTransactionIfNeededAsync(
        CancellationToken ct = default)
    {
        var ambientTx = _db.Database.CurrentTransaction;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? ownedTx = null;
        if (ambientTx == null)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            return (ownedTx, ownedTx);
        }
        return (null, ambientTx);
    }

    private async Task ExecuteCompleteAndCaptureTransactionAsync(
        Reservation reservation,
        Guid activeSessionId,
        DateTime now,
        CancellationToken ct)
    {
        var (ownedTx, tx) = await BeginTransactionIfNeededAsync(ct);
        try
        {
            // 1. Lock seat inventory + game inventory (BR §17.3).
            // BR-NEW-15: Use SeatInventoryId/GameInventoryId FK when available.
            SeatInventory? seatInventory;
            if (reservation.SeatInventoryId.HasValue)
            {
                seatInventory = await _seatInventoryRepository.GetByIdForUpdateAsync(reservation.SeatInventoryId.Value);
            }
            else
            {
                seatInventory = await _seatInventoryRepository.GetForUpdateAsync(
                    reservation.CafeId, reservation.PlayDate,
                    TimeOnly.FromDateTime(reservation.ScheduledStartTime),
                    TimeOnly.FromDateTime(reservation.ScheduledEndTime));
            }
            if (seatInventory == null)
            {
                throw new ConflictException(
                    ApiErrorMessages.System.SeatInventoryMissingForReservation(
                        reservation.CafeId, reservation.PlayDate,
                        $"{reservation.ScheduledStartTime:HH:mm}-{reservation.ScheduledEndTime:HH:mm}"));
            }

            GameInventory? gameInventory;
            if (reservation.GameInventoryId.HasValue)
            {
                gameInventory = await _gameInventoryRepository.GetByIdForUpdateAsync(reservation.GameInventoryId.Value);
            }
            else
            {
                gameInventory = await _gameInventoryRepository.GetForUpdateAsync(
                    reservation.CafeId, reservation.GameId, reservation.PlayDate,
                    TimeOnly.FromDateTime(reservation.ScheduledStartTime),
                    TimeOnly.FromDateTime(reservation.ScheduledEndTime));
            }
            if (gameInventory == null)
            {
                throw new ConflictException(
                    ApiErrorMessages.System.GameInventoryMissingForReservation(
                        reservation.CafeId, reservation.PlayDate,
                        $"{reservation.ScheduledStartTime:HH:mm}-{reservation.ScheduledEndTime:HH:mm}"));
            }

            // 2. Validate inventory state — must be InUse (từ CheckInAsync move).
            if (seatInventory.InUseSeats < reservation.MaxPlayers)
            {
                throw new ConflictException(
                    ApiErrorMessages.Reservation.SeatInventoryStateInvalidOnCapture(seatInventory.InUseSeats, reservation.MaxPlayers));
            }

            if (gameInventory.InUseCopies < 1)
            {
                throw new ConflictException(ApiErrorMessages.Reservation.GameInventoryStateInvalidOnCapture);
            }

            // 3. Move seat: inUse → Available.
            seatInventory.InUseSeats -= reservation.MaxPlayers;
            seatInventory.UpdatedAt = now;
            await _seatInventoryRepository.UpdateAsync(seatInventory);

            // 4. Move game copy: inUse → Available.
            gameInventory.InUseCopies -= 1;
            gameInventory.UpdatedAt = now;
            await _gameInventoryRepository.UpdateAsync(gameInventory);

            // 5. Update reservation → Completed + compute lifecycle metadata.
            // FIX 2026-08-27: phải set ActualEndAt / CheckedInAt / PlayedRatio / EndReason
            // trên entity để audit/karma/refund report downstream đọc đúng.
            // Trước đây chỉ set Status + UpdatedAt → các field này = NULL trong DB → "bàn tự đóng".
            // Root cause: status flip trước khi load session, không tính được playedRatio.
            // FIX: load session + tính playedRatio trước, rồi set tất cả field cùng 1 UpdateAsync.
            var session = await _activeSessionRepository.GetByIdAsync(activeSessionId);
            var scheduledEnd = reservation.ExtendedEndTime ?? reservation.ScheduledEndTime;
            var scheduledStart = reservation.ScheduledStartTime;
            var scheduledMinutes = (int)(scheduledEnd - scheduledStart).TotalMinutes;

            // BR-END-02: ActualEndAt = session.EndedAt (single source of truth); fallback về now
            // khi session không load được (legacy / demo flow).
            var actualEndAt = session?.EndedAt ?? now;
            reservation.ActualEndAt = actualEndAt;

            // Safety net: nếu ExecuteCheckInTransactionAsync chưa set CheckedInAt (legacy/demo),
            // fallback về ScheduledStartTime để tránh divide-by-zero / negative playedRatio.
            // BR-END-02 yêu cầu CheckedInAt — đây là safety net cho upstream bug đã fix ở
            // ExecuteCheckInTransactionAsync step 9 (set reservation.CheckedInAt = now).
            var checkedInAt = reservation.CheckedInAt ?? scheduledStart;
            if (!reservation.CheckedInAt.HasValue)
            {
                _logger.LogWarning(
                    "CompleteAndCapture: Reservation {ReservationId} missing CheckedInAt — fallback to ScheduledStartTime={ScheduledStart}. Check ExecuteCheckInTransactionAsync step 9.",
                    reservation.Id, scheduledStart);
                reservation.CheckedInAt = checkedInAt;
            }

            // BR-END-02: playedRatio = (EndedAt - CheckedInAt) / (ScheduledEndTime - ScheduledStartTime).
            // Clamp [0, 1] để consistent với EndAndSettleAsync (line 3567) — tránh data corruption khi
            // EndedAt ở tương lai (sessions kéo dài) → ratio > 1 → semantic sai ở nhánh refund.
            decimal playedRatio = 0m;
            if (scheduledMinutes > 0)
            {
                var playedMinutes = (decimal)(actualEndAt - checkedInAt).TotalMinutes;
                var rawRatio = playedMinutes / scheduledMinutes;
                playedRatio = Math.Max(0m, Math.Min(1m, rawRatio));
            }
            reservation.PlayedRatio = playedRatio;

            // BR-END-02: EndReason mapping.
            // <0.9 → EarlyLeave (bao gồm <0.5 vẫn là EarlyLeave về mặt session lifecycle;
            //         forfeit policy được xử lý ở wallet layer captureAmount bên dưới).
            // ≥0.9 → OnTime (treated as on-time per BR-REFUND-06).
            reservation.EndReason = playedRatio >= 0.9m
                ? SessionEndReason.OnTime
                : SessionEndReason.EarlyLeave;

            reservation.Status = ReservationStatus.Completed;
            reservation.UpdatedAt = now;
            await _reservationRepository.UpdateAsync(reservation);

            // EC-09: playedRatio < 0.5 → tạo WalkInWindow cho phần thời gian còn lại.
            // Mirror ActiveSessionService.TryCreateWalkInWindowAsync semantics:
            // - WindowStart = session.StartedAt (tính từ lúc check-in)
            // - WindowEnd = reservation.ScheduledEndTime
            // - Seats = released (tính từ members Playing chưa Finished/SuspendedMutation, trừ GuestSlot).
            //
            // Best-effort: tạo trước commit, fail thì log warning + không rollback capture
            // (capture là best-effort side-effect, WalkInWindow cũng vậy).
            if (playedRatio < 0.5m)
            {
                try
                {
                    var releasedSeats = session?.Members?
                        .Count(m => !m.IsGuestSlot
                            && m.Status != IndividualSessionStatus.SuspendedMutation
                            && m.Status != IndividualSessionStatus.Finished) ?? 0;
                    if (releasedSeats > 0)
                    {
                        var window = await _walkInService.CreateWindowFromReservationAsync(
                            reservation, releasedSeats, actualEndAt);
                        if (window != null)
                        {
                            reservation.WalkInWindowId = window.Id;
                            _logger.LogInformation(
                                "CompleteAndCapture: Created WalkInWindow {WindowId} ({Seats} seats) for Reservation {ReservationId} (playedRatio={Ratio:P1})",
                                window.Id, releasedSeats, reservation.Id, playedRatio);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "CompleteAndCapture: Failed to create WalkInWindow for Reservation {ReservationId}. Capture continues.",
                        reservation.Id);
                }
            }

            // 6. Update lobby → Closed.
            var lobby = await _lobbyRepository.GetByIdAsync(reservation.LobbyId ?? Guid.Empty);
            if (lobby != null)
            {
                lobby.Status = LobbyStatus.Closed;
                lobby.ClosedAt = now;
                lobby.UpdatedAt = now;
                MarkLobbyMembersInactive(lobby, now);
                await _lobbyRepository.UpdateAsync(lobby);
            }

            // 7. Tính captureAmount / refundAmount từ playedRatio đã tính ở step 5.
            // FIX 2026-08-27: dùng lại playedRatio từ step 5 (đã set lên reservation.PlayedRatio)
            // thay vì tính lại — tránh divergence giữa reservation.PlayedRatio và captureAmount.
            // BR-REFUND-04: playedRatio < 0.5 → forfeit 100%
            // BR-REFUND-05: 0.5 ≤ playedRatio < 0.9 → forfeit 70%, refund 30%
            // BR-REFUND-06: playedRatio ≥ 0.9 → forfeit 100% (on-time)
            long captureAmount = reservation.DepositAmount;
            long refundAmount = 0;

            if (reservation.PlayedRatio.HasValue)
            {
                var ratio = reservation.PlayedRatio.Value;
                if (ratio < 0.5m)
                {
                    // BR-REFUND-04: Forfeit 100%
                    captureAmount = reservation.DepositAmount;
                }
                else if (ratio < 0.9m)
                {
                    // BR-REFUND-05: Forfeit 70%, refund 30%
                    // Match RefundCalculationService dùng MidpointRounding.AwayFromZero.
                    captureAmount = (long)Math.Round(reservation.DepositAmount * 0.7m, MidpointRounding.AwayFromZero);
                    refundAmount = reservation.DepositAmount - captureAmount;
                }
                else
                {
                    // BR-REFUND-06: Forfeit 100% (on-time)
                    captureAmount = reservation.DepositAmount;
                }

                _logger.LogInformation(
                    "CompleteAndCapture: ReservationId={ReservationId}, PlayedRatio={PlayedRatio:P1}, " +
                    "CaptureAmount={Capture}, RefundAmount={Refund}",
                    reservation.Id, ratio, captureAmount, refundAmount);
            }

            // 7a. Capture BVC (BR-REVENUE-01: phần quy định về quán).
            //    GAP-05 Fix: Idempotency key phải DETERMINISTIC — chỉ dựa vào reservationId
            //    (KHÔNG dùng reservation.UpdatedAt.Ticks vì UpdatedAt thay đổi mỗi lần save →
            //    webhook retry/scheduler race → 2 ledger entries → DOUBLE CAPTURE BVC).
            //    Key cố định `capture-{reservationId}` → ApplyBalanceMutationAsync sẽ check
            //    ledger table bằng GetByIdempotencyKeyForUpdateAsync → nếu tồn tại → return (no-op).
            //    Note: cùng key cho cả capture + refund vì chúng là 2 entries riêng biệt trên ledger.
            var captureIdempotencyKey = $"capture-{reservation.Id:N}";

            await _walletService.CaptureDepositAsync(
                reservation.HostId,
                captureAmount,
                lobby?.Id,
                reservation.Id,
                captureIdempotencyKey,
                ct);

            // 7b. Refund 30% cho BR-REFUND-05 (0.5 ≤ playedRatio < 0.9).
            //    Idempotency key riêng biệt (capture-refund-{reservationId}) để ledger entry
            //    không trùng với capture entry.
            if (refundAmount > 0)
            {
                var refundIdempotencyKey = $"capture-refund-{reservation.Id:N}";
                await _walletService.ReleaseDepositAsync(
                    reservation.HostId,
                    refundAmount,
                    lobby?.Id,
                    reservation.Id,
                    refundIdempotencyKey);

                _logger.LogInformation(
                    "CompleteAndCapture: Refund {Refund} BVC to host {HostId} for ReservationId={ReservationId}",
                    refundAmount, reservation.HostId, reservation.Id);
            }

            // 8. Outbox event SessionCompleted (BR-REQUIRED §17.5).
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                reservationId = reservation.Id,
                lobbyId = lobby?.Id,
                activeSessionId,
                hostId = reservation.HostId,
                cafeId = reservation.CafeId,
                capturedBvc = captureAmount,
                refundedBvc = refundAmount,
                completedAt = now
            });

            await _outboxRepository.AddAsync(new OutboxEvent
            {
                Id = Guid.NewGuid(),
                EventType = OutboxEventType.SessionCompleted,
                Payload = payload,
                IdempotencyKey = captureIdempotencyKey,
                ReservationId = reservation.Id,
                LobbyId = lobby?.Id,
                UserId = reservation.HostId,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(ct);

// CRITICAL FIX (2026-08-18): chỉ commit transaction nếu method này TỰ MỞ
            // (ownedTx != null). Nếu đã có ambient transaction (gọi từ
            // ActiveSessionService.PaySessionCoreAsync trong cùng DbContext scope),
            // outer transaction sẽ commit toàn bộ — không gọi CommitAsync ở đây
            // (gọi CommitAsync trên ambient transaction KHÔNG thuộc method này
            // sẽ gây "transaction is already completed" hoặc commit sai scope).
            if (ownedTx != null)
            {
                await ownedTx.CommitAsync(ct);
            }

            _logger.LogInformation(
                "Reservation completed + BVC captured. ReservationId={ReservationId}, CapturedBvc={Bvc}, ActiveSessionId={ActiveSessionId}",
                reservation.Id, reservation.DepositAmount, activeSessionId);
        }
        catch
        {
            // Tương tự commit: chỉ rollback transaction do method này sở hữu.
            // Ambient transaction sẽ tự rollback ở outer catch (ActiveSessionService.PaySessionCoreAsync
            // line 622: `await dbTx.RollbackAsync()`).
            if (ownedTx != null)
            {
                await ownedTx.RollbackAsync(ct);
            }
            throw;
        }
    }

    /// <summary>
    /// BR-REFUND-07: Admin override refund amount cho reservation đã completed.
    /// Cho phép refund một phần hoặc toàn bộ số BVC đã capture.
    /// Ghi AdminCredit ledger entry + PlayerActionHistory audit.
    /// </summary>
    public async Task<AdminOverrideRefundResultDto> AdminOverrideRefundAsync(
        Guid adminUserId,
        Guid reservationId,
        AdminOverrideRefundRequestDto request,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // 1. Idempotency check — nếu đã xử lý rồi thì trả kết quả cũ.
        var existingEntry = await _db.BvcLedgerEntries
            .FirstOrDefaultAsync(e =>
                e.IdempotencyKey == idempotencyKey &&
                e.Type == LedgerEntryType.AdminCredit &&
                e.RelatedReservationId == reservationId);

        if (existingEntry != null)
        {
            _logger.LogInformation(
                "AdminOverrideRefund: Idempotent replay for ReservationId={ReservationId}, IdempotencyKey={Key}",
                reservationId, idempotencyKey);

            return new AdminOverrideRefundResultDto
            {
                ReservationId = reservationId,
                UserId = existingEntry.UserId,
                OriginalDepositAmount = 0, // Not available from idempotent replay
                PreviouslyCapturedAmount = 0,
                PreviouslyRefundedAmount = 0,
                NewRefundAmount = request.RefundAmountBvc,
                ActualRefundAmount = existingEntry.Amount,
                AdminUserId = adminUserId,
                ProcessedAt = existingEntry.CreatedAt
            };
        }

        // 2. Validate reservation tồn tại và đã completed.
        var reservation = await _reservationRepository.GetByIdAsync(reservationId);
        if (reservation == null)
        {
            throw new NotFoundException(ApiErrorMessages.Reservation.NotFound(reservationId));
        }

        if (reservation.Status != ReservationStatus.Completed)
        {
            throw new ConflictException(
                ApiErrorMessages.System.OverrideRefundInvalidStatus(reservation.Id.ToString(), reservation.Status.ToString()));
        }

        // 3. Validate refund amount không vượt quá deposit.
        if (request.RefundAmountBvc > reservation.DepositAmount)
        {
            throw new BadRequestException(
                ApiErrorMessages.System.RefundAmountExceedsDeposit(
                    request.RefundAmountBvc, reservation.DepositAmount));
        }

        // 4. Thực hiện refund (AdminCredit).
        var now = DateTime.UtcNow;
        var result = await _walletService.AdminAdjustBalanceAsync(
            targetUserId: reservation.HostId,
            amountBvc: request.RefundAmountBvc,
            isCredit: true,
            adminUserId: adminUserId,
            reason: $"[BR-REFUND-07] Admin override refund for Reservation {reservationId}: {request.Reason}",
            idempotencyKey: idempotencyKey);

        // 5. Audit log: AdminAdjustBalanceAsync đã ghi PlayerActionHistory (BR-RISK-05).

        _logger.LogInformation(
            "AdminOverrideRefund: ReservationId={ReservationId}, AdminUserId={AdminId}, " +
            "RefundAmount={RefundBvc} BVC, IdempotencyKey={Key}",
            reservationId, adminUserId, request.RefundAmountBvc, idempotencyKey);

        return new AdminOverrideRefundResultDto
        {
            ReservationId = reservationId,
            UserId = reservation.HostId,
            OriginalDepositAmount = reservation.DepositAmount,
            PreviouslyCapturedAmount = reservation.DepositAmount, // Simplified — actual may differ
            PreviouslyRefundedAmount = 0,
            NewRefundAmount = request.RefundAmountBvc,
            ActualRefundAmount = request.RefundAmountBvc,
            AdminUserId = adminUserId,
            ProcessedAt = now
        };
    }

    private static string GetDisplayName(User? user, UserProfile? profile)
    {
        if (user == null) return string.Empty;

        var displayName = profile?.FirstName ?? user.Username;
        if (!string.IsNullOrEmpty(profile?.LastName))
        {
            displayName = string.IsNullOrEmpty(displayName)
                ? profile.LastName
                : $"{displayName} {profile.LastName}";
        }

        return string.IsNullOrWhiteSpace(displayName) ? user.Username : displayName;
    }

    /// <summary>
    /// Tính scheduledStartTime + scheduledEndTime từ <paramref name="playDate"/> và giờ user chọn.
    /// BR-RES-07/08/09: endTime bắt buộc (không open-ended).
    /// Nếu endTime nhỏ hơn startTime thì endTime thuộc ngày kế tiếp.
    /// Lưu vào DB (<see cref="Reservation.ScheduledStartTime"/>, <see cref="Reservation.ScheduledEndTime"/>)
    /// để WalkInWindowCleanupJob (§4.4), playedRatio (§4.3), extension flow (Phase 3)
    /// không phải derive runtime từ TimeSlot enum.
    /// </summary>
    internal static (DateTime ScheduledStartTime, DateTime ScheduledEndTime) BuildScheduledStartEnd(
        DateOnly playDate,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        var startDateTime = playDate.ToDateTime(startTime);

        DateTime endDateTime;
        // Overnight: endTime < startTime (e.g., 21:00 → 00:00 ngày hôm sau)
        if (endTime < startTime)
        {
            endDateTime = playDate.AddDays(1).ToDateTime(endTime);
        }
        else
        {
            endDateTime = playDate.ToDateTime(endTime);
            // Sanity check: cùng ngày
            if (endDateTime.Date != startDateTime.Date)
            {
                throw new InvalidOperationException(
                    ApiErrorMessages.Reservation.ReservationEndTimeDifferentDay);
            }
        }

        return (startDateTime, endDateTime);
    }

    /// <summary>
    /// BR-RES-07/08/09: validate rằng reservation có đầy đủ startTime + endTime,
    /// cùng ngày hoặc ngày kế tiếp nếu qua đêm, không dùng TimeSlot enum nữa.
    /// BR-NEW-15 (2026-08-18): derive overnight từ endTime &lt; startTime.
    ///
    /// G11 fix: scheduledStartTime phải trong tương lai (so với now).
    /// G7 fix: thời lượng phiên không được vượt quá <paramref name="maxHours"/>.
    /// G8 fix: thời lượng phiên phải tối thiểu <paramref name="minMinutes"/>.
    ///
    /// NOTE: Method này chỉ validate TIME RANGE của 1 reservation.
    /// Không check overlap với reservation khác — overlap check nằm ở
    /// <see cref="EligibilityValidator.ValidateHostCanCreateAsync"/> (BR-USER-LIMIT-02).
    ///
    /// Throw BadRequestException với message tiếng Việt từ <see cref="ApiErrorMessages.Reservation"/>.
    /// </summary>
    /// <param name="scheduledStartTime">Thời gian bắt đầu dự kiến.</param>
    /// <param name="scheduledEndTime">Thời gian kết thúc dự kiến.</param>
    /// <param name="now">Thời điểm hiện tại (UTC) để so sánh.</param>
    /// <param name="maxHours">Thời lượng tối đa cho phép (mặc định 12 giờ).</param>
    /// <param name="minMinutes">Thời lượng tối thiểu cho phép (mặc định 30 phút).</param>
    internal static void ValidateReservationTimeWindow(
        DateTime scheduledStartTime,
        DateTime scheduledEndTime,
        DateTime now,
        int maxHours = 12,
        int minMinutes = 30)
    {
        if (scheduledStartTime == default || scheduledEndTime == default)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.ReservationRequiresStartAndEnd);
        }

        // G5 fix: preferredEndTime == default (TimeOnly.MinValue → DateTime.MinValue) cần message riêng.
        // Check riêng để message rõ ràng hơn "thời gian bắt đầu và kết thúc bắt buộc".
        if (scheduledEndTime == default)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.PreferredEndTimeRequired);
        }

        if (scheduledEndTime <= scheduledStartTime)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.PreferredTimesMustDiffer);
        }

        // G11 fix: scheduledStartTime phải trong tương lai.
        if (scheduledStartTime <= now)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.StartTimeInPast);
        }

        var duration = scheduledEndTime - scheduledStartTime;

        // G7 fix: max duration.
        if (duration.TotalHours > maxHours)
        {
            throw new BadRequestException(
                ApiErrorMessages.Reservation.DurationTooLong(maxHours));
        }

        // G8 fix: min duration.
        if (duration.TotalMinutes < minMinutes)
        {
            throw new BadRequestException(
                ApiErrorMessages.Reservation.DurationTooShort(minMinutes));
        }

        // Overnight khi giờ kết thúc nhỏ hơn giờ bắt đầu.
        var startTimeOnly = TimeOnly.FromDateTime(scheduledStartTime);
        var endTimeOnly = TimeOnly.FromDateTime(scheduledEndTime);
        var isOvernight = endTimeOnly < startTimeOnly;

        if (isOvernight)
        {
            if (scheduledEndTime.Date != scheduledStartTime.Date.AddDays(1))
            {
                throw new BadRequestException(
                    ApiErrorMessages.System.LateNightMustEndNextDay(
                        scheduledStartTime.ToString("HH:mm"),
                        scheduledEndTime.ToString("HH:mm")));
            }
        }
        else
        {
            // Các slot khác: cùng ngày
            if (scheduledEndTime.Date != scheduledStartTime.Date)
            {
                throw new BadRequestException(ApiErrorMessages.Reservation.ReservationEndTimeDifferentDay);
            }
        }
    }

    /// <summary>
    /// BR-END-01..05 (docs/time-slot-fixed-end-design (1).md §3.4 + §21A.8):
    /// POS end session + settle deposit + have thể tạo WalkInWindow + ghi Karma violation.
    /// </summary>
    public async Task<EndReservationResponseDto> EndAndSettleAsync(
        Guid staffUserId,
        EndReservationRequestDto request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var actualEnd = request.ActualEndAt ?? now;

        var reservation = await _reservationRepository.GetByIdAsync(request.ReservationId, includeRelations: true);
        if (reservation == null)
        {
            throw new NotFoundException(ApiErrorMessages.Reservation.NotFound(request.ReservationId));
        }

        // Idempotent: nếu đã Completed/EarlyCheckout → trả kết quả rỗng.
        if (reservation.Status is ReservationStatus.Completed or ReservationStatus.EarlyCheckout)
        {
            _logger.LogInformation(
                "EndAndSettleAsync: Reservation {ReservationId} đã terminal (status={Status}) → idempotent skip.",
                reservation.Id, reservation.Status);
            return BuildEndResponse(reservation, actualEnd);
        }

        if (reservation.Status != ReservationStatus.CheckedIn
            && reservation.Status != ReservationStatus.InProgress)
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.CompleteCaptureInvalidStatus(reservation.Id, reservation.Status));
        }

        if (!reservation.CheckedInAt.HasValue)
        {
            throw new ConflictException(
                ApiErrorMessages.System.ReservationMissingCheckedInAt(reservation.Id));
        }

        // Validate BR-RES-08 (sanity check).
        ValidateReservationTimeWindow(
            reservation.ScheduledStartTime,
            reservation.ScheduledEndTime,
            now);

        // Tính playedRatio.
        var playedMinutes = (actualEnd - reservation.CheckedInAt.Value).TotalMinutes;
        var scheduledMinutes = (reservation.ScheduledEndTime - reservation.ScheduledStartTime).TotalMinutes;
        var rawRatio = scheduledMinutes > 0 ? playedMinutes / scheduledMinutes : 0d;
        var playedRatio = (decimal)Math.Max(0d, Math.Min(1d, rawRatio));

        // BR-END-03/04: refund policy.
        var calc = _refundCalc.Calculate(reservation.DepositAmount, playedRatio);
        var deltaEnd = actualEnd - reservation.CheckedInAt.Value;

        // Reservation update.
        reservation.ActualEndAt = actualEnd;
        reservation.PlayedRatio = playedRatio;
        reservation.Status = calc.RefundAmount > 0
            ? ReservationStatus.EarlyCheckout
            : ReservationStatus.Completed;
        reservation.EndReason = playedRatio >= 0.90m
            ? SessionEndReason.OnTime
            : SessionEndReason.EarlyLeave;

        // EC-09: nếu playedRatio < 50% → tạo WalkInWindow cho phần thời gian còn lại.
        if (playedRatio < 0.50m && !request.SkipWalkInWindow)
        {
            var window = await _walkInService.CreateWindowFromReservationAsync(
                reservation,
                reservation.MaxPlayers,
                actualEnd);
            if (window != null)
            {
                reservation.WalkInWindowId = window.Id;
            }
        }

        await _reservationRepository.UpdateAsync(reservation);

        // BR-KARMA-01 §4.3: ghi short-play violation nếu playedRatio < 50%.
        bool karmaRecorded = false;
        if (playedRatio < 0.5m)
        {
            try
            {
                karmaRecorded = await _karmaService.RecordShortPlayAsync(
                    reservation.Id,
                    reservation.HostId,
                    (int)playedMinutes,
                    (int)scheduledMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "EndAndSettleAsync: Failed to record short-play karma for reservation {ReservationId}",
                    reservation.Id);
            }
        }

        _logger.LogInformation(
            "EndAndSettleAsync: Reservation {ReservationId} ended. playedRatio={Ratio:F4}, refund={Refund}, forfeit={Forfeit}, endReason={Reason}",
            reservation.Id, playedRatio, calc.RefundAmount, calc.ForfeitAmount, reservation.EndReason);

        return BuildEndResponse(reservation, actualEnd, calc.RefundAmount, calc.ForfeitAmount, calc.Reason, karmaRecorded);
    }

    private static EndReservationResponseDto BuildEndResponse(
        Reservation reservation,
        DateTime actualEnd,
        long? refundBvc = null,
        long? forfeitBvc = null,
        RefundReason? reason = null,
        bool karmaRecorded = false)
    {
        return new EndReservationResponseDto
        {
            ReservationId = reservation.Id,
            EndReason = reservation.EndReason ?? SessionEndReason.OnTime,
            PlayedRatio = reservation.PlayedRatio ?? 0m,
            OriginalDeposit = reservation.DepositAmount,
            RefundBvc = refundBvc ?? 0,
            ForfeitBvc = forfeitBvc ?? reservation.DepositAmount,
            RefundReason = reason ?? RefundReason.OnTime,
            CheckedInAt = reservation.CheckedInAt ?? DateTime.MinValue,
            ActualEndAt = actualEnd,
            ScheduledStartTime = reservation.ScheduledStartTime,
            ScheduledEndTime = reservation.ScheduledEndTime,
            WalkInWindowId = reservation.WalkInWindowId,
            KarmaRecorded = karmaRecorded
        };
    }

    /// <summary>
    /// Lấy danh sách reservation của 1 cafe cho Manager.
    /// </summary>
    public async Task<CafeReservationsResponseDto> GetCafeReservationsAsync(
        Guid cafeManagerUserId,
        Guid cafeId,
        CafeReservationsRequestDto request, CancellationToken cancellationToken = default)
    {
        // Validate user có quyền xem cafe này (Manager hoặc CafeStaff)
        var hasAccess = await _cafeRepository.IsManagerOrStaffAsync(cafeId, cafeManagerUserId);
        if (!hasAccess)
        {
            throw new ForbiddenException(ApiErrorMessages.Cafe.ManagerForbidden(cafeId));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _reservationRepository.GetByCafeAsync(
            cafeId,
            request.Statuses,
            request.PlayDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            page,
            pageSize);

        var dtos = items.Select(r => new ReservationListItemDto
        {
            Id = r.Id,
            CafeId = r.CafeId,
            CafeName = r.Cafe?.Name ?? string.Empty,
            GameId = r.GameId,
            GameName = r.Game?.Name ?? string.Empty,
            PlayDate = r.PlayDate,
            PreferredStartTime = r.PreferredStartTime ?? TimeOnly.MinValue,
            PreferredEndTime = r.PreferredEndTime ?? TimeOnly.MinValue,
            CurrentPlayers = r.CurrentPlayers,
            MaxPlayers = r.MaxPlayers,
            Status = r.Status.ToString(),
            DepositAmount = r.DepositAmount,
            LobbyId = r.LobbyId,
            LobbyStatus = r.Lobby?.Status.ToString() ?? null,
            ReservationCode = r.ReservationCode,
            ScheduledStartTime = r.ScheduledStartTime,
            ScheduledEndTime = r.ScheduledEndTime,
            RecruitmentDeadline = r.RecruitmentDeadline,
            CreatedAt = r.CreatedAt,
            IsHost = r.HostId == cafeManagerUserId,
            TableNumber = r.TableNumber
        }).ToList();

        return new CafeReservationsResponseDto
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Tìm kiếm lịch hẹn theo tên game hoặc ngày tháng.
    /// </summary>
    public async Task<ReservationSearchResponseDto> SearchAsync(
        Guid userId,
        ReservationSearchRequestDto request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _reservationRepository.SearchAsync(
            userId,
            request.GameName,
            request.FromDate,
            request.ToDate,
            request.Statuses,
            request.CafeId,
            request.HostedByMe,
            request.JoinedByMe,
            page,
            pageSize);

        var dtos = items.Select(r => new ReservationListItemDto
        {
            Id = r.Id,
            CafeId = r.CafeId,
            CafeName = r.Cafe?.Name ?? string.Empty,
            GameId = r.GameId,
            GameName = r.Game?.Name ?? string.Empty,
            PlayDate = r.PlayDate,
            PreferredStartTime = r.PreferredStartTime ?? TimeOnly.MinValue,
            PreferredEndTime = r.PreferredEndTime ?? TimeOnly.MinValue,
            CurrentPlayers = r.CurrentPlayers,
            MaxPlayers = r.MaxPlayers,
            Status = r.Status.ToString(),
            DepositAmount = r.DepositAmount,
            LobbyId = r.LobbyId,
            LobbyStatus = r.Lobby?.Status.ToString() ?? null,
            ReservationCode = r.ReservationCode,
            ScheduledStartTime = r.ScheduledStartTime,
            ScheduledEndTime = r.ScheduledEndTime,
            RecruitmentDeadline = r.RecruitmentDeadline,
            CreatedAt = r.CreatedAt,
            IsHost = r.HostId == userId,
            TableNumber = r.TableNumber
        }).ToList();

        return new ReservationSearchResponseDto
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }
}

/// <summary>
/// Generate share code 8 ký tự alphanumeric uppercase cho lobby.
/// </summary>
internal static class ShareCodeGenerator
{
    private static readonly char[] Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    public static string Generate()
    {
        var bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        return new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
    }
}
