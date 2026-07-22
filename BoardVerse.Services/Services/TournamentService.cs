using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class TournamentService : ITournamentService
{
    private readonly ITournamentRepository _tournamentRepository;
    private readonly IGameTemplateRepository _gameTemplateRepository;
    private readonly ICafePosRepository _cafePosRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly ISystemConfigurationProvider _systemConfigurationProvider;
    private readonly IKarmaRatingRepository _karmaRatingRepository;
    private readonly ILogger<TournamentService> _logger;

    public TournamentService(
        ITournamentRepository tournamentRepository,
        IGameTemplateRepository gameTemplateRepository,
        ICafePosRepository cafePosRepository,
        IUserProfileRepository userProfileRepository,
        ISystemConfigurationProvider systemConfigurationProvider,
        IKarmaRatingRepository karmaRatingRepository,
        ILogger<TournamentService> logger)
    {
        _tournamentRepository = tournamentRepository;
        _gameTemplateRepository = gameTemplateRepository;
        _cafePosRepository = cafePosRepository;
        _userProfileRepository = userProfileRepository;
        _systemConfigurationProvider = systemConfigurationProvider;
        _karmaRatingRepository = karmaRatingRepository;
        _logger = logger;
    }

    // ====================================================================
    // MANAGER: TOURNAMENT LIFECYCLE
    // ====================================================================

    public async Task<TournamentResponseDto> CreateTournamentAsync(
        Guid managerId, Guid cafeId, CreateTournamentRequestDto request)
    {
        // 1) Verify manager owns the cafe.
        await EnsureManagerOwnsCafeAsync(managerId, cafeId);

        // 2) Validate request.
        ValidateCreateRequest(request);

        // 3) Resolve tournament-supported GameTemplateId (config-driven, không hardcode tên "Splendor").
        var gameTemplateId = await ResolveTournamentGameTemplateIdAsync(request.GameTemplateId);

        // F14 Fix: Lấy MinParticipants từ GameTemplate config (Splendor = 2) thay vì hardcode = 4.
        // Cho phép hỗ trợ các game có min players khác nhau (vd Splendor Duel = 2).
        // F18: Manager có thể override cao hơn qua request.MinParticipants nhưng không thấp hơn GameTemplate config.
        var gameTemplate = await _gameTemplateRepository.GetByIdAsync(gameTemplateId);
        var templateMin = gameTemplate?.TournamentMinPlayersPerTable ?? 4;
        var minParticipants = request.MinParticipants.HasValue
            ? Math.Max(templateMin, request.MinParticipants.Value)
            : templateMin;

        var now = DateTime.UtcNow;
        var deadline = request.RegistrationDeadline
            ?? request.StartTime.AddHours(-24);

        if (deadline >= request.StartTime)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.RegistrationDeadlineAfterStartTime);
        }

        if (request.MinEloRequirement > request.MaxEloRequirement)
        {
            throw new BadRequestException(
                $"MinElo ({request.MinEloRequirement}) phải nhỏ hơn hoặc bằng MaxElo ({request.MaxEloRequirement}).");
        }

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            CreatedByManagerId = managerId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            GameTemplateId = gameTemplateId,
            StartTime = request.StartTime,
            RegistrationDeadline = deadline,
            RoundDurationMinutes = request.RoundDurationMinutes,
            MinParticipants = minParticipants,
            MaxParticipants = request.MaxParticipants,
            EntryFee = 0m,
            TotalRounds = 4,
            PreliminaryRounds = 3,
            FinalistCount = 4,
            CurrentRound = 0,
            MinKarmaRequirement = TournamentKarmaPolicy.ClampKarma(request.MinKarmaRequirement),
            MinEloRequirement = request.MinEloRequirement,
            MaxEloRequirement = request.MaxEloRequirement,
            WinnerKarmaBonus = TournamentKarmaPolicy.WinnerBonus,
            FinalistKarmaBonus = TournamentKarmaPolicy.GetFinalistBonus(2, 4),
            NoShowKarmaPenalty = TournamentKarmaPolicy.ClampPenalty(request.NoShowKarmaPenalty),
            PairingMode = request.PairingMode,
            Status = TournamentStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _tournamentRepository.AddAsync(tournament);
        await _tournamentRepository.SaveChangesAsync();

        return await BuildResponseAsync(tournament.Id, null);
    }

    public async Task<TournamentResponseDto> UpdateTournamentAsync(
        Guid managerId, Guid tournamentId, UpdateTournamentRequestDto request)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (tournament.Status != TournamentStatus.Draft)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.OnlyDraftEditable(tournamentId));
        }

        if (request.Title != null)
        {
            if (request.Title.Length < 5 || request.Title.Length > 200)
            {
                throw new BadRequestException(ApiErrorMessages.Tournament.TitleRequired);
            }
            tournament.Title = request.Title.Trim();
        }

        if (request.Description != null)
        {
            tournament.Description = request.Description.Trim();
        }

        if (request.StartTime.HasValue)
        {
            // Chỉ enforce future check khi Draft (chưa mở đăng ký).
            // Khi RegistrationOpen rồi, StartTime có thể đã qua nhưng tournament chưa start
            // → vẫn cho phép dời sang ngày future khác.
            if (tournament.Status == TournamentStatus.Draft
                && request.StartTime.Value <= DateTime.UtcNow)
            {
                throw new BadRequestException(ApiErrorMessages.Tournament.StartTimeMustBeFuture);
            }
            tournament.StartTime = request.StartTime.Value;

            // Nếu manager đổi StartTime mà không đổi RegistrationDeadline,
            // tự động re-derive deadline = StartTime - 24h (cùng rule như create).
            // Tránh case deadline cũ đã qua nhưng StartTime mới ở tương lai.
            if (!request.RegistrationDeadline.HasValue
                && tournament.RegistrationDeadline >= tournament.StartTime)
            {
                tournament.RegistrationDeadline = tournament.StartTime.AddHours(-24);
            }
        }

        if (request.RegistrationDeadline.HasValue)
        {
            tournament.RegistrationDeadline = request.RegistrationDeadline.Value;
        }

        // Re-validate deadline vs start time
        if (tournament.RegistrationDeadline >= tournament.StartTime)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.RegistrationDeadlineAfterStartTime);
        }

        if (request.RoundDurationMinutes.HasValue)
        {
            tournament.RoundDurationMinutes = request.RoundDurationMinutes.Value;
        }

        if (request.MaxParticipants.HasValue)
        {
            EnsureMaxParticipantsMultipleOf4(request.MaxParticipants.Value);
            tournament.MaxParticipants = request.MaxParticipants.Value;
        }

        if (request.MinKarmaRequirement.HasValue)
        {
            tournament.MinKarmaRequirement = TournamentKarmaPolicy.ClampKarma(request.MinKarmaRequirement.Value);
        }

        if (request.MinEloRequirement.HasValue || request.MaxEloRequirement.HasValue)
        {
            var minElo = request.MinEloRequirement ?? tournament.MinEloRequirement;
            var maxElo = request.MaxEloRequirement ?? tournament.MaxEloRequirement;
            if (minElo > maxElo)
            {
                throw new BadRequestException(
                    $"MinElo ({minElo}) phải nhỏ hơn hoặc bằng MaxElo ({maxElo}).");
            }
            tournament.MinEloRequirement = minElo;
            tournament.MaxEloRequirement = maxElo;
        }

        if (request.NoShowKarmaPenalty.HasValue)
        {
            tournament.NoShowKarmaPenalty = TournamentKarmaPolicy.ClampPenalty(request.NoShowKarmaPenalty.Value);
        }

        // WinnerKarmaBonus / FinalistKarmaBonus: hệ thống tự tính theo rank, không cho manager nhập tay.
        // Re-derive nếu FinalistCount thay đổi (hiện chưa expose API đổi nhưng giữ logic phòng trường hợp).
        tournament.WinnerKarmaBonus = TournamentKarmaPolicy.WinnerBonus;
        tournament.FinalistKarmaBonus = TournamentKarmaPolicy.GetFinalistBonus(2, tournament.FinalistCount);

        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        return await BuildResponseAsync(tournamentId, null);
    }

    public async Task<TournamentResponseDto> OpenRegistrationAsync(Guid managerId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (tournament.Status != TournamentStatus.Draft)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.CannotOpenRegistration(tournamentId));
        }

        if (tournament.RegistrationDeadline <= DateTime.UtcNow)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.CannotOpenRegistration(tournamentId));
        }

        tournament.Status = TournamentStatus.RegistrationOpen;
        tournament.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();
        return await BuildResponseAsync(tournamentId, null);
    }

    public async Task<TournamentResponseDto> CloseRegistrationAsync(Guid managerId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (tournament.Status != TournamentStatus.RegistrationOpen)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.RegistrationNotOpen(tournamentId));
        }

        tournament.Status = TournamentStatus.RegistrationClosed;
        tournament.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();
        return await BuildResponseAsync(tournamentId, null);
    }

    public async Task<TournamentResponseDto> ReopenRegistrationAsync(Guid managerId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (tournament.Status != TournamentStatus.RegistrationClosed)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotReopenRegistration(tournamentId));
        }

        tournament.Status = TournamentStatus.RegistrationOpen;
        tournament.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();
        return await BuildResponseAsync(tournamentId, null);
    }

    public async Task<TournamentResponseDto> StartTournamentAsync(Guid managerId, Guid tournamentId)
    {
        // Default: không cho phép partial start, không auto-shorten.
        return await StartTournamentCoreAsync(
            managerId,
            tournamentId,
            allowPartialStart: false,
            reducedRoundsOverride: null,
            autoShortenMode: "Auto",
            reason: null);
    }

    public async Task<TournamentResponseDto> StartTournamentWithOptionsAsync(
        Guid managerId, Guid tournamentId, StartTournamentOptionsDto options)
    {
        // Validate options
        if (options.AutoShortenMode != "Auto" && options.AutoShortenMode != "Manual")
        {
            throw new BadRequestException(
                ApiErrorMessages.Tournament.InvalidAutoShortenMode(options.AutoShortenMode));
        }

        if (options.ReducedRounds.HasValue
            && (options.ReducedRounds.Value < 1 || options.ReducedRounds.Value > 5))
        {
            throw new BadRequestException(
                ApiErrorMessages.Tournament.InvalidReducedRounds(options.ReducedRounds.Value));
        }

        return await StartTournamentCoreAsync(
            managerId,
            tournamentId,
            allowPartialStart: options.AllowPartialStart,
            reducedRoundsOverride: options.AutoShortenMode == "Manual" ? options.ReducedRounds : null,
            autoShortenMode: options.AutoShortenMode,
            reason: options.Reason);
    }

    /// <summary>
    /// Core start logic. Được dùng bởi cả StartTournamentAsync (default)
    /// và StartTournamentWithOptionsAsync (manager override).
    ///
    /// Shortage handling flow:
    ///   1. Check state (RegistrationClosed/Open).
    ///   2. Count checkedIn participants.
    ///   3. Nếu checkedIn &lt; MinParticipants:
    ///      a. Nếu Tournament.AutoExtendOnShortage && ExtensionCount &lt; MaxExtensionCount:
    ///         - Tự động extend registration deadline.
    ///         - Push notification cho users chưa check-in.
    ///         - Trả về status "Extended" → manager retry sau khi extend.
    ///      b. Nếu AllowPartialStart = true: tiếp tục với shortage.
    ///         - Tính ActualPreliminaryRounds bằng TournamentRoundsCalculator.
    ///         - Set StartedWithShortage = true (audit trail).
    ///      c. Nếu không có a hoặc b: throw 409.
    ///   4. Build matches, mark Active, save.
    /// </summary>
    private async Task<TournamentResponseDto> StartTournamentCoreAsync(
        Guid managerId,
        Guid tournamentId,
        bool allowPartialStart,
        int? reducedRoundsOverride,
        string autoShortenMode,
        string? reason)
    {
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (tournament.Status != TournamentStatus.RegistrationClosed
            && tournament.Status != TournamentStatus.RegistrationOpen)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.CannotStartRegistrationOpen(tournamentId));
        }

        var checkedIn = tournament.Participants
            .Count(p => p.Status == TournamentParticipantStatus.CheckedIn
                || p.Status == TournamentParticipantStatus.Active);

        // === Shortage check ===
        if (checkedIn < tournament.MinParticipants)
        {
            // Option a: Auto-extend registration deadline (if configured)
            // F2 Fix: Cho phép auto-extend cả khi status = RegistrationClosed.
            // Thực tế: manager thường CloseRegistration trước khi Start (để chốt danh sách).
            // Nếu thiếu người, tự động reopen + extend deadline để có thêm cơ hội tuyển.
            if (tournament.AutoExtendOnShortage
                && tournament.ExtensionCount < tournament.MaxExtensionCount)
            {
                // Mở lại registration nếu đã đóng.
                if (tournament.Status == TournamentStatus.RegistrationClosed)
                {
                    tournament.Status = TournamentStatus.RegistrationOpen;
                }
                return await PerformAutoExtensionAsync(tournament, checkedIn);
            }

            // Option b: Allow partial start (manager override)
            if (allowPartialStart)
            {
                // Continue với shortage, sẽ đánh dấu StartedWithShortage sau
            }
            else
            {
                throw new ConflictException(
                    ApiErrorMessages.Tournament.CannotStartNotEnoughParticipants(tournament.MinParticipants, checkedIn));
            }
        }

        // === Determine actual rounds ===
        var actualPreliminaryRounds = tournament.PreliminaryRounds;
        if (checkedIn < tournament.MinParticipants)
        {
            // Shortage: tính optimal rounds
            if (autoShortenMode == "Manual" && reducedRoundsOverride.HasValue)
            {
                actualPreliminaryRounds = reducedRoundsOverride.Value;
            }
            else
            {
                actualPreliminaryRounds = TournamentRoundsCalculator.CalculateOptimalPreliminaryRounds(
                    checkedIn, tournament.PreliminaryRounds);
            }
        }

        // === Build pairings và set state ===
        // F4 Fix: Auto-promote Registered participants (đã đến quán nhưng manager không check-in trước) → Active.
        // Thực tế board game cafe: manager thường bấm Start kèm danh sách đến luôn,
        // không check-in từng người. Nếu không auto-promote, participants "Registered" bị bỏ sót
        // → tournament chạy thiếu người dù họ đã đến.
        var now = DateTime.UtcNow;
        foreach (var p in tournament.Participants
            .Where(p => p.Status == TournamentParticipantStatus.Registered))
        {
            p.Status = TournamentParticipantStatus.Active;
            p.CheckedInAt ??= now;
            p.CheckedInByStaffId ??= managerId;
            p.UpdatedAt = now;
        }

        var activeParticipants = tournament.Participants
            .Where(p => p.Status == TournamentParticipantStatus.CheckedIn
                || p.Status == TournamentParticipantStatus.Active)
            .OrderBy(p => p.CheckedInAt ?? p.RegisteredAt)
            .ToList();

        var matches = BuildRoundMatches(tournament, 1, activeParticipants);

        tournament.Matches = matches;
        tournament.Status = TournamentStatus.OnGoing;
        tournament.CurrentRound = 1;
        tournament.StartedAt = DateTime.UtcNow;
        tournament.ActualPreliminaryRounds = actualPreliminaryRounds;
        tournament.StartedWithShortage = checkedIn < tournament.MinParticipants;
        tournament.UpdatedAt = DateTime.UtcNow;

        // Mark remaining CheckedIn participants as Active
        foreach (var p in activeParticipants.Where(p => p.Status == TournamentParticipantStatus.CheckedIn))
        {
            p.Status = TournamentParticipantStatus.Active;
            p.UpdatedAt = DateTime.UtcNow;
        }

        await _tournamentRepository.SaveChangesAsync();
        return await BuildResponseAsync(tournamentId, null);
    }

    /// <summary>
    /// Auto-extend registration deadline khi thiếu người.
    /// ExtensionMinutesPerAttempt (default 30) mỗi lần, tối đa MaxExtensionCount lần.
    /// F7 Fix: Log audit event để admin/debug theo dõi + mobile app có thể polling để biết extend.
    /// Khi NotificationService sẵn sàng, swap ILogger → IPushNotificationService.SendTournamentExtensionAsync.
    /// </summary>
    private async Task<TournamentResponseDto> PerformAutoExtensionAsync(
        Tournament tournament, int currentCheckedIn)
    {
        tournament.RegistrationDeadline = tournament.RegistrationDeadline
            .AddMinutes(tournament.ExtensionMinutesPerAttempt);
        tournament.ExtensionCount += 1;
        tournament.UpdatedAt = DateTime.UtcNow;

        // F7: Audit log cho admin/debug. Mobile app cần polling endpoint GetTournamentAsync
        // để detect RegistrationDeadline thay đổi và hiển thị banner "Đã được gia hạn".
        // Khi NotificationService sẵn sàng, hook vào đây để push notification tới:
        //   - Mobile app users đã đăng ký (status=Registered/CheckedIn) — thông báo giải chưa bắt đầu.
        //   - Manager — xác nhận auto-extend đã trigger.
        // Hiện tại: structured log để monitoring tool scrape + audit trail.
        var registeredCount = tournament.Participants.Count(p =>
            p.Status == TournamentParticipantStatus.Registered
            || p.Status == TournamentParticipantStatus.CheckedIn);
        var notCheckedInCount = tournament.Participants.Count(p =>
            p.Status == TournamentParticipantStatus.Registered);

        _logger.LogWarning(
            "[TournamentAutoExtension] TournamentId={TournamentId}, ExtensionCount={ExtensionCount}/{MaxExtensions}, " +
            "NewDeadline={NewDeadline:o}, TotalRegistered={RegisteredCount}, NotCheckedIn={NotCheckedIn}, " +
            "CurrentCheckedIn={CurrentCheckedIn}, MinRequired={MinRequired}",
            tournament.Id,
            tournament.ExtensionCount,
            tournament.MaxExtensionCount,
            tournament.RegistrationDeadline,
            registeredCount,
            notCheckedInCount,
            currentCheckedIn,
            tournament.MinParticipants);

        await _tournamentRepository.SaveChangesAsync();

        throw new ConflictException(
            ApiErrorMessages.Tournament.RegistrationAutoExtended(
                tournament.ExtensionCount,
                tournament.MaxExtensionCount,
                tournament.ExtensionMinutesPerAttempt,
                currentCheckedIn,
                tournament.MinParticipants));
    }

    public async Task<TournamentResponseDto> ExtendRegistrationAsync(
        Guid managerId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (tournament.Status != TournamentStatus.RegistrationOpen)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotExtendRegistrationNotOpen(tournamentId));
        }

        if (tournament.ExtensionCount >= tournament.MaxExtensionCount)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotExtendRegistrationMaxReached(
                    tournament.MaxExtensionCount, tournament.ExtensionMinutesPerAttempt));
        }

        tournament.RegistrationDeadline = tournament.RegistrationDeadline
            .AddMinutes(tournament.ExtensionMinutesPerAttempt);
        tournament.ExtensionCount += 1;
        tournament.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();
        return await BuildResponseAsync(tournamentId, null);
    }

    public async Task<TournamentResponseDto> CancelTournamentAsync(
        Guid managerId, Guid tournamentId, string? reason)
    {
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        // BR-09 mirror + flow 4.9: Cho phép cancel từ RegistrationOpen / RegistrationClosed / OnGoing.
        // Lý do thực tế:
        // - RegistrationOpen / RegistrationClosed: chưa ai chơi → cancel an toàn.
        // - OnGoing: tournament đã chạy 1-2 round, manager muốn dừng vì lý do bất khả kháng
        //   (vd: cúp điện, mưa lớn, dispute giữa các đội). Player tự xử lý cash refund ngoài app.
        //   CHỈ chặn khi Status = Completed — không thể cancel sau khi đã trao giải (Elo/Karma đã sync).
        
        // Chặn cancel nếu đã cancelled rồi (idempotent nhưng test expect 409)
        if (tournament.Status == TournamentStatus.Cancelled)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.AlreadyCancelled(tournamentId));
        }
        
        if (tournament.Status == TournamentStatus.Completed)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.CannotCancelCompleted(tournamentId));
        }

        var registeredCount = await _tournamentRepository.CountActiveParticipantsAsync(tournamentId);
        if (registeredCount > 0 && string.IsNullOrWhiteSpace(reason))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.CancellationReasonRequired);
        }

        // Auto-mark tất cả participants (Registered/CheckedIn/Active) thành Withdrawn
        // để dọn dẹp state. Player vẫn có thể gọi unregister idempotent.
        var now = DateTime.UtcNow;
        var participantsToWithdraw = tournament.Participants
            .Where(p => p.Status != TournamentParticipantStatus.Withdrawn
                && p.Status != TournamentParticipantStatus.NoShow
                && p.Status != TournamentParticipantStatus.Finished)
            .ToList();
        foreach (var p in participantsToWithdraw)
        {
            p.Status = TournamentParticipantStatus.Withdrawn;
            p.UpdatedAt = now;
        }

        // Hủy các matches chưa diễn ra (nếu có - thường chỉ có ở RegistrationClosed)
        var matchesToCancel = tournament.Matches
            .Where(m => m.Status == TournamentMatchStatus.Scheduled
                || m.Status == TournamentMatchStatus.OnGoing)
            .ToList();
        foreach (var m in matchesToCancel)
        {
            m.Status = TournamentMatchStatus.Cancelled;
            m.Notes = $"[Tournament cancelled] {(string.IsNullOrWhiteSpace(reason) ? "" : reason.Trim())}";
            m.UpdatedAt = now;
        }

        tournament.Status = TournamentStatus.Cancelled;
        tournament.CancellationReason = reason?.Trim();
        tournament.CancelledAt = now;
        tournament.UpdatedAt = now;

        await _tournamentRepository.SaveChangesAsync();
        return await BuildResponseAsync(tournament, null);  // Pass updated entity, not re-fetch
    }

    public async Task<TournamentResponseDto> CompleteTournamentAsync(Guid managerId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (tournament.Status == TournamentStatus.Completed)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.AlreadyCompleted(tournamentId));
        }

        if (tournament.Status != TournamentStatus.OnGoing)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.OnlyOnGoingCompletable);
        }

        // Verify Final match is completed
        var finalMatch = tournament.Matches.FirstOrDefault(m => m.IsFinal);
        if (finalMatch == null || finalMatch.Status != TournamentMatchStatus.Completed)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.FinalMatchNotCompleted);
        }

        // Apply Karma bonuses + sync Elo về UserProfile (idempotent — guard bằng IsFinalEloSynced).
        if (!tournament.IsFinalEloSynced)
        {
            await ApplyFinalKarmaBonusesAsync(tournament);
            await SyncFinalEloToProfilesAsync(tournament);
            tournament.IsFinalEloSynced = true;
        }

        tournament.Status = TournamentStatus.Completed;
        tournament.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();
        return await BuildResponseAsync(tournamentId, null);
    }

    // ====================================================================
    // QUERIES
    // ====================================================================

    public async Task<TournamentResponseDto> GetTournamentAsync(Guid tournamentId, Guid? currentUserId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));
        return await BuildResponseAsync(tournament, currentUserId);
    }

    public async Task<IReadOnlyList<TournamentResponseDto>> GetOpenTournamentsAsync(Guid? currentUserId)
    {
        var tournaments = await _tournamentRepository.GetAllOpenAsync();
        var responses = new List<TournamentResponseDto>();
        foreach (var t in tournaments)
        {
            responses.Add(await BuildResponseAsync(t, currentUserId));
        }
        return responses;
    }

    public async Task<IReadOnlyList<TournamentResponseDto>> GetCafeTournamentsAsync(
        Guid cafeId, Guid? currentUserId, string? status)
    {
        TournamentStatus? statusEnum = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TournamentStatus>(status, ignoreCase: true, out var parsed))
            {
                throw new BadRequestException(
                    $"Trạng thái tournament không hợp lệ: '{status}'. Dùng Draft, RegistrationOpen, RegistrationClosed, OnGoing, Completed hoặc Cancelled.");
            }
            statusEnum = parsed;
        }

        var tournaments = await _tournamentRepository.GetByCafeAsync(cafeId, statusEnum);
        var responses = new List<TournamentResponseDto>();
        foreach (var t in tournaments)
        {
            responses.Add(await BuildResponseAsync(t, currentUserId));
        }
        return responses;
    }

    // ====================================================================
    // PLAYER: REGISTER / WITHDRAW / CHECK-IN
    // ====================================================================

    public async Task<TournamentParticipantResponseDto> RegisterAsync(Guid tournamentId, Guid userId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (tournament.Status != TournamentStatus.RegistrationOpen)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.RegistrationNotOpen(tournamentId));
        }

        if (tournament.RegistrationDeadline <= DateTime.UtcNow)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.RegistrationDeadlinePassed);
        }

        var existing = await _tournamentRepository.GetParticipantAsync(tournamentId, userId);
        if (existing != null)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.AlreadyRegistered(tournamentId));
        }

        var activeCount = await _tournamentRepository.CountActiveParticipantsAsync(tournamentId);
        if (activeCount >= tournament.MaxParticipants)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.TournamentFull(tournamentId));
        }

        // Karma check
        // F9 Fix: Cache user profile snapshot trong 1 query, dùng cho cả Karma check + snapshot fields.
        // Trước đây: GetByIdWithProfileAsync được gọi 2-3 lần (Karma check, Karma snapshot, Elo snapshot) → N+1.
        // Giờ: 1 query duy nhất, cache vào local var.
        var user = await _userProfileRepository.GetByIdWithProfileAsync(userId);
        if (user?.Profile == null)
        {
            throw new NotFoundException(
                $"Không tìm thấy hồ sơ của user {userId}. Vui lòng cập nhật profile trước khi tham gia giải đấu.");
        }

        var currentKarma = user.Profile.KarmaPoints;
        if (tournament.MinKarmaRequirement > 0 && currentKarma < tournament.MinKarmaRequirement)
        {
            throw new ForbiddenException(
                ApiErrorMessages.Tournament.KarmaRequirementNotMet(tournament.MinKarmaRequirement, currentKarma));
        }

        var currentElo = user.Profile.GlobalElo > 0 ? user.Profile.GlobalElo : EloRatingHelper.DefaultRating;
        if (currentElo < tournament.MinEloRequirement || currentElo > tournament.MaxEloRequirement)
        {
            throw new ForbiddenException(
                $"Elo hiện tại ({currentElo}) nằm ngoài khoảng cho phép [{tournament.MinEloRequirement}, {tournament.MaxEloRequirement}] của giải đấu này.");
        }

        var now = DateTime.UtcNow;
        var karmaSnapshot = currentKarma;
        var eloSnapshot = user.Profile.GlobalElo > 0 ? user.Profile.GlobalElo : EloRatingHelper.DefaultRating;
        var participant = new TournamentParticipant
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            RegisteredAt = now,
            KarmaAtRegistration = karmaSnapshot,
            InitialElo = eloSnapshot,
            Status = TournamentParticipantStatus.Registered,
            TotalPrestigePoints = 0,
            TotalCardsBought = 0,
            SwissWins = 0,
            SwissDraws = 0,
            SwissLosses = 0,
            EloDelta = 0,
            FinalElo = eloSnapshot,
            CreatedAt = now
        };

        await _tournamentRepository.AddParticipantAsync(participant);

        try
        {
            await _tournamentRepository.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message?.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message?.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message?.Contains("TournamentParticipants_TournamentId_UserId", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Race: 2 requests cùng register 1 user trong cùng tournament.
            throw new ConflictException(ApiErrorMessages.Tournament.AlreadyRegistered(tournamentId));
        }

        // Reload with User navigation
        var reloaded = await _tournamentRepository.GetParticipantByIdAsync(participant.Id);
        return MapParticipantDto(reloaded!);
    }

    public async Task<TournamentParticipantResponseDto> WithdrawRegistrationAsync(Guid tournamentId, Guid userId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        var participant = await _tournamentRepository.GetParticipantAsync(tournamentId, userId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.ParticipantNotRegistered(tournamentId));

        // Tournament Cancelled/Completed → idempotent no-op (tránh lộ state trước kia).
        if (tournament.Status == TournamentStatus.Cancelled
            || tournament.Status == TournamentStatus.Completed)
        {
            return MapParticipantDto(participant);
        }

        if (participant.Status == TournamentParticipantStatus.Withdrawn)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.AlreadyWithdrawn(tournamentId));
        }

        // Không cho rút lui khi player đã check-in, đang thi đấu hoặc đã kết thúc
        // → tránh bỏ trống ghế ở vòng Final.
        if (participant.Status == TournamentParticipantStatus.CheckedIn
            || participant.Status == TournamentParticipantStatus.Active
            || participant.Status == TournamentParticipantStatus.Finished)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotWithdrawAfterCheckIn(participant.Status));
        }

        participant.Status = TournamentParticipantStatus.Withdrawn;
        participant.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();
        return MapParticipantDto(participant);
    }

    public async Task<IReadOnlyList<TournamentParticipantResponseDto>> GetParticipantsAsync(Guid tournamentId)
    {
        var participants = await _tournamentRepository.GetParticipantsAsync(tournamentId);
        return participants.Select(MapParticipantDto).ToList();
    }

    // ====================================================================
    // POS: CHECK-IN PARTICIPANTS
    // ====================================================================

    public async Task<TournamentParticipantResponseDto> CheckInParticipantAsync(
        Guid managerId, Guid tournamentId, Guid participantId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        var participant = await _tournamentRepository.GetParticipantByIdAsync(participantId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.ParticipantNotFound);

        if (participant.TournamentId != tournamentId)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.ParticipantNotInTournament);
        }

        if (participant.Status != TournamentParticipantStatus.Registered)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.AlreadyCheckedIn);
        }

        participant.Status = TournamentParticipantStatus.CheckedIn;
        participant.CheckedInAt = DateTime.UtcNow;
        participant.CheckedInByStaffId = managerId;
        participant.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();
        return MapParticipantDto(participant);
    }

    public async Task<TournamentParticipantResponseDto> ManagerAddWalkInParticipantAsync(
        Guid managerId, Guid tournamentId, AddWalkInParticipantRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.WalkInDisplayNameRequired);
        }

        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        // Walk-in được phép ở mọi trạng thái ngoại trừ Draft / Completed / Cancelled.
        // Lý do: quán có thể nhận khách vãng lai bất kỳ lúc nào trước khi R1 hoàn thành.
        if (tournament.Status != TournamentStatus.RegistrationOpen
            && tournament.Status != TournamentStatus.RegistrationClosed
            && tournament.Status != TournamentStatus.OnGoing)
        {
            throw new ConflictException(
                $"Không thể thêm khách vãng lai vào giải đang ở trạng thái [{tournament.Status}].");
        }

        // Không cho add walk-in sau khi Final đã build (Final có 4 slot cố định, BR-13 analogy).
        if (tournament.Matches?.Any(m => m.IsFinal) == true)
        {
            throw new ConflictException(
                "Đã có bàn chung kết. Không thể thêm khách vãng lai.");
        }

        // Thực tế board game cafe: walk-in chỉ được vào khi R1 CHƯA hoàn thành.
        // Sau khi R1 đã có Swiss score (≥1 match Completed), reject để giữ fairness
        // — player gốc đã đầu tư 1 round, walk-in không thể nhảy vào giữa R2+ để "rửa" Swiss.
        var roundOneCompleted = tournament.Matches?.Any(m =>
            m.RoundNumber == 1
            && m.Status == TournamentMatchStatus.Completed) ?? false;
        if (roundOneCompleted)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.WalkInClosedAfterRoundOne);
        }

        // Không cho add walk-in khi round hiện tại đang OnGoing (mid-match).
        // Manager chờ round kết thúc rồi add trước khi AdvanceRound.
        var currentRoundInProgress = tournament.Matches?.Any(m =>
            m.RoundNumber == tournament.CurrentRound
            && m.Status == TournamentMatchStatus.OnGoing) ?? false;
        if (currentRoundInProgress)
        {
            throw new ConflictException(
                "Vòng đấu hiện tại đang diễn ra. Hãy đợi round kết thúc rồi thêm khách vãng lai.");
        }

        // Idempotency: DisplayName đã tồn tại (walk-in only).
        var trimmedName = request.DisplayName.Trim();
        var existingWalkIn = tournament.Participants?
            .FirstOrDefault(p => p.IsWalkIn
                && string.Equals(p.WalkInDisplayName, trimmedName, StringComparison.OrdinalIgnoreCase));
        if (existingWalkIn != null)
        {
            throw new ConflictException(
                $"Đã có khách vãng lai với tên '{trimmedName}' trong giải.");
        }

        // Walk-in luôn join từ Round 1. Nếu R1 đã hoàn thành thì bị reject ở check trên.
        // (Tournament chưa start → JoinedRound = 1; Tournament OnGoing nhưng R1 chưa Completed → vẫn JoinedRound = 1.)
        var joinedRound = 1;

        // F16 Fix: Auto CheckedIn khi tournament đã RegistrationClosed (chưa Start) hoặc OnGoing.
        // Thực tế board game cafe: walk-in đến quán → manager add ngay tại POS → walk-in đã có mặt
        // → nên CheckedIn luôn để sẵn sàng tham gia R1, không cần manager check-in thêm 1 bước.
        // Status = RegistrationOpen thì giữ Registered (vì có thể chưa đến ngay).
        var initialStatus = tournament.Status == TournamentStatus.RegistrationOpen
            ? TournamentParticipantStatus.Registered
            : TournamentParticipantStatus.CheckedIn;

        var now = DateTime.UtcNow;
        var walkIn = new TournamentParticipant
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = null,
            IsWalkIn = true,
            WalkInDisplayName = trimmedName,
            WalkInPhoneNumber = request.PhoneNumber?.Trim(),
            RegisteredByStaffId = managerId,
            JoinedRoundNumber = joinedRound,
            RegisteredAt = now,
            KarmaAtRegistration = 0,
            InitialElo = EloRatingHelper.DefaultRating, // Walk-in không có profile → dùng default rating.
            Status = initialStatus,
            CheckedInAt = initialStatus == TournamentParticipantStatus.CheckedIn ? now : null,
            CheckedInByStaffId = initialStatus == TournamentParticipantStatus.CheckedIn ? managerId : null,
            TotalPrestigePoints = 0,
            TotalCardsBought = 0,
            SwissWins = 0,
            SwissDraws = 0,
            SwissLosses = 0,
            EloDelta = 0,
            FinalElo = EloRatingHelper.DefaultRating,
            CreatedAt = now
        };

        await _tournamentRepository.AddParticipantAsync(walkIn);
        await _tournamentRepository.SaveChangesAsync();

        var reloaded = await _tournamentRepository.GetParticipantByIdAsync(walkIn.Id);
        return MapParticipantDto(reloaded!);
    }

    public async Task<TournamentParticipantResponseDto> MarkNoShowAsync(
        Guid managerId, Guid tournamentId, Guid participantId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        var participant = await _tournamentRepository.GetParticipantByIdAsync(participantId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.ParticipantNotFound);

        if (participant.TournamentId != tournamentId)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.ParticipantNotInTournament);
        }

        if (participant.Status == TournamentParticipantStatus.NoShow)
        {
            return MapParticipantDto(participant);
        }

        // NoShow chỉ áp dụng cho player chưa tham gia vòng đấu nào.
        // Nếu đã Active (đã chơi ít nhất 1 round) hoặc Finished, không thể đánh no-show
        // vì FinalRank và Elo đã được tính. Manager cần xử lý riêng (refund/forfeit).
        if (participant.Status == TournamentParticipantStatus.Finished
            || participant.Status == TournamentParticipantStatus.Active)
        {
            throw new ConflictException(
                $"Không thể đánh dấu no-show khi người chơi đang ở trạng thái [{participant.Status}]. " +
                "Người chơi đã tham gia vòng đấu hoặc đã hoàn thành giải.");
        }

        if (participant.Status != TournamentParticipantStatus.Registered
            && participant.Status != TournamentParticipantStatus.CheckedIn)
        {
            throw new ConflictException(
                $"Không thể đánh dấu no-show khi người chơi ở trạng thái [{participant.Status}].");
        }

        participant.Status = TournamentParticipantStatus.NoShow;
        participant.UpdatedAt = DateTime.UtcNow;

        // Apply Karma penalty + audit log
        if (tournament.NoShowKarmaPenalty != 0 && participant.UserId.HasValue)
        {
            var profile = await _userProfileRepository.GetProfileByUserIdAsync(participant.UserId.Value);
            if (profile != null)
            {
                var before = profile.KarmaPoints;
                var after = TournamentKarmaPolicy.ClampKarma(before + tournament.NoShowKarmaPenalty);
                var actualDelta = after - before;

                profile.KarmaPoints = after;
                profile.GamerTier = KarmaRatingHelper.ResolveTier(after);
                profile.UpdatedAt = DateTime.UtcNow;

                await _karmaRatingRepository.AddKarmaLogAsync(new KarmaLog
                {
                    Id = Guid.NewGuid(),
                    UserId = participant.UserId.Value,
                    ViolationCategory = KarmaViolationCategory.NoShow,
                    Source = KarmaLogSource.TournamentReward,
                    KarmaPointsChange = actualDelta,
                    KarmaBefore = before,
                    KarmaAfter = after,
                    Reason = $"[Tournament {tournamentId}] Không đến tham dự (no-show)",
                    RelatedLobbyId = null,
                    PerformedByUserId = managerId,
                    IsAdminAdjustment = false,
                    CreatedAt = DateTime.UtcNow
                });

                await _karmaRatingRepository.SaveChangesAsync();
            }
        }

        await _tournamentRepository.SaveChangesAsync();
        return MapParticipantDto(participant);
    }

    // ====================================================================
    // MATCHES
    // ====================================================================

    public async Task<IReadOnlyList<TournamentMatchResponseDto>> GetMatchesAsync(Guid tournamentId)
    {
        var matches = await _tournamentRepository.GetMatchesByTournamentAsync(tournamentId);
        return matches.Select(MapMatchDto).ToList();
    }

    public async Task<IReadOnlyList<TournamentMatchResponseDto>> GetRoundMatchesAsync(
        Guid tournamentId, int roundNumber)
    {
        var matches = await _tournamentRepository.GetMatchesByRoundAsync(tournamentId, roundNumber);
        return matches.Select(MapMatchDto).ToList();
    }

    public async Task<TournamentMatchResponseDto> StartMatchAsync(Guid managerId, Guid matchId)
    {
        var match = await _tournamentRepository.GetMatchByIdAsync(matchId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.MatchNotFound(matchId));

        var tournament = await _tournamentRepository.GetByIdAsync(match.TournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(match.TournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (match.Status != TournamentMatchStatus.Scheduled)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.MatchAlreadyStartedOrFinished);
        }

        match.Status = TournamentMatchStatus.OnGoing;
        match.ActualStartTime = DateTime.UtcNow;
        match.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();
        return MapMatchDto(match);
    }

    public async Task<TournamentMatchResponseDto> RecordMatchResultAsync(
        Guid managerId, Guid matchId, RecordMatchResultRequestDto request)
    {
        var match = await _tournamentRepository.GetMatchByIdAsync(matchId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.MatchNotFound(matchId));

        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(match.TournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(match.TournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (match.Status != TournamentMatchStatus.OnGoing
            && match.Status != TournamentMatchStatus.Scheduled)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.MatchNotOnGoing(matchId));
        }

        // Schedule → Completed mà không qua OnGoing bỏ qua audit (ActualStartTime/EndTime).
        // Tự động set ActualStartTime khi manager skip StartMatch step (defensive).
        if (match.Status == TournamentMatchStatus.Scheduled)
        {
            match.ActualStartTime = DateTime.UtcNow;
            match.Status = TournamentMatchStatus.OnGoing;
        }

        // Validate that winner is in the player list
        var playerSlots = new[] { match.Player1Id, match.Player2Id, match.Player3Id, match.Player4Id }
            .Where(p => p.HasValue).Select(p => p!.Value).ToList();

        if (!playerSlots.Contains(request.WinnerUserId ?? Guid.Empty))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.WinnerMustBePlayer(matchId));
        }

        // === F3.1 Fix: Validate Results.Count matches player slot count ===
        // Tránh Swiss score thiếu do manager bỏ sót 1 player khi nhập kết quả.
        if (request.Results.Count != playerSlots.Count)
        {
            throw new BadRequestException(
                $"Kết quả phải bao gồm đủ {playerSlots.Count} người chơi (hiện tại có {request.Results.Count}). " +
                "Vui lòng nhập điểm và số thẻ đã mua cho từng người chơi trong bàn.");
        }

        // === F3 Fix: Per-game max score validation ===
        // Lấy GameTemplate config (TournamentMaxScorePerPlayer) từ tournament.GameTemplate.
        // Splendor = 15; Splendor Duel = 20. Default 15.
        var maxScorePerPlayer = tournament.GameTemplate?.TournamentMaxScorePerPlayer ?? 15;
        foreach (var r in request.Results)
        {
            if (r.Score > maxScorePerPlayer)
            {
                throw new BadRequestException(
                    $"Điểm của player {r.UserId} ({r.Score}) vượt quá giới hạn {maxScorePerPlayer} " +
                    $"của game '{tournament.GameTemplate?.Name ?? "Tournament"}'. " +
                    "Vui lòng kiểm tra lại kết quả.");
            }
        }

        // Apply scores to slot positions
        foreach (var r in request.Results)
        {
            if (!playerSlots.Contains(r.UserId ?? Guid.Empty))
            {
                throw new BadRequestException(ApiErrorMessages.Tournament.PlayerNotInMatch(matchId, r.UserId ?? Guid.Empty));
            }

            if (match.Player1Id == r.UserId)
            {
                match.Player1Score = r.Score;
                match.Player1CardsBought = r.CardsBought;
            }
            else if (match.Player2Id == r.UserId)
            {
                match.Player2Score = r.Score;
                match.Player2CardsBought = r.CardsBought;
            }
            else if (match.Player3Id == r.UserId)
            {
                match.Player3Score = r.Score;
                match.Player3CardsBought = r.CardsBought;
            }
            else if (match.Player4Id == r.UserId)
            {
                match.Player4Score = r.Score;
                match.Player4CardsBought = r.CardsBought;
            }
        }

        match.WinnerPlayerId = request.WinnerUserId;
        match.Status = TournamentMatchStatus.Completed;
        match.ActualEndTime = DateTime.UtcNow;
        match.RecordedByStaffId = request.RecordedByStaffId ?? managerId;
        match.Notes = request.Notes?.Trim();
        match.UpdatedAt = DateTime.UtcNow;

        // === I1 Fix: Validate Final feasibility TRƯỚC khi mutate Elo/Swiss ===
        // Nếu match vừa ghi là round Swiss cuối → phải build Final.
        // Walk-in được vào Final (hiển thị tên với 🚶 prefix, không update Elo/Karma).
        if (!match.IsFinal
            && tournament.CurrentRound >= tournament.PreliminaryRounds
            && match.RoundNumber == tournament.PreliminaryRounds
            && !tournament.Matches.Any(m => m.IsFinal))
        {
            var activeCount = tournament.Participants
                .Count(p => p.Status == TournamentParticipantStatus.Active);
            if (activeCount < tournament.FinalistCount)
            {
                throw new ConflictException(
                    ApiErrorMessages.Tournament.FinalRequiresFourActiveParticipants(
                        activeCount, tournament.FinalistCount));
            }
        }

        // Aggregate Prestige scores + Elo delta vào TournamentParticipant totals
        await AggregateSwissScoresAsync(tournament, match);

        // Aggregate Elo changes (multi-player, Swiss round hoặc Final)
        if (!match.EloApplied)
        {
            await AggregateEloForMatchAsync(tournament, match);
            match.EloApplied = true;
        }

        // If this is the final match (Round 4), also assign FinalRank
        if (match.IsFinal)
        {
            AssignFinalRanks(tournament, match);
            // Mark all participants Finished
            foreach (var p in tournament.Participants
                .Where(p => p.Status == TournamentParticipantStatus.Active))
            {
                p.Status = TournamentParticipantStatus.Finished;
                p.UpdatedAt = DateTime.UtcNow;
            }
        }
        else if (tournament.CurrentRound >= tournament.PreliminaryRounds
            && match.RoundNumber == tournament.PreliminaryRounds
            && !tournament.Matches.Any(m => m.IsFinal))
        {
            // Just finished the last Swiss round → build Final match (idempotent: skip if already exists)
            await BuildFinalMatchAsync(tournament);
            tournament.CurrentRound = tournament.TotalRounds; // advance to Final round
        }

        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();
        return MapMatchDto(match);
    }

    // ====================================================================
    // PLAYER PERSONAL DATA (my-registrations, elo-history, leaderboard)
    // ====================================================================

    public async Task<IReadOnlyList<MyTournamentRegistrationDto>> GetMyRegistrationsAsync(Guid userId, string? status = null)
    {
        TournamentStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TournamentStatus>(status, ignoreCase: true, out var parsed))
            {
                throw new BadRequestException(
                    ApiErrorMessages.Controller.InvalidQueryParameter(nameof(status), "TournamentStatus enum"));
            }
            statusFilter = parsed;
        }

        var participations = await _tournamentRepository.GetParticipantsByUserAsync(userId);

        var results = participations
            .Where(p => p.Tournament != null)
            .Where(p => statusFilter == null || p.Tournament!.Status == statusFilter)
            .Select(p => new MyTournamentRegistrationDto
            {
                TournamentId = p.TournamentId,
                Title = p.Tournament!.Title,
                CafeId = p.Tournament!.CafeId,
                CafeName = p.Tournament.Cafe?.Name ?? string.Empty,
                StartTime = p.Tournament.StartTime,
                TournamentStatus = p.Tournament.Status,
                ParticipantId = p.Id,
                ParticipantStatus = p.Status,
                IsWalkIn = p.IsWalkIn,
                WalkInDisplayName = p.WalkInDisplayName,
                RegisteredAt = p.RegisteredAt,
                CheckedInAt = p.CheckedInAt,
                SwissScore = (decimal)(p.SwissWins + (p.SwissDraws * 0.5)),
                SwissWins = p.SwissWins,
                SwissDraws = p.SwissDraws,
                SwissLosses = p.SwissLosses,
                FinalRank = p.FinalRank,
                InitialElo = p.InitialElo,
                FinalElo = p.FinalElo,
                EloDelta = p.EloDelta
            })
            .OrderByDescending(r => r.StartTime)
            .ToList();

        return results;
    }

    public async Task<EloHistoryResponseDto> GetEloHistoryAsync(Guid userId)
    {
        var user = await _userProfileRepository.GetByIdWithProfileAsync(userId);
        if (user?.Profile == null)
        {
            throw new NotFoundException($"Không tìm thấy profile của user {userId}.");
        }

        var participations = await _tournamentRepository.GetParticipantsByUserAsync(userId);
        var entries = participations
            .Where(p => p.Tournament != null)
            .OrderBy(p => p.Tournament!.StartTime)
            .Select(p => new EloHistoryEntryDto
            {
                TournamentId = p.TournamentId,
                TournamentTitle = p.Tournament!.Title,
                GameTemplateName = p.Tournament.GameTemplate?.Name ?? string.Empty,
                TournamentDate = p.Tournament.StartTime,
                EloBefore = p.InitialElo,
                EloAfter = p.FinalElo,
                EloDelta = p.EloDelta,
                FinalRank = p.FinalRank,
                TournamentStatus = p.Tournament.Status.ToString()
            })
            .ToList();

        return new EloHistoryResponseDto
        {
            UserId = userId,
            Username = user.Username,
            CurrentElo = user.Profile.GlobalElo,
            History = entries
        };
    }

    public async Task<LeaderboardResponseDto> GetLeaderboardAsync(int topCount = 100, Guid? gameTemplateId = null)
    {
        if (topCount is < 1 or > 500) topCount = 100;

        var profiles = await _tournamentRepository.GetTopEloProfilesAsync(topCount, gameTemplateId);
        var userIds = profiles.Select(p => p.UserId).ToList();

        // Bulk fetch stats cho tất cả userIds trong 1 query thay vì N+1.
        var stats = await _tournamentRepository.GetAggregatedTournamentStatsAsync(userIds, gameTemplateId);

        var entries = profiles.Select((p, idx) => new LeaderboardEntryDto
        {
            Rank = idx + 1,
            UserId = p.UserId,
            Username = p.User?.Username ?? string.Empty,
            AvatarUrl = p.User?.Profile?.AvatarUrl,
            GlobalElo = p.GlobalElo,
            TournamentsPlayed = stats.TryGetValue(p.UserId, out var s) ? s.TournamentsPlayed : 0,
            ChampionsCount = stats.TryGetValue(p.UserId, out var s2) ? s2.Champions : 0
        }).ToList();

        return new LeaderboardResponseDto
        {
            TotalPlayers = entries.Count,
            Entries = entries
        };
    }

    public async Task<IReadOnlyList<TournamentResponseDto>> GetCafeActiveTournamentsAsync(Guid cafeId, Guid managerId)
    {
        // Đảm bảo manager owns cafe trước khi trả data.
        await EnsureManagerOwnsCafeAsync(managerId, cafeId);

        var tournaments = await _tournamentRepository.GetActiveByCafeAsync(cafeId);
        var responses = new List<TournamentResponseDto>();
        foreach (var t in tournaments)
        {
            responses.Add(await BuildResponseAsync(t, managerId));
        }
        return responses;
    }

    public async Task<TournamentMatchResponseDto> UpdateMatchResultAsync(
        Guid managerId, Guid matchId, UpdateMatchResultRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CorrectionReason))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.CorrectionReasonRequired);
        }

        var match = await _tournamentRepository.GetMatchByIdAsync(matchId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.MatchNotFound(matchId));

        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(match.TournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(match.TournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (match.Status != TournamentMatchStatus.Completed)
        {
            throw new ConflictException(
                $"Chỉ có thể sửa kết quả bàn đã Completed. Hiện tại: [{match.Status}].");
        }

        if (match.IsFinal)
        {
            throw new ConflictException(
                "Không thể sửa kết quả bàn chung kết. FinalRank + Karma + Elo đã sync xong.");
        }

        // Chỉ cho sửa khi CHƯA build round kế tiếp — tránh revert Swiss score.
        var nextRound = match.RoundNumber + 1;
        if (tournament.Matches.Any(m => m.RoundNumber == nextRound && m.RoundNumber <= tournament.PreliminaryRounds))
        {
            throw new ConflictException(
                $"Đã có matches của Round {nextRound}. Không thể sửa kết quả Round {match.RoundNumber}.");
        }

        // Validate + apply giống RecordMatchResultAsync nhưng với correctionReason
        var playerSlots = new[] { match.Player1Id, match.Player2Id, match.Player3Id, match.Player4Id }
            .Where(p => p.HasValue).Select(p => p!.Value).ToList();

        if (!playerSlots.Contains(request.WinnerUserId ?? Guid.Empty))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.WinnerMustBePlayer(matchId));
        }

        if (request.Results.Count != playerSlots.Count)
        {
            throw new BadRequestException(
                $"Kết quả phải bao gồm đủ {playerSlots.Count} người chơi (hiện tại có {request.Results.Count}).");
        }

        // === Revert Swiss score cũ của 4 players ===
        await RevertMatchSwissScoresAsync(tournament, match);

        // === Apply Swiss score mới ===
        var maxScorePerPlayer = tournament.GameTemplate?.TournamentMaxScorePerPlayer ?? 15;
        foreach (var r in request.Results)
        {
            if (!playerSlots.Contains(r.UserId ?? Guid.Empty))
            {
                throw new BadRequestException(ApiErrorMessages.Tournament.PlayerNotInMatch(matchId, r.UserId ?? Guid.Empty));
            }
            if (r.Score > maxScorePerPlayer)
            {
                throw new BadRequestException(
                    $"Điểm của player {r.UserId} ({r.Score}) vượt quá giới hạn {maxScorePerPlayer}.");
            }

            if (match.Player1Id == r.UserId)
            {
                match.Player1Score = r.Score;
                match.Player1CardsBought = r.CardsBought;
            }
            else if (match.Player2Id == r.UserId)
            {
                match.Player2Score = r.Score;
                match.Player2CardsBought = r.CardsBought;
            }
            else if (match.Player3Id == r.UserId)
            {
                match.Player3Score = r.Score;
                match.Player3CardsBought = r.CardsBought;
            }
            else if (match.Player4Id == r.UserId)
            {
                match.Player4Score = r.Score;
                match.Player4CardsBought = r.CardsBought;
            }
        }

        // === Revert Elo + apply lại ===
        await RevertMatchEloAsync(tournament, match);
        match.EloApplied = false;
        match.WinnerPlayerId = request.WinnerUserId;
        match.Notes = $"[Corrected by manager {managerId}] {request.CorrectionReason.Trim()}";
        match.UpdatedAt = DateTime.UtcNow;

        await AggregateSwissScoresAsync(tournament, match);
        await AggregateEloForMatchAsync(tournament, match);
        match.EloApplied = true;

        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        _logger.LogWarning(
            "[TournamentMatchCorrected] TournamentId={TournamentId}, MatchId={MatchId}, ManagerId={ManagerId}, Reason={Reason}",
            tournament.Id, match.Id, managerId, request.CorrectionReason);

        return MapMatchDto(match);
    }

    private async Task RevertMatchSwissScoresAsync(Tournament tournament, TournamentMatchBracket match)
    {
        // PlayerNId = TournamentParticipant.Id.
        // Trừ lại Swiss score cũ (PrestigePoints + CardsBought) cho tất cả players (kể cả walk-in).
        var slotIds = new[]
        {
            match.Player1Id, match.Player2Id,
            match.Player3Id, match.Player4Id
        }.Where(id => id.HasValue).Select(id => id!.Value).ToList();

        foreach (var participantId in slotIds)
        {
            var participant = tournament.Participants.FirstOrDefault(p => p.Id == participantId);
            if (participant == null) continue;

            if (participant.Id == match.Player1Id)
            {
                participant.TotalPrestigePoints = Math.Max(0, participant.TotalPrestigePoints - (match.Player1Score ?? 0));
                participant.TotalCardsBought = Math.Max(0, participant.TotalCardsBought - (match.Player1CardsBought ?? 0));
            }
            else if (participant.Id == match.Player2Id)
            {
                participant.TotalPrestigePoints = Math.Max(0, participant.TotalPrestigePoints - (match.Player2Score ?? 0));
                participant.TotalCardsBought = Math.Max(0, participant.TotalCardsBought - (match.Player2CardsBought ?? 0));
            }
            else if (participant.Id == match.Player3Id)
            {
                participant.TotalPrestigePoints = Math.Max(0, participant.TotalPrestigePoints - (match.Player3Score ?? 0));
                participant.TotalCardsBought = Math.Max(0, participant.TotalCardsBought - (match.Player3CardsBought ?? 0));
            }
            else if (participant.Id == match.Player4Id)
            {
                participant.TotalPrestigePoints = Math.Max(0, participant.TotalPrestigePoints - (match.Player4Score ?? 0));
                participant.TotalCardsBought = Math.Max(0, participant.TotalCardsBought - (match.Player4CardsBought ?? 0));
            }
            participant.UpdatedAt = DateTime.UtcNow;
        }

        await Task.CompletedTask;
    }

    private async Task RevertMatchEloAsync(Tournament tournament, TournamentMatchBracket match)
    {
        // Lấy contributions đã lưu để revert chính xác từng player (chỉ registered players).
        var contributions = await _tournamentRepository.GetEloContributionsByMatchAsync(match.Id);

        foreach (var contribution in contributions)
        {
            var participant = tournament.Participants.FirstOrDefault(p => p.Id == contribution.ParticipantId);
            if (participant == null) continue;

            // Reverse Elo
            participant.FinalElo -= contribution.EloDelta;
            participant.EloDelta -= contribution.EloDelta;
            participant.UpdatedAt = DateTime.UtcNow;
        }

        // Revert Swiss counters: dựa vào WinnerPlayerId trong match
        // PlayerNId = User.Id (FK reference to Users table)
        var isDraw = !match.WinnerPlayerId.HasValue;
        var playerIds = new[]
        {
            match.Player1Id, match.Player2Id,
            match.Player3Id, match.Player4Id
        }.Where(id => id.HasValue).Select(id => id!.Value).ToList();

        var participantsInMatch = tournament.Participants
            .Where(p => p.UserId.HasValue && playerIds.Contains(p.UserId.Value))
            .ToList();

        if (isDraw)
        {
            foreach (var p in participantsInMatch)
            {
                p.SwissDraws = Math.Max(0, p.SwissDraws - 1);
            }
        }
        else
        {
            // WinnerPlayerId = User.Id, find participant by UserId
            var winner = participantsInMatch.FirstOrDefault(p => p.UserId == match.WinnerPlayerId);
            if (winner != null)
            {
                winner.SwissWins = Math.Max(0, winner.SwissWins - 1);
            }
            foreach (var p in participantsInMatch.Where(p => p.UserId != match.WinnerPlayerId))
            {
                p.SwissLosses = Math.Max(0, p.SwissLosses - 1);
            }
        }

        // Xóa contributions cũ (sẽ được tạo lại khi apply result mới)
        await _tournamentRepository.DeleteEloContributionsByMatchAsync(match.Id);
    }

    public async Task<TournamentMatchResponseDto> CancelMatchAsync(Guid managerId, Guid matchId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.CancelMatchReasonRequired);
        }

        var match = await _tournamentRepository.GetMatchByIdAsync(matchId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.MatchNotFoundById);

        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(match.TournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(match.TournamentId));

        // Cafe-level ownership check (consistent với các endpoint khác trong service).
        // Cho phép cả Co-Manager của cùng cafe, không chỉ creator.
        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (match.Status == TournamentMatchStatus.Completed)
        {
            throw new ConflictException(
                "Không thể hủy ván đấu đã hoàn thành. Vui lòng sửa kết quả trực tiếp trên app.");
        }

        match.Status = TournamentMatchStatus.Cancelled;
        match.Notes = $"[Cancelled by manager {managerId}] {reason.Trim()}";
        match.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.UpdateMatchAsync(match);
        await _tournamentRepository.SaveChangesAsync();

        return MapMatchDto(match);
    }

