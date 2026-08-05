using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.DTOs.Wallet;
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
        TimeProvider timeProvider)
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
    }

    // ===== 21A.2 QUOTE =====

    public async Task<ReservationQuoteDto> CreateQuoteAsync(Guid hostId, ReservationQuoteRequestDto request)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        ValidatePlayDate(request.PlayDate, now);
        ValidateTimeSlotWindow(request);

        // Validate cafe + game tồn tại, dùng cho BR-RESERVATION-02.
        await ValidateCafeAndGameAsync(request);

        // Validate preferredStartTime nằm trong timeSlot window.
        if (!CafeSchedule.IsPreferredStartTimeValid(request.TimeSlot, request.PreferredStartTime))
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.PreferredStartTimeOutOfRange);
        }

        // Load cafe config (BR-NEW-12).
        var cafeConfig = await _cafeConfigRepository.GetOrCreateDefaultAsync(request.CafeId);

        // Ensure SeatInventory tồn tại để tính số BVC cọc (BR §21A.2).
        // Dùng TotalSeats từ CafeConfig thay vì Cafe.TotalSeats vì config có thể override.
        await _seatInventoryRepository.EnsureRowAsync(
            request.CafeId,
            request.PlayDate,
            request.TimeSlot,
            cafeConfig.Capacity);

        // Ensure GameInventory tồn tại (BR-RESERVATION-02).
        // TotalCopies lấy từ CafeGameInventory.BoxQuantity (số box khả dụng của cafe cho game này).
        // Nếu cafe chưa add game vào inventory → dùng fallback 1 copy để quote vẫn chạy được.
        var cafeInventory = await _cafeInventoryRepository.GetByCafeAndGameTemplateAsync(
            request.CafeId, request.GameId);
        var totalCopies = cafeInventory?.BoxQuantity ?? 1;
        await _gameInventoryRepository.EnsureRowAsync(
            request.CafeId,
            request.GameId,
            request.PlayDate,
            request.TimeSlot,
            totalCopies);

        // Load wallet để lấy riskMultiplier.
        var wallet = await GetOrCreateWalletEntityAsync(hostId, now);

        // Tính quote (có áp dụng CafeScheduleOverride qua resolver).
        var quote = await _depositCalculator.CalculateWithScheduleAsync(
            request,
            cafeConfig,
            wallet.RiskMultiplier,
            wallet.IsCoolingOff,
            request.IsPrivate,
            now,
            _scheduleResolver,
            request.CafeId);

        // BR-LOBBY-01a/b: buffer check.
        var (isAllowed, _) = DepositCalculator.EvaluateBuffer(quote.BufferMinutes);
        if (!isAllowed)
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

        _eligibilityValidator.ValidateHostCanCreate(eligibilityContext);

        var resolvedSchedule = await _scheduleResolver.ResolveAsync(
            request.CafeId, request.PlayDate, request.TimeSlot);
        var scheduledTime = request.PlayDate.ToDateTime(resolvedSchedule.StartTime);
        var recruitmentDeadline = scheduledTime.AddMinutes(-20); // default leadTimeMinutes

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
            TimeSlot = request.TimeSlot,
            PreferredStartTime = request.PreferredStartTime,
            ScheduledTime = scheduledTime,
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

    public async Task<ReservationConfirmResponseDto> ConfirmAsync(Guid hostId, ReservationConfirmRequestDto request)
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

            var existingLobbyId = existing.LobbyId
                ?? throw new InternalServerErrorException(
                    $"Reservation idempotent '{existing.Id:N}' thiếu lobby.");

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
        ValidateTimeSlotWindowRaw(request.MinPlayers, request.MaxPlayers);

        if (!CafeSchedule.IsPreferredStartTimeValid(request.TimeSlot, request.PreferredStartTime))
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.PreferredStartTimeOutOfRange);
        }

        var quoteRequest = new ReservationQuoteRequestDto
        {
            CafeId = request.CafeId,
            GameId = request.GameId,
            PlayDate = request.PlayDate,
            TimeSlot = request.TimeSlot,
            PreferredStartTime = request.PreferredStartTime,
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
        await _seatInventoryRepository.EnsureRowAsync(
            request.CafeId,
            request.PlayDate,
            request.TimeSlot,
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
            request.TimeSlot,
            totalCopies);

        // 4. Tính lại quote (server authoritative — BR §XVII.2) — có áp dụng CafeScheduleOverride.
        var quote = await _depositCalculator.CalculateWithScheduleAsync(
            quoteRequest,
            cafeConfig,
            wallet.RiskMultiplier,
            wallet.IsCoolingOff,
            request.IsPrivate,
            now,
            _scheduleResolver,
            request.CafeId);

        // 5. BR-LOBBY-01a/b: buffer check.
        var (isAllowed, _) = DepositCalculator.EvaluateBuffer(quote.BufferMinutes);
        if (!isAllowed)
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
        _eligibilityValidator.ValidateHostCanCreate(eligibilityContext);

        // 8. BR-RESERVATION-01: đủ ghế? BR-RESERVATION-02: đủ game copy?
        if (wallet.AvailableBalance < quote.FinalDeposit)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.InsufficientAvailableBalance(
                wallet.AvailableBalance, quote.FinalDeposit));
        }

        var resolvedSchedule = await _scheduleResolver.ResolveAsync(
            request.CafeId, quoteRequest.PlayDate, quoteRequest.TimeSlot);
        var scheduledTime = quoteRequest.PlayDate.ToDateTime(resolvedSchedule.StartTime);
        var recruitmentDeadline = scheduledTime.AddMinutes(-20); // BR-LOBBY-01 default leadTimeMinutes = 20

        // ===== Atomic transaction (BR-REQUIRED §17.4) =====
        // Dùng Serializable Isolation để chống race condition overbooking (BR §17.3).
        // Postgres sẽ throw DbUpdateException với SqlState=40001 nếu conflict → retry tối đa 3 lần.
        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await ExecuteConfirmTransactionAsync(
                    hostId, request, quoteRequest, cafeConfig, wallet, quote, scheduledTime, recruitmentDeadline, now);
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
                quote = await _depositCalculator.CalculateWithScheduleAsync(
                    quoteRequest,
                    cafeConfig,
                    wallet.RiskMultiplier,
                    wallet.IsCoolingOff,
                    request.IsPrivate,
                    now,
                    _scheduleResolver,
                    request.CafeId);
            }
        }

        // Không bao giờ đến đây, nhưng compiler cần.
        throw new InternalServerErrorException("Không thể hoàn tất reservation sau nhiều lần thử.");
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
        DateTime scheduledTime,
        DateTime recruitmentDeadline,
        DateTime now)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        try
        {
            // 9. Lock seat inventory + game inventory (BR §17.3 — SELECT FOR UPDATE).
            var seatInventory = await _seatInventoryRepository.GetForUpdateAsync(
                request.CafeId, request.PlayDate, request.TimeSlot);
            if (seatInventory == null)
            {
                throw new BadRequestException(ApiErrorMessages.Reservation.SeatInventoryNotConfigured);
            }

            if (seatInventory.AvailableSeats < quote.MaxPlayersApplied)
            {
                throw new ConflictException(
                    ApiErrorMessages.Reservation.SeatsNotAvailable(seatInventory.AvailableSeats, quote.MaxPlayersApplied));
            }

            var gameInventory = await _gameInventoryRepository.GetForUpdateAsync(
                request.CafeId, request.GameId, request.PlayDate, request.TimeSlot);
            if (gameInventory == null || gameInventory.AvailableCopies < 1)
            {
                throw new ConflictException(
                    gameInventory == null
                        ? ApiErrorMessages.Reservation.GameInventoryNotFound
                        : ApiErrorMessages.Reservation.GameCopyNotAvailable(gameInventory.AvailableCopies));
            }

            // 10. Hold BVC (ledger + wallet mutation).
            await _walletService.HoldDepositAsync(
                hostId,
                quote.FinalDeposit,
                null,
                null,
                request.IdempotencyKey);

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
                TimeSlot = request.TimeSlot,
                PreferredStartTime = request.PreferredStartTime,
                RecruitmentDeadline = recruitmentDeadline,
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
                CreatedAt = now,
                UpdatedAt = now
            };
            await _reservationRepository.AddAsync(reservation);

            // 13. Insert Lobby (BR-REQUIRED §17.4 — bước 7).
            var initialLobbyStatus = DetermineInitialLobbyStatus(reservation, cafeConfig, now);
            var lobby = new Lobby
            {
                Id = Guid.NewGuid(),
                HostUserId = hostId,
                GameTemplateId = request.GameId,
                CafeId = request.CafeId,
                ReservationId = reservation.Id,
                PlayDate = request.PlayDate,
                TimeSlot = request.TimeSlot,
                PreferredStartTime = request.PreferredStartTime,
                RecruitmentDeadline = recruitmentDeadline,
                ScheduledStartTime = scheduledTime,
                MaxMembers = quote.MaxPlayersApplied,
                MinPlayers = request.MinPlayers,
                MinDeposit = quote.FinalDeposit,
                DepositSnapshot = depositSnapshot,
                Status = initialLobbyStatus,
                ShareCode = ShareCodeGenerator.Generate(),
                IsPrivate = false,
                CancellationLeadTimeMinutes = cafeConfig.RecruitmentDeadlineBufferMinutes,
                CreatedAt = now,
                UpdatedAt = now
            };

            if (initialLobbyStatus == LobbyStatus.PendingCafeApproval)
            {
                lobby.CafeApprovalDeadline = now.AddHours(cafeConfig.ApprovalTimeoutHours);
            }

            await _lobbyRepository.AddAsync(lobby);

            // 14. Bind FK reservation ↔ lobby.
            reservation.LobbyId = lobby.Id;
            reservation.UpdatedAt = now;

            // 15. Update inventory counters.
            seatInventory.HeldSeats += quote.MaxPlayersApplied;
            seatInventory.UpdatedAt = now;
            await _seatInventoryRepository.UpdateAsync(seatInventory);

            gameInventory.HeldCopies += 1;
            gameInventory.UpdatedAt = now;
            await _gameInventoryRepository.UpdateAsync(gameInventory);

            // 16. Insert Host as first lobby member (BR-DEPOSIT-01).
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

            // 17. BR-REQUIRED §17.5: Transactional Outbox — 3 event trong cùng transaction.
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

            await _db.SaveChangesAsync();
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
        catch
        {
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
            timeSlot = (int)lobby.TimeSlot,
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
    public async Task<CancelReservationResponseDto> CancelAsync(Guid hostId, CancelReservationRequestDto request)
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
            $"Không thể hoàn tất cancel reservation '{request.ReservationId}' sau {MaxRetries} lần thử.");
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

            if (reservation.Status != ReservationStatus.Holding)
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
            var members = await _lobbyRepository.GetMembersAsync(lobby.Id);
            var minutesSinceCreated = (now - reservation.CreatedAt).TotalMinutes;
            var hasMembers = members.Count > 1;

            var refundPolicy = ComputeRefundPolicy(
                reservation.ScheduledTime,
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
        CafeApprovalRequestDto request)
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
            $"Không thể hoàn tất cafe approval reservation '{request.ReservationId}' sau {MaxRetries} lần thử.");
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
                ?? throw new NotFoundException($"Không tìm thấy cafe '{reservation.CafeId}'.");

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

        try
        {
            foreach (var reservation in reservations)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessSingleDeadlineAsync(reservation, cutoff);
                processed++;
            }

            await batchTx.CommitAsync(ct);
        }
        catch
        {
            await batchTx.RollbackAsync(ct);
            throw;
        }

        return processed;
    }

    /// <summary>
    /// GAP #11 fix: Wrap từng deadline processing trong 1 Serializable transaction.
    /// Trước: update status + refund + release inventory rời rạc → race với member join / cancel.
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
                await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                try
                {
                    if (reservation.CurrentPlayers >= reservation.MinPlayers)
                    {
                        // Đạt minPlayers → viable/full → confirmed.
                        reservation.Status = ReservationStatus.Confirmed;
                        lobby.Status = lobby.Status == LobbyStatus.PendingCafeApproval
                            ? LobbyStatus.PendingCafeApproval
                            : (reservation.CurrentPlayers >= reservation.MaxPlayers ? LobbyStatus.Full : LobbyStatus.Viable);
                        lobby.UpdatedAt = now;

                        await _reservationRepository.UpdateAsync(reservation);
                        await _lobbyRepository.UpdateAsync(lobby);
                        await _reservationRepository.SaveChangesAsync();
                        await tx.CommitAsync();

                        _logger.LogInformation(
                            "Reservation confirmed at deadline. ReservationId={ReservationId}, Players={Players}",
                            reservation.Id, reservation.CurrentPlayers);
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
                        lobby.ClosedReason = "Đến recruitmentDeadline mà chưa đạt minPlayers.";
                        lobby.UpdatedAt = now;

                        await _reservationRepository.UpdateAsync(reservation);
                        await _lobbyRepository.UpdateAsync(lobby);
                        await _reservationRepository.SaveChangesAsync();
                        await tx.CommitAsync();

                        _logger.LogInformation(
                            "Reservation timeout. ReservationId={ReservationId}, RefundBvc={RefundBvc}",
                            reservation.Id, reservation.DepositAmount);
                    }
                    return;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
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

        try
        {
            foreach (var reservation in reservations)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessSingleCafeApprovalExpiryAsync(reservation, cutoff);
                processed++;
            }

            await batchTx.CommitAsync(ct);
        }
        catch
        {
            await batchTx.RollbackAsync(ct);
            throw;
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
                await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

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

                    await _reservationRepository.UpdateAsync(reservation);
                    await _lobbyRepository.UpdateAsync(lobby);
                    await _reservationRepository.SaveChangesAsync();
                    await tx.CommitAsync();

                    _logger.LogInformation(
                        "Reservation expired by cafe no-approval. ReservationId={ReservationId}",
                        reservation.Id);
                    return;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
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

        try
        {
            foreach (var reservation in reservations)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessSingleNoShowAsync(reservation, cutoff);
                processed++;
            }

            await batchTx.CommitAsync(ct);
        }
        catch
        {
            await batchTx.RollbackAsync(ct);
            throw;
        }

        return processed;
    }

    /// <summary>
    /// GAP #11 fix: Wrap no-show processing trong Serializable transaction.
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

        const int MaxRetries = 3;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                try
                {
                    // Forfeit 100% (no-show).
                    // Idempotency key dựa trên reservationId (stable).
                    var forfeitIdempotencyKey = $"no-show-{reservation.Id:N}";

                    reservation.Status = ReservationStatus.NoShow;
                    lobby.Status = LobbyStatus.Closed;
                    lobby.ClosedAt = now;
                    lobby.ClosedReason = "No-show (không check-in sau grace period).";
                    lobby.UpdatedAt = now;

                    await _reservationRepository.UpdateAsync(reservation);
                    await _lobbyRepository.UpdateAsync(lobby);

                    await _walletService.ForfeitDepositAsync(
                        reservation.HostId,
                        reservation.DepositAmount,
                        lobby.Id,
                        reservation.Id,
                        forfeitIdempotencyKey);

                    await _reservationRepository.SaveChangesAsync();
                    await tx.CommitAsync();

                    _logger.LogInformation(
                        "Reservation no-show. ReservationId={ReservationId}, ForfeitBvc={ForfeitBvc}",
                        reservation.Id, reservation.DepositAmount);
                    return;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
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

    public async Task<ReservationCheckInResponseDto> CheckInAsync(Guid staffUserId, ReservationCheckInRequestDto request)
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
                HeldBvc = reservation.DepositAmount
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
                return await ExecuteCheckInTransactionAsync(reservation, staffUserId, request, now);
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
            "Không thể hoàn tất check-in sau nhiều lần thử.");
    }

    private async Task<ReservationCheckInResponseDto> ExecuteCheckInTransactionAsync(
        Reservation reservation,
        Guid staffUserId,
        ReservationCheckInRequestDto request,
        DateTime now)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        try
        {
            // 5. Lock seat inventory + game inventory (BR §17.3).
            var seatInventory = await _seatInventoryRepository.GetForUpdateAsync(
                reservation.CafeId, reservation.PlayDate, reservation.TimeSlot);
            if (seatInventory == null)
            {
                throw new ConflictException(
                    $"Không tìm thấy seat inventory cho reservation '{reservation.Id}'.");
            }

            var gameInventory = await _gameInventoryRepository.GetForUpdateAsync(
                reservation.CafeId, reservation.GameId, reservation.PlayDate, reservation.TimeSlot);
            if (gameInventory == null)
            {
                throw new ConflictException(
                    $"Không tìm thấy game inventory cho reservation '{reservation.Id}'.");
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
            reservation.Status = ReservationStatus.CheckedIn;
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
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                reservationId = reservation.Id,
                lobbyId = lobby.Id,
                activeSessionId = request.ActiveSessionId,
                staffUserId,
                hostId = reservation.HostId,
                cafeId = reservation.CafeId,
                checkedInAt = now,
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

            await _db.SaveChangesAsync();
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
                CheckedInAt = now,
                HeldBvc = reservation.DepositAmount
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
    /// - Early grace: scheduledTime - 30 phút (cho phép khách đến sớm, staff chuẩn bị).
    /// - Late grace: timeSlot.endTime + 30 phút (cho phép khách đến muộn sau giờ chơi).
    ///
    /// Trả 400 Bad Request qua ApiExceptionMiddleware nếu ngoài window.
    /// </summary>
    private async Task ValidateCheckInTimeWindowAsync(Reservation reservation, DateTime now)
    {
        const int EarlyGraceMinutes = 30;
        const int LateGraceMinutes = 30;

        var scheduledTime = reservation.ScheduledTime;
        var resolvedSchedule = await _scheduleResolver.ResolveAsync(
            reservation.CafeId, reservation.PlayDate, reservation.TimeSlot);
        var slotEndTime = reservation.PlayDate.ToDateTime(resolvedSchedule.EndTime);

        var windowStart = scheduledTime.AddMinutes(-EarlyGraceMinutes);
        var windowEnd = slotEndTime.AddMinutes(LateGraceMinutes);

        if (now < windowStart)
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.CheckInTimeWindowInvalid(
                    reservation.Id, scheduledTime, windowStart, windowEnd));
        }

        if (now > windowEnd)
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.CheckInTimeWindowLate(
                    reservation.Id, slotEndTime, windowEnd));
        }
    }

    // ===== Helpers =====

    private async Task<HostReservationContext> BuildHostEligibilityContextAsync(
        Guid hostId,
        ReservationQuoteRequestDto request,
        DepositQuoteResult quote,
        Wallet wallet,
        DateTime now)
    {
        var overlapList = await _lobbyRepository.GetOverlappingLobbiesAsync(
            hostId, request.PlayDate, request.TimeSlot, now, now);
        var firstOverlap = overlapList.FirstOrDefault();

        var activeLobbyByHost = await _lobbyRepository.GetActiveLobbiesByHostAsync(hostId);
        var activeLobbyByMember = await _lobbyRepository.GetActiveLobbiesByMemberAsync(hostId);

        var activeLobbyOnPlayDate = await _lobbyRepository.GetActiveLobbiesByHostAsync(hostId, request.PlayDate);
        var activeLobbyOnCafeSlot = await _lobbyRepository.GetActiveLobbiesByCafeDateSlotAsync(
            hostId, request.CafeId, request.PlayDate, request.TimeSlot);

        var hostCreateOrCancelCount = await _reservationRepository.CountHostActionsForPlayDateAsync(hostId, request.PlayDate);

        var resolvedSchedule = await _scheduleResolver.ResolveAsync(
            request.CafeId, request.PlayDate, request.TimeSlot);
        var scheduledTime = request.PlayDate.ToDateTime(resolvedSchedule.StartTime);
        var recruitmentDeadline = scheduledTime.AddMinutes(-20);

        return new HostReservationContext
        {
            HostId = hostId,
            CafeId = request.CafeId,
            PlayDate = request.PlayDate,
            TimeSlot = request.TimeSlot,
            RecruitmentDeadline = recruitmentDeadline,
            Now = now,
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
            CoolingOffExpiresAt = wallet.IsCoolingOff ? (DateTime?)null : null
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
        var maxDate = today.AddDays(7);
        if (playDate < today || playDate > maxDate)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.PlayDateOutOfRange(7));
        }
    }

    private static void ValidateTimeSlotWindow(ReservationQuoteRequestDto request)
    {
        if (!Enum.IsDefined(typeof(TimeSlot), request.TimeSlot))
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.InvalidTimeSlot(request.TimeSlot));
        }

        ValidateTimeSlotWindowRaw(request.MinPlayers, request.MaxPlayers);
    }

    private static void ValidateTimeSlotWindowRaw(int minPlayers, int maxPlayers)
    {
        if (minPlayers < 2)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.MinPlayersLessThanTwo);
        }

        if (maxPlayers < minPlayers)
        {
            throw new BadRequestException(
                ApiErrorMessages.Reservation.MinGreaterThanMaxPlayers(minPlayers, maxPlayers));
        }
    }

    private static LobbyStatus DetermineInitialLobbyStatus(Reservation reservation, CafeConfig cafeConfig, DateTime now)
    {
        var daysInFuture = (reservation.PlayDate.ToDateTime(TimeOnly.MinValue) - now.Date).TotalDays;
        var requiresApproval = daysInFuture >= cafeConfig.DistantThresholdDays
            && (reservation.MaxPlayers > 10 || cafeConfig.RequireApprovalForDistant);

        return requiresApproval ? LobbyStatus.PendingCafeApproval : LobbyStatus.PendingActivation;
    }

    private static (string PolicyName, decimal RefundPercent) ComputeRefundPolicy(
        DateTime scheduledTime,
        DateTime now,
        bool hasMembers,
        double minutesSinceCreated)
    {
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
            var seatInv = await _seatInventoryRepository.GetForUpdateAsync(
                reservation.CafeId, reservation.PlayDate, reservation.TimeSlot);
            if (seatInv != null)
            {
                seatInv.HeldSeats = Math.Max(0, seatInv.HeldSeats - reservation.MaxPlayers);
                seatInv.UpdatedAt = now;
                await _seatInventoryRepository.UpdateAsync(seatInv);
            }
        }

        if (reservation.GameInventoryId != null)
        {
            var gameInv = await _gameInventoryRepository.GetForUpdateAsync(
                reservation.CafeId, reservation.GameId, reservation.PlayDate, reservation.TimeSlot);
            if (gameInv != null)
            {
                gameInv.HeldCopies = Math.Max(0, gameInv.HeldCopies - 1);
                gameInv.UpdatedAt = now;
                await _gameInventoryRepository.UpdateAsync(gameInv);
            }
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
                        $"Reservation cho lobby '{lobbyId}' không tìm thấy sau retry.");
            }
        }

        throw new InternalServerErrorException(
            $"Không thể capture BVC cho lobby '{lobbyId}' sau {maxRetries} lần thử.");
    }

    // ===== GET LIST / DETAIL =====

    public async Task<ReservationDetailDto?> GetByIdAsync(Guid userId, Guid reservationId)
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
            TimeSlot = reservation.TimeSlot,
            PreferredStartTime = reservation.PreferredStartTime,
            ScheduledTime = reservation.ScheduledTime,
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
            LobbyStatus = reservation.Lobby?.Status.ToString(),
            CafeRejectionReason = reservation.Lobby?.CafeRejectionReason,
            ReservationCode = reservation.ReservationCode,
            CreatedAt = reservation.CreatedAt,
            UpdatedAt = reservation.UpdatedAt,
            IsHost = isHost,
            CanCancel = canCancel
        };
    }

    public async Task<ReservationListResponseDto> GetListAsync(Guid userId, ReservationListRequestDto request)
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
            TimeSlot = r.TimeSlot,
            CurrentPlayers = r.CurrentPlayers,
            MaxPlayers = r.MaxPlayers,
            Status = r.Status.ToString(),
            DepositAmount = r.DepositAmount,
            LobbyId = r.LobbyId,
            LobbyStatus = r.Lobby?.Status.ToString(),
            ReservationCode = r.ReservationCode,
            RecruitmentDeadline = r.RecruitmentDeadline,
            CreatedAt = r.CreatedAt,
            IsHost = r.HostId == userId
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
        Guid reservationId)
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
            TimeSlot = reservation.TimeSlot,
            MinPlayers = reservation.MinPlayers,
            MaxPlayers = reservation.MaxPlayers,
            CurrentPlayers = reservation.CurrentPlayers,
            DepositAmount = reservation.DepositAmount,
            ScheduledTime = reservation.ScheduledTime,
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
        LobbyPendingApprovalRequestDto request)
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
            TimeSlot = r.TimeSlot,
            MinPlayers = r.MinPlayers,
            MaxPlayers = r.MaxPlayers,
            CurrentPlayers = r.CurrentPlayers,
            DepositAmount = r.DepositAmount,
            ScheduledTime = r.ScheduledTime,
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

    private async Task ExecuteCompleteAndCaptureTransactionAsync(
        Reservation reservation,
        Guid activeSessionId,
        DateTime now,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        try
        {
            // 1. Lock seat inventory + game inventory (BR §17.3).
            var seatInventory = await _seatInventoryRepository.GetForUpdateAsync(
                reservation.CafeId, reservation.PlayDate, reservation.TimeSlot);
            if (seatInventory == null)
            {
                throw new ConflictException(
                    $"Không tìm thấy seat inventory cho reservation '{reservation.Id}'.");
            }

            var gameInventory = await _gameInventoryRepository.GetForUpdateAsync(
                reservation.CafeId, reservation.GameId, reservation.PlayDate, reservation.TimeSlot);
            if (gameInventory == null)
            {
                throw new ConflictException(
                    $"Không tìm thấy game inventory cho reservation '{reservation.Id}'.");
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

            // 5. Update reservation → Completed.
            reservation.Status = ReservationStatus.Completed;
            reservation.UpdatedAt = now;
            await _reservationRepository.UpdateAsync(reservation);

            // 6. Update lobby → Closed.
            var lobby = await _lobbyRepository.GetByIdAsync(reservation.LobbyId ?? Guid.Empty);
            if (lobby != null)
            {
                lobby.Status = LobbyStatus.Closed;
                lobby.ClosedAt = now;
                lobby.UpdatedAt = now;
                await _lobbyRepository.UpdateAsync(lobby);
            }

            // 7. Capture BVC (BR-REVENUE-01: deposit 100% về quán).
            //    Idempotency key gắn với reservationId + UpdatedAt để tránh double capture
            //    nếu có 2 scheduler/host race condition.
            var captureIdempotencyKey = $"capture-{reservation.Id:N}-{reservation.UpdatedAt.Ticks:x}";

            await _walletService.CaptureDepositAsync(
                reservation.HostId,
                reservation.DepositAmount,
                lobby?.Id,
                reservation.Id,
                captureIdempotencyKey,
                ct);

            // 8. Outbox event SessionCompleted (BR-REQUIRED §17.5).
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                reservationId = reservation.Id,
                lobbyId = lobby?.Id,
                activeSessionId,
                hostId = reservation.HostId,
                cafeId = reservation.CafeId,
                capturedBvc = reservation.DepositAmount,
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

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "Reservation completed + BVC captured. ReservationId={ReservationId}, CapturedBvc={Bvc}, ActiveSessionId={ActiveSessionId}",
                reservation.Id, reservation.DepositAmount, activeSessionId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
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