// ====================================================================
// BACKGROUND JOBS
// ====================================================================

public async Task<TournamentResponseDto> AdvanceRoundAsync(Guid managerId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (tournament.Status != TournamentStatus.OnGoing)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotAdvanceRoundNotOnGoing(tournamentId));
        }

        var currentRound = tournament.CurrentRound;
        if (currentRound <= 0)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotAdvanceRoundAlreadyCompleted(tournamentId));
        }

        // Round hiện tại phải đã Completed toàn bộ matches (mọi status phải là Completed hoặc Cancelled)
        var currentRoundMatches = tournament.Matches
            .Where(m => m.RoundNumber == currentRound)
            .ToList();

        if (currentRoundMatches.Count == 0)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotAdvanceRoundAlreadyCompleted(tournamentId));
        }

        var unfinishedMatches = currentRoundMatches
            .Where(m => m.Status != TournamentMatchStatus.Completed
                && m.Status != TournamentMatchStatus.Cancelled)
            .ToList();

        if (unfinishedMatches.Count > 0)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotAdvanceRoundCurrentNotFinished(currentRound));
        }

        var nextRound = currentRound + 1;
        // Sort theo Swiss score giảm dần (Swiss pairing: người cùng điểm gặp nhau)
        // Tiebreaker: CheckedInAt sớm hơn (FIFO trong nhóm cùng điểm)
        var activeParticipants = tournament.Participants
            .Where(p => p.Status == TournamentParticipantStatus.Active)
            .OrderByDescending(p => (p.SwissWins * 1.0) + (p.SwissDraws * 0.5))
            .ThenByDescending(p => p.TotalPrestigePoints)
            .ThenBy(p => p.CheckedInAt ?? p.RegisteredAt)
            .ToList();

        if (nextRound <= tournament.PreliminaryRounds)
        {
            // Swiss round kế tiếp — build từ active participants
            if (activeParticipants.Count < 2)
            {
                throw new ConflictException(
                    $"Không đủ người chơi Active để build Round {nextRound} (cần ≥ 2, hiện có {activeParticipants.Count}). " +
                    "Hãy dùng Manual Pairing hoặc hủy giải.");
            }

            var newMatches = BuildRoundMatches(tournament, nextRound, activeParticipants);
            await _tournamentRepository.AddMatchesAsync(newMatches);
        }
        else if (nextRound == tournament.TotalRounds)
        {
            // Build bàn chung kết — top 4 theo Swiss score (auto) HOẶC manual
            var finalExists = tournament.Matches.Any(m => m.IsFinal);
            if (finalExists)
            {
                throw new ConflictException(
                    ApiErrorMessages.Tournament.CannotAdvanceRoundFinalAlreadyBuilt(tournamentId));
            }

            // Manual Final: build từ pairings; Auto: gọi BuildFinalMatchAsync
            var finalJson = tournament.FinalPairingsJson;
            if (!string.IsNullOrWhiteSpace(finalJson))
            {
                var pairings = ParseManualJson(finalJson);
                if (pairings.Count != 1 || pairings[0].PlayerIds.Count != tournament.FinalistCount)
                {
                    throw new BadRequestException(
                        $"Final pairings không hợp lệ. Cần đúng 1 bàn với {tournament.FinalistCount} người.");
                }

                // Walk-in được tham gia Final nếu nằm trong manual pairings.
                // Hiển thị tên với 🚶 prefix, không update Elo/Karma.
                var finalMatch = new TournamentMatchBracket
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournament.Id,
                    RoundNumber = tournament.TotalRounds,
                    MatchNumber = 1,
                    IsFinal = true,
                    Player1Id = pairings[0].PlayerIds.ElementAtOrDefault(0),
                    Player2Id = pairings[0].PlayerIds.ElementAtOrDefault(1),
                    Player3Id = pairings[0].PlayerIds.ElementAtOrDefault(2),
                    Player4Id = pairings[0].PlayerIds.ElementAtOrDefault(3),
                    Status = TournamentMatchStatus.Scheduled,
                    CreatedAt = DateTime.UtcNow
                };
                await _tournamentRepository.AddMatchAsync(finalMatch);
            }
            else
            {
                // Auto Final: build top N theo Swiss score ngay tại AdvanceRound (không đợi RecordMatchResultAsync)
                await BuildFinalMatchAsync(tournament);
            }
        }
        else
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotAdvanceRoundAlreadyCompleted(tournamentId));
        }

        tournament.CurrentRound = nextRound;
        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        return await BuildResponseAsync(tournamentId, null);
    }

    public async Task<int> AutoCloseExpiredRegistrationsAsync(DateTime cutoffTime)
    {
        var tournaments = await _tournamentRepository.GetUpcomingForClosingAsync(cutoffTime);
        var count = 0;
        foreach (var t in tournaments)
        {
            // Tournament chưa có ai đăng ký + đã hết hạn → bỏ qua, không chuyển sang Closed.
            // Nếu không skip, tournament sẽ kẹt ở RegistrationOpen mãi mãi cho tới khi manager cancel.
            // Manager tự xử lý 0-participant tournament (cancel thủ công).
            // F12: Logic nhất quán giữa 2 overloads (có CT và không CT).
            if (!HasActiveParticipants(t)) continue;

            t.Status = TournamentStatus.RegistrationClosed;
            t.UpdatedAt = DateTime.UtcNow;
            count++;
        }
        if (count > 0)
        {
            await _tournamentRepository.SaveChangesAsync();
        }
        return count;
    }

    /// <summary>
    /// Cancellable variant — pass stoppingToken xuống DB calls.
    /// Background job nên dùng overload này để shutdown nhanh khi app tắt.
    /// </summary>
    public async Task<int> AutoCloseExpiredRegistrationsAsync(DateTime cutoffTime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tournaments = await _tournamentRepository.GetUpcomingForClosingAsync(cutoffTime);
        var count = 0;
        foreach (var t in tournaments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // F12: Thêm guard giống overload không CT để nhất quán logic.
            if (!HasActiveParticipants(t)) continue;
            t.Status = TournamentStatus.RegistrationClosed;
            t.UpdatedAt = DateTime.UtcNow;
            count++;
        }
        if (count > 0)
        {
            await _tournamentRepository.SaveChangesAsync();
        }
        return count;
    }

    private static bool HasActiveParticipants(Tournament t) =>
        t.Participants?.Any(p =>
            p.Status == TournamentParticipantStatus.Registered
            || p.Status == TournamentParticipantStatus.CheckedIn
            || p.Status == TournamentParticipantStatus.Active) ?? false;

    // ====================================================================
    // AUTO REMINDER & NO-SHOW DETECTION
    // ====================================================================

    /// <summary>
    /// Gửi reminder notification cho participants chưa check-in của các giải đấu sắp bắt đầu.
    /// Reminder schedule: T-30, T-15, T-5 phút.
    /// </summary>
    public async Task<int> SendTournamentRemindersAsync(DateTime now, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var upcoming = await _tournamentRepository.GetTournamentsStartingSoonAsync(now, ct);
        var sentCount = 0;

        foreach (var tournament in upcoming)
        {
            ct.ThrowIfCancellationRequested();

            var minutesUntilStart = (int)(tournament.StartTime - now).TotalMinutes;

            // Xác định reminder type dựa trên thời gian còn lại
            string? reminderType = minutesUntilStart switch
            {
                <= 30 and > 15 => "30min",
                <= 15 and > 5 => "15min",
                <= 5 => "5min",
                _ => null
            };

            if (reminderType == null) continue;

            // Lấy participants chưa check-in (chỉ gửi reminder cho user online)
            var notCheckedIn = tournament.Participants
                .Where(p => p.UserId.HasValue &&
                    p.Status == TournamentParticipantStatus.Registered)
                .ToList();

            foreach (var participant in notCheckedIn)
            {
                var message = reminderType switch
                {
                    "30min" => ApiErrorMessages.Tournament.Reminder30Minutes(
                        tournament.Title, tournament.StartTime, tournament.Cafe?.Name ?? ""),
                    "15min" => ApiErrorMessages.Tournament.Reminder15Minutes(
                        tournament.Title, tournament.StartTime, tournament.Cafe?.Name ?? ""),
                    "5min" => ApiErrorMessages.Tournament.Reminder5Minutes(
                        tournament.Title, tournament.StartTime, tournament.Cafe?.Name ?? ""),
                    _ => null
                };

                if (message != null)
                {
                    // TODO: Khi IPushNotificationService sẵn sàng, hook vào đây
                    // await _pushNotificationService.SendAsync(participant.UserId.Value, message);
                    _logger.LogInformation(
                        "[TournamentReminder] Would send '{ReminderType}' reminder to User {UserId} for Tournament {TournamentId}: {Message}",
                        reminderType, participant.UserId, tournament.Id, message);
                    sentCount++;
                }
            }
        }

        return sentCount;
    }

    /// <summary>
    /// Tự động đánh dấu no-show cho participants đã đăng ký nhưng không check-in
    /// khi giải đấu bắt đầu (OnGoing + CurrentRound = 1).
    /// Áp dụng Karma penalty nếu có cấu hình.
    /// </summary>
    public async Task<NoShowDetectionResult> AutoMarkNoShowsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var result = new NoShowDetectionResult();
        var noShowTournaments = await _tournamentRepository.GetTournamentsJustStartedAsync(ct);

        foreach (var tournament in noShowTournaments)
        {
            ct.ThrowIfCancellationRequested();

            result.TournamentId = tournament.Id;
            var markedIds = new List<Guid>();

            // Tìm participants đã đăng ký nhưng chưa check-in và chưa Active (chưa chơi round nào)
            var noShowParticipants = tournament.Participants
                .Where(p => p.UserId.HasValue &&
                    p.Status == TournamentParticipantStatus.Registered)
                .ToList();

            foreach (var participant in noShowParticipants)
            {
                ct.ThrowIfCancellationRequested();

                // Gọi MarkNoShowAsync để reuse logic có sẵn
                await MarkNoShowAsync(tournament.CreatedByManagerId, tournament.Id, participant.Id);
                markedIds.Add(participant.Id);

                result.TotalKarmaPenalty += tournament.NoShowKarmaPenalty;

                // Gửi notification cho user biết họ bị đánh dấu no-show
                var message = ApiErrorMessages.Tournament.NoShowMarked(
                    tournament.Title, tournament.NoShowKarmaPenalty);
                // TODO: Khi IPushNotificationService sẵn sàng
                // await _pushNotificationService.SendAsync(participant.UserId.Value, message);
                _logger.LogInformation(
                    "[TournamentNoShow] User {UserId} marked no-show for Tournament {TournamentId}. Karma penalty: {Penalty}",
                    participant.UserId, tournament.Id, tournament.NoShowKarmaPenalty);
            }

            result.MarkedParticipantIds = markedIds;
            result.TotalMarked = markedIds.Count;
        }

        return result;
    }

    // ====================================================================
    // HELPERS
    // ====================================================================

    private async Task EnsureManagerOwnsCafeAsync(Guid managerId, Guid cafeId)
    {
        var can = await _cafePosRepository.CanOperateCafeAsync(
            cafeId, managerId, UserRole.Manager.ToString());
        if (!can)
        {
            throw new ForbiddenException(ApiErrorMessages.Tournament.ManagerForbidden(cafeId));
        }
    }

    /// <summary>
    /// Resolve GameTemplateId cho tournament creation.
    /// Chỉ chấp nhận game có <see cref="GameTemplate.IsTournamentSupported"/> = true
    /// (config-driven thay cho hardcode tên "Splendor").
    /// </summary>
    private async Task<Guid> ResolveTournamentGameTemplateIdAsync(Guid? requestedId)
    {
        if (requestedId.HasValue)
        {
            var requested = await _gameTemplateRepository.GetByIdAsync(requestedId.Value);
            if (requested == null || !requested.IsActive)
            {
                throw new NotFoundException(ApiErrorMessages.BoardGame.MasterNotFound(requestedId.Value));
            }
            if (!requested.IsTournamentSupported)
            {
                throw new BadRequestException(
                    string.Format(ApiErrorMessages.Tournament.SplendorRequired, requested.Name));
            }
            return requested.Id;
        }

        // Fallback: chọn game được flag TournamentSupported đầu tiên (Splendor hiện tại).
        // Có thể thay bằng danh sách cho phép manager chọn game trong tương lai.
        var candidates = await _gameTemplateRepository.GetByNameAsync("Splendor");
        if (candidates == null || !candidates.IsActive || !candidates.IsTournamentSupported)
        {
            throw new ConfigurationMissingException(ApiErrorMessages.Tournament.SplendorGameNotFound);
        }
        return candidates.Id;
    }

    private static void ValidateCreateRequest(CreateTournamentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length < 5 || request.Title.Length > 200)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.TitleRequired);
        }
        if (request.StartTime <= DateTime.UtcNow)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.StartTimeMustBeFuture);
        }
        EnsureMaxParticipantsMultipleOf4(request.MaxParticipants);
    }

    private static void EnsureMaxParticipantsMultipleOf4(int max)
    {
        if (max < 4 || max > 32 || max % 4 != 0)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.MaxParticipantsMustBeMultipleOf4);
        }
    }

    private async Task<int> GetUserKarmaAsync(Guid userId)
    {
        var user = await _userProfileRepository.GetByIdWithProfileAsync(userId);
        return user?.Profile?.KarmaPoints ?? 100;
    }

    private async Task<int> GetUserEloAsync(Guid userId)
    {
        var user = await _userProfileRepository.GetByIdWithProfileAsync(userId);
        var elo = user?.Profile?.GlobalElo ?? 0;
        return elo <= 0 ? EloRatingHelper.DefaultRating : elo;
    }

    private static List<TournamentMatchBracket> BuildSwissRound(
        IReadOnlyList<TournamentParticipant> participants,
        Guid tournamentId,
        int roundNumber,
        IReadOnlyList<TournamentMatchBracket>? previousMatches = null)
    {
        var matches = new List<TournamentMatchBracket>();

        // Dùng Adaptive Balanced Swiss algorithm (SwissPairingHelper).
        // - Round 1: Snake draft by Elo (top vs bottom).
        // - Round 2+: Constraint solver với anti-repeat + Elo balance.
        var tables = SwissPairingHelper.BuildBalancedPairings(
            participants,
            roundNumber,
            previousMatches ?? new List<TournamentMatchBracket>());

        var matchNumber = 1;
        foreach (var table in tables)
        {
            // PlayerNId = User.Id (FK reference to Users table).
            // Walk-in có UserId = null, không thể tạo match hợp lệ → skip.
            if (table.Any(p => p.UserId == null))
            {
                continue; // Skip tables with walk-ins for now (manual pairing required)
            }

            var match = new TournamentMatchBracket
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                RoundNumber = roundNumber,
                MatchNumber = matchNumber++,
                IsFinal = false,
                Player1Id = table.Count > 0 ? table[0].UserId : null,
                Player2Id = table.Count > 1 ? table[1].UserId : null,
                Player3Id = table.Count > 2 ? table[2].UserId : null,
                Player4Id = table.Count > 3 ? table[3].UserId : null,
                Status = TournamentMatchStatus.Scheduled,
                CreatedAt = DateTime.UtcNow
            };
            matches.Add(match);
        }

        return matches;
    }

    private async Task AggregateSwissScoresAsync(Tournament tournament, TournamentMatchBracket match)
    {
        // PlayerNId = User.Id (FK reference to Users table).
        var playerIds = new[]
        {
            match.Player1Id, match.Player2Id,
            match.Player3Id, match.Player4Id
        }.Where(id => id.HasValue).Select(id => id!.Value).ToList();

        foreach (var userId in playerIds)
        {
            // Find participant by UserId (not Participant.Id)
            var participant = tournament.Participants.FirstOrDefault(p => p.UserId == userId);
            if (participant == null) continue;

            if (userId == match.Player1Id)
            {
                participant.TotalPrestigePoints += match.Player1Score ?? 0;
                participant.TotalCardsBought += match.Player1CardsBought ?? 0;
            }
            else if (userId == match.Player2Id)
            {
                participant.TotalPrestigePoints += match.Player2Score ?? 0;
                participant.TotalCardsBought += match.Player2CardsBought ?? 0;
            }
            else if (userId == match.Player3Id)
            {
                participant.TotalPrestigePoints += match.Player3Score ?? 0;
                participant.TotalCardsBought += match.Player3CardsBought ?? 0;
            }
            else if (userId == match.Player4Id)
            {
                participant.TotalPrestigePoints += match.Player4Score ?? 0;
                participant.TotalCardsBought += match.Player4CardsBought ?? 0;
            }
            participant.UpdatedAt = DateTime.UtcNow;
        }

        await Task.CompletedTask;
    }

    private async Task AggregateEloForMatchAsync(Tournament tournament, TournamentMatchBracket match)
    {
        // PlayerNId = User.Id (FK reference to Users table).
        // Walk-in có UserId = null → skip Elo update.
        var playerIds = new[] { match.Player1Id, match.Player2Id, match.Player3Id, match.Player4Id }
            .Where(id => id.HasValue).Select(id => id!.Value).ToList();

        if (playerIds.Count < 2) return;

        // Chỉ registered players (UserId not null) mới có Elo
        var currentEloByUser = tournament.Participants
            .Where(p => p.UserId.HasValue && playerIds.Contains(p.UserId.Value))
            .ToDictionary(p => p.UserId!.Value, p => p.FinalElo);

        if (currentEloByUser.Count < 2) return; // Cần ≥ 2 registered players

        var configuredK = await _systemConfigurationProvider.GetIntAsync(SystemConfigKeys.EloKFactor, 32);
        match.EloKFactorUsed = configuredK;

        // Splendor 4-player: 1 winner, 3 losers. Draw semantics vẫn support cho future-proof
        var isDraw = !match.WinnerPlayerId.HasValue;
        var eloChanges = TournamentEloCalculator.CalculateMatchEloChanges(
            currentEloByUser,
            match.WinnerPlayerId,
            isDraw,
            configuredK);

        // Tất cả participants trong match (registered + walk-in)
        var participantsInMatch = tournament.Participants
            .Where(p => p.UserId.HasValue && playerIds.Contains(p.UserId.Value))
            .ToList();

        // Swiss counters: chỉ cho registered players
        if (isDraw)
        {
            foreach (var p in participantsInMatch.Where(p => p.UserId.HasValue))
            {
                p.SwissDraws += 1;
                p.UpdatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            // Tìm winner: WinnerPlayerId = User.Id
            var winner = participantsInMatch.FirstOrDefault(p => p.UserId == match.WinnerPlayerId);
            var losers = participantsInMatch.Where(p => p.UserId != match.WinnerPlayerId).ToList();
            if (winner != null)
            {
                TournamentEloCalculator.UpdateSwissCounters(match, winner, losers);
            }
        }

        // Elo changes: chỉ cho registered players (UserId not null)
        var registeredInMatch = participantsInMatch.Where(p => p.UserId.HasValue).ToList();
        TournamentEloCalculator.ApplyEloChanges(registeredInMatch, eloChanges, isFinal: match.IsFinal);

        // Lưu Elo contributions cho registered players (để revert chính xác)
        foreach (var p in registeredInMatch)
        {
            if (eloChanges.TryGetValue(p.UserId!.Value, out var delta))
            {
                await _tournamentRepository.AddEloContributionAsync(new TournamentMatchEloContribution
                {
                    Id = Guid.NewGuid(),
                    MatchId = match.Id,
                    ParticipantId = p.Id,
                    EloDelta = delta,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
    }

    private async Task BuildFinalMatchAsync(Tournament tournament)
    {
        // Walk-in được vào Final (hiển thị tên với 🚶 prefix, không update Elo/Karma).
        // BR-13 analogy: walk-in không có UserId → không nhận Elo/Karma rewards.
        //
        // PlayerNId = TournamentParticipant.Id (không phải UserId).
        // Walk-in có Participant.Id nhưng UserId = null.
        // Dùng ParticipantId để walk-in có thể tham gia Final.
        var top4 = TournamentEloCalculator.RankBySwiss(
            tournament.Participants.Where(p => p.Status == TournamentParticipantStatus.Active),
            tournament.FinalistCount).ToList();

        if (top4.Count < tournament.FinalistCount)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.FinalRequiresFourActiveParticipants(
                    top4.Count, tournament.FinalistCount));
        }

        var finalMatch = new TournamentMatchBracket
        {
            Id = Guid.NewGuid(),
            TournamentId = tournament.Id,
            RoundNumber = tournament.TotalRounds,
            MatchNumber = 1,
            IsFinal = true,
            // Dùng Participant.Id để walk-in có thể tham gia Final
            Player1Id = top4.ElementAtOrDefault(0)?.Id,
            Player2Id = top4.ElementAtOrDefault(1)?.Id,
            Player3Id = top4.ElementAtOrDefault(2)?.Id,
            Player4Id = top4.ElementAtOrDefault(3)?.Id,
            Status = TournamentMatchStatus.Scheduled,
            CreatedAt = DateTime.UtcNow
        };

        await _tournamentRepository.AddMatchAsync(finalMatch);
    }

    private void AssignFinalRanks(Tournament tournament, TournamentMatchBracket finalMatch)
    {
        // Walk-in được xếp rank trong Final (hiển thị với 🚶 prefix trong response).
        // BR-13 analogy: walk-in không nhận Elo/Karma rewards (UserId = null).
        //
        // Lưu ý: PlayerNId trong match slot = TournamentParticipant.Id (không phải User.Id).
        // Finalists: PlayerNId = User.Id (FK reference to Users table)
        var playerIds = new[]
        {
            finalMatch.Player1Id, finalMatch.Player2Id,
            finalMatch.Player3Id, finalMatch.Player4Id
        }.Where(id => id.HasValue).Select(id => id!.Value).ToList();

        // Tất cả finalists (registered + walk-in) by UserId
        var allFinalists = tournament.Participants
            .Where(p => p.UserId.HasValue && playerIds.Contains(p.UserId.Value))
            .ToList();

        if (allFinalists.Count == 0) return;

        // Winner: WinnerPlayerId = User.Id
        var winner = allFinalists.FirstOrDefault(p => p.UserId == finalMatch.WinnerPlayerId);
        if (winner != null)
        {
            winner.FinalRank = 1;
        }
        else if (finalMatch.WinnerPlayerId == null)
        {
            _logger.LogWarning(
                "[TournamentFinalRankFallback] TournamentId={TournamentId}, MatchId={MatchId}: " +
                "WinnerPlayerId is null. Fallback to PrestigePoints ranking.",
                tournament.Id, finalMatch.Id);

            // Fallback: rank tất cả finalists theo PrestigePoints
            var ranked = allFinalists
                .OrderByDescending(p => p.TotalPrestigePoints)
                .ThenBy(p => p.TotalCardsBought)
                .ToList();
            for (var i = 0; i < ranked.Count; i++)
            {
                ranked[i].FinalRank = i + 1;
            }
            return;
        }

        // Losers: tất cả finalists trừ winner, rank theo PrestigePoints
        var losers = allFinalists
            .Where(p => p.UserId != finalMatch.WinnerPlayerId)
            .OrderByDescending(p => p.TotalPrestigePoints)
            .ThenBy(p => p.TotalCardsBought)
            .ToList();

        for (var i = 0; i < losers.Count; i++)
        {
            losers[i].FinalRank = i + 2;
        }
    }

    private async Task ApplyFinalKarmaBonusesAsync(Tournament tournament)
    {
        var performer = tournament.CreatedByManagerId;
        var winner = tournament.Participants.FirstOrDefault(p => p.FinalRank == 1);

        // BR-13/14 mirror + BR-12 invariant: walk-in không có UserId → không nhận Karma bonus.
        // Respect FinalistCount config: chỉ thưởng cho Top FinalistCount (không hardcode 4).
        if (winner != null && !winner.IsWalkIn && winner.UserId.HasValue && tournament.WinnerKarmaBonus > 0)
        {
            await ApplyKarmaDeltaAsync(winner.UserId.Value, tournament.WinnerKarmaBonus,
                "Giành vô địch tournament Splendor", tournament.Id, performer);
        }

        var finalists = tournament.Participants
            .Where(p => p.FinalRank.HasValue
                && p.FinalRank > 1
                && p.FinalRank <= tournament.FinalistCount)
            .ToList();

        foreach (var p in finalists)
        {
            var bonus = TournamentKarmaPolicy.GetFinalistBonus(p.FinalRank!.Value, tournament.FinalistCount);
            if (bonus > 0 && !p.IsWalkIn && p.UserId.HasValue)
            {
                await ApplyKarmaDeltaAsync(p.UserId.Value, bonus,
                    $"Top {p.FinalRank} tournament Splendor", tournament.Id, performer);
            }
        }
    }

    /// <summary>
    /// Sync FinalElo từ mỗi TournamentParticipant về UserProfile.GlobalElo.
    /// Winner nhận thêm WinnerEloBonus (mặc định +20 elo bonus).
    /// Chỉ chạy khi Tournament.Status = Completed.
    /// </summary>
    private async Task SyncFinalEloToProfilesAsync(Tournament tournament)
    {
        // Bonus winner sau tournament Completed. Chỉ áp dụng khi FinalRank = 1.
        // Giá trị +20 ~ bằng 1 Swiss win thắng bình thường (delta +12~+20 tuỳ split rating)
        // → winner bonus không lấn át phần Elo tích lũy từ các ván Swiss đã chơi.
        // Có thể promote thành Tournament field nếu sau này cần config per-tournament.
        const int WinnerEloBonus = 20;

        foreach (var participant in tournament.Participants
            .Where(p => p.Status == TournamentParticipantStatus.Finished
                || p.FinalRank.HasValue))
        {
            // BR-13/14 mirror: walk-in không có UserId, không sync Elo về profile
            // (không có profile để đồng bộ + không có trách nhiệm tài sản cá nhân).
            if (participant.IsWalkIn || participant.UserId == null) continue;

            var profile = await _userProfileRepository.GetProfileByUserIdAsync(participant.UserId.Value);
            if (profile == null) continue;

            var totalDelta = TournamentEloCalculator.SyncToUserProfile(
                profile, participant, WinnerEloBonus);

            // Update participant.FinalElo = profile.GlobalElo (sau khi cộng bonus)
            participant.FinalElo = profile.GlobalElo;
            participant.EloDelta = totalDelta;
            participant.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task ApplyKarmaDeltaAsync(Guid userId, int delta, string reason, Guid tournamentId, Guid performedByStaffId)
    {
        if (delta == 0) return;

        var profile = await _userProfileRepository.GetProfileByUserIdAsync(userId);
        if (profile == null) return;

        var before = profile.KarmaPoints;
        var after = TournamentKarmaPolicy.ClampKarma(before + delta);
        var actualDelta = after - before;

        profile.KarmaPoints = after;
        profile.UpdatedAt = DateTime.UtcNow;

        await _karmaRatingRepository.AddKarmaLogAsync(new KarmaLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ViolationCategory = KarmaViolationCategory.None,
            Source = KarmaLogSource.TournamentReward,
            KarmaPointsChange = actualDelta,
            KarmaBefore = before,
            KarmaAfter = after,
            Reason = $"[Tournament {tournamentId}] {reason}",
            RelatedLobbyId = null,
            PerformedByUserId = performedByStaffId,
            IsAdminAdjustment = false,
            CreatedAt = DateTime.UtcNow
        });

        await _karmaRatingRepository.SaveChangesAsync();
    }

    private async Task<TournamentResponseDto> BuildResponseAsync(Guid tournamentId, Guid? currentUserId)
    {
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));
        return await BuildResponseAsync(tournament, currentUserId);
    }

    private async Task<TournamentResponseDto> BuildResponseAsync(Tournament tournament, Guid? currentUserId)
    {
        var game = await _gameTemplateRepository.GetByIdAsync(tournament.GameTemplateId);

        var dto = new TournamentResponseDto
        {
            Id = tournament.Id,
            CafeId = tournament.CafeId,
            CafeName = tournament.Cafe?.Name ?? string.Empty,
            CreatedByManagerId = tournament.CreatedByManagerId,
            Title = tournament.Title,
            Description = tournament.Description,
            GameTemplateId = tournament.GameTemplateId,
            GameName = game?.Name ?? "Splendor",
            StartTime = tournament.StartTime,
            RegistrationDeadline = tournament.RegistrationDeadline,
            RoundDurationMinutes = tournament.RoundDurationMinutes,
            MinParticipants = tournament.MinParticipants,
            MaxParticipants = tournament.MaxParticipants,
            EntryFee = tournament.EntryFee,
            TotalRounds = tournament.TotalRounds,
            PreliminaryRounds = tournament.PreliminaryRounds,
            FinalistCount = tournament.FinalistCount,
            CurrentRound = tournament.CurrentRound,
            StartedAt = tournament.StartedAt,
            MinKarmaRequirement = tournament.MinKarmaRequirement,
            WinnerKarmaBonus = tournament.WinnerKarmaBonus,
            FinalistKarmaBonus = tournament.FinalistKarmaBonus,
            NoShowKarmaPenalty = tournament.NoShowKarmaPenalty,
            CancellationReason = tournament.CancellationReason,
            CancelledAt = tournament.CancelledAt,
            Status = tournament.Status,
            // RegisteredCount: player đã đăng ký nhưng chưa check-in.
            // Nếu tournament đã check-in xong, count = 0.
            // Nếu tournament bị cancel trước check-in, player vẫn ở Registered.
            RegisteredCount = tournament.Participants?
                .Count(p => p.Status == TournamentParticipantStatus.Registered) ?? 0,
            // CheckedInCount = player đã có mặt tại quán nhưng tournament chưa kết thúc.
            // Loại trừ Finished (đã hoàn thành) — đếm nhầm sẽ làm manager tưởng còn check-in thêm được.
            CheckedInCount = tournament.Participants?
                .Count(p => p.Status == TournamentParticipantStatus.CheckedIn
                    || p.Status == TournamentParticipantStatus.Active) ?? 0,
            CreatedAt = tournament.CreatedAt,
            UpdatedAt = tournament.UpdatedAt,
            PairingMode = tournament.PairingMode,
            ManualPairings = new ManualPairingsSummaryDto
            {
                Round1Set = !string.IsNullOrWhiteSpace(tournament.Round1PairingsJson),
                Round2Set = !string.IsNullOrWhiteSpace(tournament.Round2PairingsJson),
                Round3Set = !string.IsNullOrWhiteSpace(tournament.Round3PairingsJson),
                FinalSet = !string.IsNullOrWhiteSpace(tournament.FinalPairingsJson)
            }
        };

        if (currentUserId.HasValue && tournament.Participants != null)
        {
            var me = tournament.Participants.FirstOrDefault(p => p.UserId == currentUserId.Value);
            if (me != null)
            {
                dto.CurrentUserRegistered = true;
                dto.CurrentUserParticipantStatus = me.Status;
            }
            else
            {
                dto.CurrentUserRegistered = false;
            }
        }

        return dto;
    }

    private static TournamentParticipantResponseDto MapParticipantDto(TournamentParticipant p)
    {
        return new TournamentParticipantResponseDto
        {
            Id = p.Id,
            TournamentId = p.TournamentId,
            UserId = p.UserId,
            Username = p.User?.Username,
            AvatarUrl = p.User?.Profile?.AvatarUrl,
            WalkInDisplayName = p.WalkInDisplayName,
            WalkInPhoneNumber = p.WalkInPhoneNumber,
            IsWalkIn = p.IsWalkIn,
            JoinedRoundNumber = p.JoinedRoundNumber,
            RegisteredAt = p.RegisteredAt,
            KarmaAtRegistration = p.KarmaAtRegistration,
            CheckedInAt = p.CheckedInAt,
            CheckedInByStaffId = p.CheckedInByStaffId,
            RegisteredByStaffId = p.RegisteredByStaffId,
            Status = p.Status,
            TotalPrestigePoints = p.TotalPrestigePoints,
            TotalCardsBought = p.TotalCardsBought,
            FinalRank = p.FinalRank,
            InitialElo = p.InitialElo,
            CurrentElo = p.FinalElo, // FinalElo = running total (FinalElo = Initial + delta)
            EloDelta = p.EloDelta,
            FinalElo = p.FinalElo,
            SwissWins = p.SwissWins,
            SwissDraws = p.SwissDraws,
            SwissLosses = p.SwissLosses,
            SwissScore = TournamentEloCalculator.CalculateSwissScore(p)
        };
    }

    private static TournamentMatchResponseDto MapMatchDto(TournamentMatchBracket m)
    {
        return new TournamentMatchResponseDto
        {
            Id = m.Id,
            TournamentId = m.TournamentId,
            RoundNumber = m.RoundNumber,
            MatchNumber = m.MatchNumber,
            IsFinal = m.IsFinal,
            Player1Id = m.Player1Id,
            Player2Id = m.Player2Id,
            Player3Id = m.Player3Id,
            Player4Id = m.Player4Id,
            Player1Score = m.Player1Score,
            Player2Score = m.Player2Score,
            Player3Score = m.Player3Score,
            Player4Score = m.Player4Score,
            Player1CardsBought = m.Player1CardsBought,
            Player2CardsBought = m.Player2CardsBought,
            Player3CardsBought = m.Player3CardsBought,
            Player4CardsBought = m.Player4CardsBought,
            WinnerPlayerId = m.WinnerPlayerId,
            Status = m.Status,
            ScheduledStartTime = m.ScheduledStartTime,
            ActualStartTime = m.ActualStartTime,
            ActualEndTime = m.ActualEndTime,
            Notes = m.Notes
        };
    }

    // ====================================================================
    // MANUAL PAIRING (Manager override Auto Swiss pairing)
    // ====================================================================

    public async Task<TournamentResponseDto> SetPairingModeAsync(Guid managerId, Guid tournamentId, TournamentPairingMode mode)
    {
        // F15 Fix: Load with matches để check round hiện tại có matches chưa.
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        // F15 Fix: Cho phép Auto → Manual khi đã OnGoing, miễn là round hiện tại chưa build matches.
        // Thực tế: manager dùng Auto cho R1-R2, muốn Manual cho R3 (matchup quan trọng cần control).
        // Nếu round hiện tại đã có matches → không cho đổi (tránh data không khớp).
        if (tournament.Status == TournamentStatus.OnGoing && mode == TournamentPairingMode.Manual)
        {
            var currentRoundHasMatches = tournament.Matches.Any(m => m.RoundNumber == tournament.CurrentRound);
            if (currentRoundHasMatches)
            {
                throw new ConflictException(
                    "Không thể chuyển sang Manual mode khi round hiện tại đã có matches. " +
                    "Hãy đợi từng round và set manual pairings cho round kế tiếp trước khi AdvanceRound.");
            }
        }

        tournament.PairingMode = mode;
        tournament.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();
        return await BuildResponseAsync(tournamentId, null);
    }

    public async Task<RoundPairingsResponseDto> PreviewPairingsAsync(Guid managerId, Guid tournamentId, int roundNumber)
    {
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        ValidateRoundNumber(roundNumber, tournament);

        // Nếu đã có manual, trả về manual hiện tại. Nếu không, build auto preview (DÙNG helper thật).
        var existingJson = GetRoundPairingsJson(tournament, roundNumber);
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            var existing = ParseManualJson(existingJson);
            return new RoundPairingsResponseDto
            {
                TournamentId = tournamentId,
                RoundNumber = roundNumber,
                Source = "Manual",
                Pairings = existing,
                Warnings = new List<string>()
            };
        }

        // Auto preview: dùng SwissPairingHelper.BuildBalancedPairings thật,
        // giống với những gì BuildRoundMatches sẽ sinh ra.
        var orderedParticipants = GetOrderedActiveParticipants(tournament, roundNumber);
        var warnings = new List<string>();

        // Round Swiss: dùng balanced pairing helper
        if (roundNumber < tournament.TotalRounds)
        {
            var previousMatches = tournament.Matches?
                .Where(m => m.RoundNumber < roundNumber)
                .ToList() ?? new List<TournamentMatchBracket>();
            var tables = SwissPairingHelper.BuildBalancedPairings(
                orderedParticipants, roundNumber, previousMatches);

            var pairings = tables.Select((table, idx) => new ManualPairingDto
            {
                MatchNumber = idx + 1,
                PlayerIds = table.Select(p => p.UserId!.Value).ToList()
            }).ToList();

            if (orderedParticipants.Count < 4)
            {
                warnings.Add($"Số người chơi ({orderedParticipants.Count}) dưới 4 — không đủ để tạo bàn Splendor hợp lệ.");
            }
            else if (orderedParticipants.Count % 4 != 0)
            {
                var remainder = orderedParticipants.Count % 4;
                warnings.Add($"Số người chơi ({orderedParticipants.Count}) không chia hết cho 4. Bàn cuối sẽ có {remainder} người — nên dùng Manual mode để sắp xếp lại.");
            }

            return new RoundPairingsResponseDto
            {
                TournamentId = tournamentId,
                RoundNumber = roundNumber,
                Source = "Auto (suggested)",
                Pairings = pairings,
                Warnings = warnings
            };
        }

        // Round Final: top 4 theo Swiss score
        var topFinalists = TournamentEloCalculator.RankBySwiss(
            orderedParticipants
                .Where(p => p.Status == TournamentParticipantStatus.Active
                    || p.Status == TournamentParticipantStatus.CheckedIn),
            tournament.FinalistCount).ToList();

        if (topFinalists.Count < tournament.FinalistCount)
        {
            warnings.Add($"Chỉ có {topFinalists.Count} người chơi Active, không đủ {tournament.FinalistCount} cho bàn chung kết.");
        }

        var finalPairings = new List<ManualPairingDto>
        {
            new()
            {
                MatchNumber = 1,
                PlayerIds = topFinalists.Select(p => p.UserId!.Value).ToList()
            }
        };

        return new RoundPairingsResponseDto
        {
            TournamentId = tournamentId,
            RoundNumber = roundNumber,
            Source = "Auto (suggested)",
            Pairings = finalPairings,
            Warnings = warnings
        };
    }

    public async Task<RoundPairingsResponseDto> SetRoundPairingsAsync(
        Guid managerId, Guid tournamentId, SetRoundPairingsRequestDto request)
    {
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        ValidateRoundNumber(request.RoundNumber, tournament);

        // Không cho set manual nếu round đã build matches (tránh xung đột với matches đã có)
        var roundExists = tournament.Matches.Any(m => m.RoundNumber == request.RoundNumber);
        if (roundExists)
        {
            throw new ConflictException(
                $"Round {request.RoundNumber} đã có matches. Hãy hủy các bàn hiện tại trước khi set manual pairings.");
        }

        // Validate pairings
        ValidateManualPairings(request.Pairings, tournament, request.RoundNumber);

        // Serialize + save
        var json = SerializeManualJson(request.Pairings);
        SetRoundPairingsJson(tournament, request.RoundNumber, json);
        tournament.PairingMode = TournamentPairingMode.Manual;
        tournament.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();

        return new RoundPairingsResponseDto
        {
            TournamentId = tournamentId,
            RoundNumber = request.RoundNumber,
            Source = "Manual",
            Pairings = request.Pairings,
            Warnings = new List<string>()
        };
    }

    public async Task<RoundPairingsResponseDto> ClearRoundPairingsAsync(Guid managerId, Guid tournamentId, int roundNumber)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        ValidateRoundNumber(roundNumber, tournament);

        var roundExists = tournament.Matches.Count == 0
            ? false
            : await _tournamentRepository.GetMatchesByTournamentAsync(tournamentId) is var matches
              && matches.Any(m => m.RoundNumber == roundNumber);

        if (roundExists)
        {
            throw new ConflictException(
                $"Round {roundNumber} đã có matches. Không thể reset pairings khi round đã chạy.");
        }

        SetRoundPairingsJson(tournament, roundNumber, null);
        tournament.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();

        // Trả về auto preview để manager biết sau khi clear
        return await PreviewPairingsAsync(managerId, tournamentId, roundNumber);
    }

    // === Helpers cho Manual Pairing ===

    private static void ValidateRoundNumber(int roundNumber, Tournament tournament)
    {
        if (roundNumber < 1 || roundNumber > tournament.TotalRounds)
        {
            throw new BadRequestException(
                $"Round {roundNumber} không hợp lệ. Tournament có TotalRounds = {tournament.TotalRounds}.");
        }
    }

    private static string? GetRoundPairingsJson(Tournament tournament, int roundNumber)
    {
        return roundNumber switch
        {
            1 => tournament.Round1PairingsJson,
            2 => tournament.Round2PairingsJson,
            3 => tournament.Round3PairingsJson,
            4 => tournament.FinalPairingsJson,
            _ => null
        };
    }

    private static void SetRoundPairingsJson(Tournament tournament, int roundNumber, string? json)
    {
        switch (roundNumber)
        {
            case 1: tournament.Round1PairingsJson = json; break;
            case 2: tournament.Round2PairingsJson = json; break;
            case 3: tournament.Round3PairingsJson = json; break;
            case 4: tournament.FinalPairingsJson = json; break;
        }
    }

    private static string SerializeManualJson(List<ManualPairingDto> pairings)
    {
        return System.Text.Json.JsonSerializer.Serialize(pairings);
    }

    private static List<ManualPairingDto> ParseManualJson(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<List<ManualPairingDto>>(json) ?? new List<ManualPairingDto>();
    }

    private void ValidateManualPairings(List<ManualPairingDto> pairings, Tournament tournament, int roundNumber)
    {
        // 1. MatchNumber unique
        var matchNumbers = pairings.Select(p => p.MatchNumber).ToList();
        if (matchNumbers.Distinct().Count() != matchNumbers.Count)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.MatchNumbersMustBeUnique);
        }

        // 2. PlayerId unique across toàn bộ pairings
        var allPlayerIds = pairings.SelectMany(p => p.PlayerIds).ToList();
        if (allPlayerIds.Distinct().Count() != allPlayerIds.Count)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.PlayerCannotAppearInMultipleTables);
        }

        // 3. Tất cả PlayerIds phải thuộc Active/CheckedIn participants
        var validUserIds = tournament.Participants
            .Where(p => p.Status == TournamentParticipantStatus.Active
                || p.Status == TournamentParticipantStatus.CheckedIn
                || p.Status == TournamentParticipantStatus.Registered)
            .Select(p => p.UserId)
            .ToHashSet();

        var invalidUserIds = allPlayerIds.Where(uid => !validUserIds.Contains(uid)).ToList();
        if (invalidUserIds.Count > 0)
        {
            throw new BadRequestException(
                $"Các UserId không thuộc giải đấu hoặc chưa check-in: {string.Join(", ", invalidUserIds)}");
        }

        // 4. Final round phải đúng 4 người trên 1 bàn
        if (roundNumber == tournament.TotalRounds)
        {
            if (pairings.Count != 1 || pairings[0].PlayerIds.Count != tournament.FinalistCount)
            {
                throw new BadRequestException(
                    $"Round Final phải có đúng 1 bàn với {tournament.FinalistCount} người chơi.");
            }
        }

        // 5. Mỗi pairing phải có 2-4 người (Splendor rule)
        foreach (var p in pairings)
        {
            if (p.PlayerIds.Count < 2 || p.PlayerIds.Count > 4)
            {
                throw new BadRequestException(
                    $"Bàn {p.MatchNumber}: phải có từ 2 đến 4 người (hiện tại: {p.PlayerIds.Count}).");
            }
        }
    }

    /// <summary>
    /// Build match list cho 1 round: dùng Manual nếu có, không thì Auto.
    /// </summary>
    private List<TournamentMatchBracket> BuildRoundMatches(
        Tournament tournament, int roundNumber, IReadOnlyList<TournamentParticipant> participants)
    {
        var manualJson = GetRoundPairingsJson(tournament, roundNumber);

        if (!string.IsNullOrWhiteSpace(manualJson))
        {
            var pairings = ParseManualJson(manualJson);
            return pairings.Select(p => new TournamentMatchBracket
            {
                Id = Guid.NewGuid(),
                TournamentId = tournament.Id,
                RoundNumber = roundNumber,
                MatchNumber = p.MatchNumber,
                IsFinal = roundNumber == tournament.TotalRounds,
                Player1Id = p.PlayerIds.Count > 0 ? p.PlayerIds[0] : null,
                Player2Id = p.PlayerIds.Count > 1 ? p.PlayerIds[1] : null,
                Player3Id = p.PlayerIds.Count > 2 ? p.PlayerIds[2] : null,
                Player4Id = p.PlayerIds.Count > 3 ? p.PlayerIds[3] : null,
                Status = TournamentMatchStatus.Scheduled,
                CreatedAt = DateTime.UtcNow
            }).ToList();
        }

        // Auto fallback
        if (roundNumber == tournament.TotalRounds)
        {
            // BuildFinalMatchAsync sẽ được gọi riêng trong RecordMatchResultAsync flow
            return new List<TournamentMatchBracket>();
        }

        // Truyền matches trước đó làm anti-repeat history cho balanced algorithm
        var previousMatches = (tournament.Matches ?? new List<TournamentMatchBracket>()).ToList();
        return BuildSwissRound(participants, tournament.Id, roundNumber, previousMatches);
    }

    /// <summary>
    /// Lấy active participants đã sort theo Swiss score cho round > 1, hoặc FIFO cho Round 1.
    /// </summary>
    private List<TournamentParticipant> GetOrderedActiveParticipants(Tournament tournament, int roundNumber)
    {
        var active = tournament.Participants
            .Where(p => p.Status == TournamentParticipantStatus.Active
                || p.Status == TournamentParticipantStatus.CheckedIn)
            .ToList();

        if (roundNumber == 1)
        {
            return active.OrderBy(p => p.CheckedInAt ?? p.RegisteredAt).ToList();
        }

        // Round 2+ : sort theo Swiss score giảm dần
        return active
            .OrderByDescending(p => (p.SwissWins * 1.0) + (p.SwissDraws * 0.5))
            .ThenByDescending(p => p.TotalPrestigePoints)
            .ThenBy(p => p.CheckedInAt ?? p.RegisteredAt)
            .ToList();
    }
}