using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class TournamentService : ITournamentService
{
    private readonly ITournamentRepository _tournamentRepository;
    private readonly ITournamentWaitlistRepository _waitlistRepository;
    private readonly IGameTemplateRepository _gameTemplateRepository;
    private readonly ICafePosRepository _cafePosRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly ISystemConfigurationProvider _systemConfigurationProvider;
    private readonly IKarmaRatingRepository _karmaRatingRepository;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly ILogger<TournamentService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TournamentService(
        ITournamentRepository tournamentRepository,
        ITournamentWaitlistRepository waitlistRepository,
        IGameTemplateRepository gameTemplateRepository,
        ICafePosRepository cafePosRepository,
        ICafeRepository cafeRepository,
        IUserProfileRepository userProfileRepository,
        ISystemConfigurationProvider systemConfigurationProvider,
        IKarmaRatingRepository karmaRatingRepository,
        IPushNotificationService pushNotificationService,
        ILogger<TournamentService> logger,
        IHttpContextAccessor httpContextAccessor = null!)
    {
        _tournamentRepository = tournamentRepository;
        _waitlistRepository = waitlistRepository;
        _gameTemplateRepository = gameTemplateRepository;
        _cafePosRepository = cafePosRepository;
        _cafeRepository = cafeRepository;
        _userProfileRepository = userProfileRepository;
        _systemConfigurationProvider = systemConfigurationProvider;
        _karmaRatingRepository = karmaRatingRepository;
        _pushNotificationService = pushNotificationService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
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

        // 3) Resolve tournament-supported GameTemplateId (config-driven, khÃ´ng hardcode tÃªn "Splendor").
        var gameTemplateId = await ResolveTournamentGameTemplateIdAsync(request.GameTemplateId);

        // F14 Fix: Láº¥y MinParticipants tá»« GameTemplate config (Splendor = 2) thay vÃ¬ hardcode = 4.
        // Cho phÃ©p há»— trá»£ cÃ¡c game cÃ³ min players khÃ¡c nhau (vd Splendor Duel = 2).
        // F18: Manager cÃ³ thá»ƒ override cao hÆ¡n qua request.MinParticipants nhÆ°ng khÃ´ng tháº¥p hÆ¡n GameTemplate config.
        var gameTemplate = await _gameTemplateRepository.GetByIdAsync(gameTemplateId);
        var templateMin = gameTemplate?.TournamentMinPlayersPerTable ?? 4;
        var minParticipants = request.MinParticipants.HasValue
            ? Math.Max(templateMin, request.MinParticipants.Value)
            : templateMin;

        var now = DateTime.UtcNow;
        var deadline = request.RegistrationDeadline
            ?? request.StartTime.AddHours(-24);

        // P2 Fix #12: Ensure deadline is not in the past
        if (deadline <= now
            && !await TimeWindowGuard.ShouldBypassAsync(
                _httpContextAccessor?.HttpContext, _systemConfigurationProvider, _logger,
                operation: "Tournament.CreateDeadlinePast", entityId: null))
        {
            throw new BadRequestException(
                ApiErrorMessages.Tournament.RegistrationDeadlineInPast(deadline));
        }

        if (deadline >= request.StartTime)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.RegistrationDeadlineAfterStartTime);
        }

        if (request.MinEloRequirement > request.MaxEloRequirement)
        {
            throw new BadRequestException(
                ApiErrorMessages.Tournament.MinEloGreaterThanMaxElo);
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
            HasThirdPlaceMatch = request.HasThirdPlaceMatch,
            CurrentRound = 0,
            MinKarmaRequirement = TournamentKarmaPolicy.ClampKarma(request.MinKarmaRequirement),
            MinEloRequirement = request.MinEloRequirement,
            MaxEloRequirement = request.MaxEloRequirement,
            WinnerKarmaBonus = TournamentKarmaPolicy.WinnerBonus,
            FinalistKarmaBonus = TournamentKarmaPolicy.GetFinalistBonus(2, 4),
            // NoShowKarmaPenalty: null = client khÃ´ng gá»­i field â†’ dÃ¹ng default -10.
            // CÃ³ giÃ¡ trá»‹ (ká»ƒ cáº£ 0) â†’ lÆ°u Ä‘Ãºng giÃ¡ trá»‹ client gá»­i (Ä‘Ã£ clamp vá» [-100, 0]).
            NoShowKarmaPenalty = TournamentKarmaPolicy.ClampPenalty(
                request.NoShowKarmaPenalty ?? TournamentKarmaPolicy.NoShowPenalty),
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
            // Chá»‰ enforce future check khi Draft (chÆ°a má»Ÿ Ä‘Äƒng kÃ½).
            // Khi RegistrationOpen rá»“i, StartTime cÃ³ thá»ƒ Ä‘Ã£ qua nhÆ°ng tournament chÆ°a start
            // â†’ váº«n cho phÃ©p dá»i sang ngÃ y future khÃ¡c.
            if (tournament.Status == TournamentStatus.Draft
                && request.StartTime.Value <= DateTime.UtcNow
                && !await TimeWindowGuard.ShouldBypassAsync(
                    _httpContextAccessor?.HttpContext, _systemConfigurationProvider, _logger,
                    operation: "Tournament.StartTimeFuture", entityId: tournament.Id))
            {
                throw new BadRequestException(ApiErrorMessages.Tournament.StartTimeMustBeFuture);
            }
            tournament.StartTime = request.StartTime.Value;

            // Náº¿u manager Ä‘á»•i StartTime mÃ  khÃ´ng Ä‘á»•i RegistrationDeadline,
            // tá»± Ä‘á»™ng re-derive deadline = StartTime - 24h (cÃ¹ng rule nhÆ° create).
            // TrÃ¡nh case deadline cÅ© Ä‘Ã£ qua nhÆ°ng StartTime má»›i á»Ÿ tÆ°Æ¡ng lai.
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
                    ApiErrorMessages.Tournament.MinEloGreaterThanMaxElo);
            }
            tournament.MinEloRequirement = minElo;
            tournament.MaxEloRequirement = maxElo;
        }

        if (request.NoShowKarmaPenalty.HasValue)
        {
            tournament.NoShowKarmaPenalty = TournamentKarmaPolicy.ClampPenalty(request.NoShowKarmaPenalty.Value);
        }

        // WinnerKarmaBonus / FinalistKarmaBonus: há»‡ thá»‘ng tá»± tÃ­nh theo rank, khÃ´ng cho manager nháº­p tay.
        // Re-derive náº¿u FinalistCount thay Ä‘á»•i (hiá»‡n chÆ°a expose API Ä‘á»•i nhÆ°ng giá»¯ logic phÃ²ng trÆ°á»ng há»£p).
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
        // Default: khÃ´ng cho phÃ©p partial start, khÃ´ng auto-shorten.
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
    /// Core start logic. ÄÆ°á»£c dÃ¹ng bá»Ÿi cáº£ StartTournamentAsync (default)
    /// vÃ  StartTournamentWithOptionsAsync (manager override).
    ///
    /// Shortage handling flow:
    ///   1. Check state (RegistrationClosed/Open).
    ///   2. Count checkedIn participants.
    ///   3. Náº¿u checkedIn &lt; MinParticipants:
    ///      a. Náº¿u Tournament.AutoExtendOnShortage && ExtensionCount &lt; MaxExtensionCount:
    ///         - Tá»± Ä‘á»™ng extend registration deadline.
    ///         - Push notification cho users chÆ°a check-in.
    ///         - Tráº£ vá» status "Extended" â†’ manager retry sau khi extend.
    ///      b. Náº¿u AllowPartialStart = true: tiáº¿p tá»¥c vá»›i shortage.
    ///         - TÃ­nh ActualPreliminaryRounds báº±ng TournamentRoundsCalculator.
    ///         - Set StartedWithShortage = true (audit trail).
    ///      c. Náº¿u khÃ´ng cÃ³ a hoáº·c b: throw 409.
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
            // F2 Fix: Cho phÃ©p auto-extend cáº£ khi status = RegistrationClosed.
            // Thá»±c táº¿: manager thÆ°á»ng CloseRegistration trÆ°á»›c khi Start (Ä‘á»ƒ chá»‘t danh sÃ¡ch).
            // Náº¿u thiáº¿u ngÆ°á»i, tá»± Ä‘á»™ng reopen + extend deadline Ä‘á»ƒ cÃ³ thÃªm cÆ¡ há»™i tuyá»ƒn.
            if (tournament.AutoExtendOnShortage
                && tournament.ExtensionCount < tournament.MaxExtensionCount)
            {
                // Má»Ÿ láº¡i registration náº¿u Ä‘Ã£ Ä‘Ã³ng.
                if (tournament.Status == TournamentStatus.RegistrationClosed)
                {
                    tournament.Status = TournamentStatus.RegistrationOpen;
                }
                return await PerformAutoExtensionAsync(tournament, checkedIn);
            }

            // Option b: Allow partial start (manager override)
            if (allowPartialStart)
            {
                // Continue vá»›i shortage, sáº½ Ä‘Ã¡nh dáº¥u StartedWithShortage sau
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
            // Shortage: tÃ­nh optimal rounds
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

        // === Build pairings vÃ  set state ===
        // F4 Fix: Auto-promote Registered participants (Ä‘Ã£ Ä‘áº¿n quÃ¡n nhÆ°ng manager khÃ´ng check-in trÆ°á»›c) â†’ Active.
        // Thá»±c táº¿ board game cafe: manager thÆ°á»ng báº¥m Start kÃ¨m danh sÃ¡ch Ä‘áº¿n luÃ´n,
        // khÃ´ng check-in tá»«ng ngÆ°á»i. Náº¿u khÃ´ng auto-promote, participants "Registered" bá»‹ bá» sÃ³t
        // â†’ tournament cháº¡y thiáº¿u ngÆ°á»i dÃ¹ há» Ä‘Ã£ Ä‘áº¿n.
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
    /// Auto-extend registration deadline khi thiáº¿u ngÆ°á»i.
    /// ExtensionMinutesPerAttempt (default 30) má»—i láº§n, tá»‘i Ä‘a MaxExtensionCount láº§n.
    /// F7 Fix: Log audit event Ä‘á»ƒ admin/debug theo dÃµi + mobile app cÃ³ thá»ƒ polling Ä‘á»ƒ biáº¿t extend.
    /// Khi NotificationService sáºµn sÃ ng, swap ILogger â†’ IPushNotificationService.SendTournamentExtensionAsync.
    /// </summary>
    private async Task<TournamentResponseDto> PerformAutoExtensionAsync(
        Tournament tournament, int currentCheckedIn)
    {
        tournament.RegistrationDeadline = tournament.RegistrationDeadline
            .AddMinutes(tournament.ExtensionMinutesPerAttempt);
        tournament.ExtensionCount += 1;
        tournament.UpdatedAt = DateTime.UtcNow;

        // F7: Audit log cho admin/debug. Mobile app cáº§n polling endpoint GetTournamentAsync
        // Ä‘á»ƒ detect RegistrationDeadline thay Ä‘á»•i vÃ  hiá»ƒn thá»‹ banner "ÄÃ£ Ä‘Æ°á»£c gia háº¡n".
        // Khi NotificationService sáºµn sÃ ng, hook vÃ o Ä‘Ã¢y Ä‘á»ƒ push notification tá»›i:
        //   - Mobile app users Ä‘Ã£ Ä‘Äƒng kÃ½ (status=Registered/CheckedIn) â€” thÃ´ng bÃ¡o giáº£i chÆ°a báº¯t Ä‘áº§u.
        //   - Manager â€” xÃ¡c nháº­n auto-extend Ä‘Ã£ trigger.
        // Hiá»‡n táº¡i: structured log Ä‘á»ƒ monitoring tool scrape + audit trail.
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

        // BR-09 mirror + flow 4.9: Cho phÃ©p cancel tá»« RegistrationOpen / RegistrationClosed / OnGoing.
        // LÃ½ do thá»±c táº¿:
        // - RegistrationOpen / RegistrationClosed: chÆ°a ai chÆ¡i â†’ cancel an toÃ n.
        // - OnGoing: tournament Ä‘Ã£ cháº¡y 1-2 round, manager muá»‘n dá»«ng vÃ¬ lÃ½ do báº¥t kháº£ khÃ¡ng
        //   (vd: cÃºp Ä‘iá»‡n, mÆ°a lá»›n, dispute giá»¯a cÃ¡c Ä‘á»™i). Player tá»± xá»­ lÃ½ cash refund ngoÃ i app.
        //   CHá»ˆ cháº·n khi Status = Completed â€” khÃ´ng thá»ƒ cancel sau khi Ä‘Ã£ trao giáº£i (Elo/Karma Ä‘Ã£ sync).
        
        // Cháº·n cancel náº¿u Ä‘Ã£ cancelled rá»“i (idempotent nhÆ°ng test expect 409)
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

        // Auto-mark táº¥t cáº£ participants (Registered/CheckedIn/Active) thÃ nh Withdrawn
        // Ä‘á»ƒ dá»n dáº¹p state. Player váº«n cÃ³ thá»ƒ gá»i unregister idempotent.
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

        // Há»§y cÃ¡c matches chÆ°a diá»…n ra (náº¿u cÃ³ - thÆ°á»ng chá»‰ cÃ³ á»Ÿ RegistrationClosed)
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

        // Apply Karma bonuses + sync Elo vá» UserProfile (idempotent â€” guard báº±ng IsFinalEloSynced).
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

    public async Task<IReadOnlyList<TournamentResponseDto>> GetTournamentsAsync(Guid? currentUserId, string? status)
    {
        // Hỗ trợ 3 trường hợp của FE:
        // - status = null / "" → lấy tất cả (frontend tự filter).
        // - status = "all" → lấy tất cả (giống null).
        // - status = "<enum-name>" → parse TournamentStatus.
        TournamentStatus? statusEnum = null;

        if (!string.IsNullOrWhiteSpace(status)
            && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<TournamentStatus>(status, ignoreCase: true, out var parsed))
            {
                throw new BadRequestException(
                    ApiErrorMessages.Tournament.InvalidStatusFilter(
                        status,
                        "Draft, RegistrationOpen, RegistrationClosed, OnGoing, Completed hoặc Cancelled"));
            }
            statusEnum = parsed;
        }

        var tournaments = await _tournamentRepository.GetAllByStatusAsync(statusEnum);
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
                    ApiErrorMessages.Tournament.InvalidStatusFilter(
                        status,
                        "Draft, RegistrationOpen, RegistrationClosed, OnGoing, Completed hoáº·c Cancelled"));
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
            // T-03: Tournament full â†’ add to waitlist instead of throwing error
            var existingWaitlist = await _waitlistRepository.GetPendingByUserAsync(tournamentId, userId);
            if (existingWaitlist != null)
            {
                throw new ConflictException(ApiErrorMessages.Tournament.AlreadyInWaitlist);
            }

            var position = await _waitlistRepository.GetNextPositionAsync(tournamentId);
            var waitlistEntry = new TournamentWaitlist
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                UserId = userId,
                Position = position,
                Status = TournamentWaitlistStatus.Pending,
                JoinedAt = DateTime.UtcNow
            };

            await _waitlistRepository.AddAsync(waitlistEntry);
            await _waitlistRepository.SaveChangesAsync();

            _logger.LogInformation(
                "User {UserId} added to waitlist for tournament {TournamentId} at position {Position}",
                userId, tournamentId, position);

            // Return a placeholder DTO indicating waitlist status
            return new TournamentParticipantResponseDto
            {
                Id = waitlistEntry.Id,
                TournamentId = tournamentId,
                UserId = userId,
                Username = string.Empty, // Not fetched in waitlist path
                Status = TournamentParticipantStatus.Registered,
                IsWaitlisted = true,
                WaitlistPosition = position,
                RegisteredAt = waitlistEntry.JoinedAt
            };
        }

        // Karma check
        // F9 Fix: Cache user profile snapshot trong 1 query, dÃ¹ng cho cáº£ Karma check + snapshot fields.
        // TrÆ°á»›c Ä‘Ã¢y: GetByIdWithProfileAsync Ä‘Æ°á»£c gá»i 2-3 láº§n (Karma check, Karma snapshot, Elo snapshot) â†’ N+1.
        // Giá»: 1 query duy nháº¥t, cache vÃ o local var.
        var user = await _userProfileRepository.GetByIdWithProfileAsync(userId);
        if (user?.Profile == null)
        {
            throw new NotFoundException(
                ApiErrorMessages.Tournament.ProfileRequiredForJoin);
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
                ApiErrorMessages.Tournament.EloOutOfRange(
                    currentElo, tournament.MinEloRequirement, tournament.MaxEloRequirement));
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
            // Race: 2 requests cÃ¹ng register 1 user trong cÃ¹ng tournament.
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

        // Tournament Cancelled/Completed â†’ idempotent no-op (trÃ¡nh lá»™ state trÆ°á»›c kia).
        if (tournament.Status == TournamentStatus.Cancelled
            || tournament.Status == TournamentStatus.Completed)
        {
            return MapParticipantDto(participant);
        }

        if (participant.Status == TournamentParticipantStatus.Withdrawn)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.AlreadyWithdrawn(tournamentId));
        }

        // KhÃ´ng cho rÃºt lui khi player Ä‘Ã£ check-in, Ä‘ang thi Ä‘áº¥u hoáº·c Ä‘Ã£ káº¿t thÃºc
        // â†’ trÃ¡nh bá» trá»‘ng gháº¿ á»Ÿ vÃ²ng Final.
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

    /// <summary>
    /// Manager xÃ³a (kick) 1 participant khá»i tournament (vÃ­ dá»¥: gian láº­n, vi pháº¡m ná»™i quy).
    /// Set status = Withdrawn, ghi audit reason. Player tá»± withdraw báº±ng <see cref="WithdrawRegistrationAsync"/>.
    /// </summary>
    public async Task<TournamentParticipantResponseDto> ManagerKickParticipantAsync(
        Guid managerId,
        Guid tournamentId,
        Guid participantId,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.KickReasonRequired);
        }

        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        var participant = await _tournamentRepository.GetParticipantByIdAsync(participantId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.ParticipantNotFound);

        if (participant.TournamentId != tournamentId)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.ParticipantNotInTournament);
        }

        // Tournament terminal â†’ reject kick
        if (tournament.Status == TournamentStatus.Cancelled
            || tournament.Status == TournamentStatus.Completed)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotKickParticipantTerminal(tournament.Status));
        }

        if (participant.Status == TournamentParticipantStatus.Withdrawn)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.AlreadyWithdrawn(tournamentId));
        }

        // Không cho kick khi participant đã hoàn thành tournament (Finished).
        // Cho phép kick khi đã CheckedIn/Active để Manager có thể loại người chơi vi phạm.
        if (participant.Status == TournamentParticipantStatus.Finished)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotKickAfterCheckIn(participant.Status));
        }

        participant.Status = TournamentParticipantStatus.Withdrawn;
        participant.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();

        _logger.LogInformation(
            "Manager {ManagerId} kicked participant {ParticipantId} from tournament {TournamentId}. Reason: {Reason}",
            managerId, participantId, tournamentId, reason);

        return MapParticipantDto(participant);
    }

    public async Task<IReadOnlyList<TournamentParticipantResponseDto>> GetParticipantsAsync(Guid tournamentId)
    {
        var participants = await _tournamentRepository.GetParticipantsAsync(tournamentId);
        return participants.Select(MapParticipantDto).ToList();
    }

    public async Task<IReadOnlyList<TournamentParticipantResponseDto>> GetParticipantsForPosAsync(Guid managerId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

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

        // Walk-in Ä‘Æ°á»£c phÃ©p á»Ÿ má»i tráº¡ng thÃ¡i ngoáº¡i trá»« Draft / Completed / Cancelled.
        // LÃ½ do: quÃ¡n cÃ³ thá»ƒ nháº­n khÃ¡ch vÃ£ng lai báº¥t ká»³ lÃºc nÃ o trÆ°á»›c khi R1 hoÃ n thÃ nh.
        if (tournament.Status != TournamentStatus.RegistrationOpen
            && tournament.Status != TournamentStatus.RegistrationClosed
            && tournament.Status != TournamentStatus.OnGoing)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotAddWalkInInStatus(tournament.Status));
        }

        // KhÃ´ng cho add walk-in sau khi Final Ä‘Ã£ build (Final cÃ³ 4 slot cá»‘ Ä‘á»‹nh, BR-13 analogy).
        if (tournament.Matches?.Any(m => m.IsFinal) == true)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.FinalMatchExists);
        }

        // Thá»±c táº¿ board game cafe: walk-in chá»‰ Ä‘Æ°á»£c vÃ o khi R1 CHÆ¯A hoÃ n thÃ nh.
        // Sau khi R1 Ä‘Ã£ cÃ³ Swiss score (â‰¥1 match Completed), reject Ä‘á»ƒ giá»¯ fairness
        // â€” player gá»‘c Ä‘Ã£ Ä‘áº§u tÆ° 1 round, walk-in khÃ´ng thá»ƒ nháº£y vÃ o giá»¯a R2+ Ä‘á»ƒ "rá»­a" Swiss.
        var roundOneCompleted = tournament.Matches?.Any(m =>
            m.RoundNumber == 1
            && m.Status == TournamentMatchStatus.Completed) ?? false;
        if (roundOneCompleted)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.WalkInClosedAfterRoundOne);
        }

        // KhÃ´ng cho add walk-in khi round hiá»‡n táº¡i Ä‘ang OnGoing (mid-match).
        // Manager chá» round káº¿t thÃºc rá»“i add trÆ°á»›c khi AdvanceRound.
        var currentRoundInProgress = tournament.Matches?.Any(m =>
            m.RoundNumber == tournament.CurrentRound
            && m.Status == TournamentMatchStatus.OnGoing) ?? false;
        if (currentRoundInProgress)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.RoundInProgress);
        }

        // Idempotency: DisplayName Ä‘Ã£ tá»“n táº¡i (walk-in only).
        var trimmedName = request.DisplayName.Trim();
        var existingWalkIn = tournament.Participants?
            .FirstOrDefault(p => p.IsWalkIn
                && string.Equals(p.WalkInDisplayName, trimmedName, StringComparison.OrdinalIgnoreCase));
        if (existingWalkIn != null)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.WalkInDuplicateName(trimmedName));
        }

        // Walk-in luÃ´n join tá»« Round 1. Náº¿u R1 Ä‘Ã£ hoÃ n thÃ nh thÃ¬ bá»‹ reject á»Ÿ check trÃªn.
        // (Tournament chÆ°a start â†’ JoinedRound = 1; Tournament OnGoing nhÆ°ng R1 chÆ°a Completed â†’ váº«n JoinedRound = 1.)
        var joinedRound = 1;

        // F16 Fix: Auto CheckedIn khi tournament Ä‘Ã£ RegistrationClosed (chÆ°a Start) hoáº·c OnGoing.
        // Thá»±c táº¿ board game cafe: walk-in Ä‘áº¿n quÃ¡n â†’ manager add ngay táº¡i POS â†’ walk-in Ä‘Ã£ cÃ³ máº·t
        // â†’ nÃªn CheckedIn luÃ´n Ä‘á»ƒ sáºµn sÃ ng tham gia R1, khÃ´ng cáº§n manager check-in thÃªm 1 bÆ°á»›c.
        // Status = RegistrationOpen thÃ¬ giá»¯ Registered (vÃ¬ cÃ³ thá»ƒ chÆ°a Ä‘áº¿n ngay).
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
            InitialElo = EloRatingHelper.DefaultRating, // Walk-in khÃ´ng cÃ³ profile â†’ dÃ¹ng default rating.
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

        // NoShow chá»‰ Ã¡p dá»¥ng cho player chÆ°a tham gia vÃ²ng Ä‘áº¥u nÃ o.
        // Náº¿u Ä‘Ã£ Active (Ä‘Ã£ chÆ¡i Ã­t nháº¥t 1 round) hoáº·c Finished, khÃ´ng thá»ƒ Ä‘Ã¡nh no-show
        // vÃ¬ FinalRank vÃ  Elo Ä‘Ã£ Ä‘Æ°á»£c tÃ­nh. Manager cáº§n xá»­ lÃ½ riÃªng (refund/forfeit).
        if (participant.Status == TournamentParticipantStatus.Finished
            || participant.Status == TournamentParticipantStatus.Active)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.NoShowAfterRoundStarted(participant.Status));
        }

        if (participant.Status != TournamentParticipantStatus.Registered
            && participant.Status != TournamentParticipantStatus.CheckedIn)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.NoShowInvalidStatus(participant.Status));
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
                    Reason = $"[Tournament {tournamentId}] KhÃ´ng Ä‘áº¿n tham dá»± (no-show)",
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

    public async Task<IReadOnlyList<TournamentMatchResponseDto>> GetMatchesForPosAsync(
        Guid managerId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        var matches = await _tournamentRepository.GetMatchesByTournamentAsync(tournamentId);
        return matches.Select(MapMatchDto).ToList();
    }

    public async Task<IReadOnlyList<TournamentMatchResponseDto>> GetRoundMatchesForPosAsync(
        Guid managerId, Guid tournamentId, int roundNumber)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

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

        // Schedule â†’ Completed mÃ  khÃ´ng qua OnGoing bá» qua audit (ActualStartTime/EndTime).
        // Tá»± Ä‘á»™ng set ActualStartTime khi manager skip StartMatch step (defensive).
        if (match.Status == TournamentMatchStatus.Scheduled)
        {
            match.ActualStartTime = DateTime.UtcNow;
            match.Status = TournamentMatchStatus.OnGoing;
        }

        // Resolve winner + results: PlayerNId = User.Id for both Swiss và Final (FK reference).
        // Walk-in cã UserId = null → không tham gia slot → request.UserId/WinnerUserId luôn là User.Id.
        var resolvedWinnerId = request.WinnerUserId;
        var resolvedResults = request.Results.Select(r => new
        {
            r.Score,
            r.CardsBought,
            ResolvedUserId = r.UserId
        }).ToList();

        // Validate that winner is in the player list
        var playerSlots = new[] { match.Player1Id, match.Player2Id, match.Player3Id, match.Player4Id }
            .Where(p => p.HasValue).Select(p => p!.Value).ToList();

        if (!resolvedWinnerId.HasValue || !playerSlots.Contains(resolvedWinnerId.Value))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.WinnerMustBePlayer(matchId));
        }

        // === F3.1 Fix: Validate Results.Count matches player slot count ===
        // TrÃ¡nh Swiss score thiáº¿u do manager bá» sÃ³t 1 player khi nháº­p káº¿t quáº£.
        if (request.Results.Count != playerSlots.Count)
        {
            throw new BadRequestException(
                ApiErrorMessages.Tournament.ResultsIncomplete(
                    playerSlots.Count, request.Results.Count));
        }

        // === F3 Fix: Per-game max score validation ===
        // Láº¥y GameTemplate config (TournamentMaxScorePerPlayer) tá»« tournament.GameTemplate.
        // Splendor = 15; Splendor Duel = 20. Default 15.
        var maxScorePerPlayer = tournament.GameTemplate?.TournamentMaxScorePerPlayer ?? 15;
        foreach (var r in resolvedResults)
        {
            if (r.Score > maxScorePerPlayer)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Tournament.ScoreExceedsLimit(
                        r.ResolvedUserId ?? Guid.Empty, r.Score, maxScorePerPlayer,
                        tournament.GameTemplate?.Name ?? "Tournament"));
            }
        }

        // Apply scores to slot positions
        foreach (var r in resolvedResults)
        {
            if (!r.ResolvedUserId.HasValue || !playerSlots.Contains(r.ResolvedUserId.Value))
            {
                throw new BadRequestException(ApiErrorMessages.Tournament.PlayerNotInMatch(matchId, r.ResolvedUserId ?? Guid.Empty));
            }

            if (match.Player1Id == r.ResolvedUserId)
            {
                match.Player1Score = r.Score;
                match.Player1CardsBought = r.CardsBought;
            }
            else if (match.Player2Id == r.ResolvedUserId)
            {
                match.Player2Score = r.Score;
                match.Player2CardsBought = r.CardsBought;
            }
            else if (match.Player3Id == r.ResolvedUserId)
            {
                match.Player3Score = r.Score;
                match.Player3CardsBought = r.CardsBought;
            }
            else if (match.Player4Id == r.ResolvedUserId)
            {
                match.Player4Score = r.Score;
                match.Player4CardsBought = r.CardsBought;
            }
        }

        // WinnerPlayerId = resolvedWinnerId (already resolved for both Final and Swiss)
        match.WinnerPlayerId = resolvedWinnerId;

        match.Status = TournamentMatchStatus.Completed;
        match.ActualEndTime = DateTime.UtcNow;
        match.RecordedByStaffId = request.RecordedByStaffId ?? managerId;
        match.Notes = request.Notes?.Trim();
        match.UpdatedAt = DateTime.UtcNow;

        // === I1 Fix: Validate Final feasibility TRÆ¯á»šC khi mutate Elo/Swiss ===
        // Náº¿u match vá»«a ghi lÃ  round Swiss cuá»‘i â†’ pháº£i build Final.
        // Walk-in Ä‘Æ°á»£c vÃ o Final (hiá»ƒn thá»‹ tÃªn vá»›i ðŸš¶ prefix, khÃ´ng update Elo/Karma).
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

        // Aggregate Prestige scores + Elo delta vÃ o TournamentParticipant totals
        await AggregateSwissScoresAsync(tournament, match);

        // Aggregate Elo changes (multi-player, Swiss round hoáº·c Final)
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
            // Just finished the last Swiss round â†’ build Final match (idempotent: skip if already exists)
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
            throw new NotFoundException(ApiErrorMessages.Tournament.UserProfileNotFoundById(userId));
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

        // Bulk fetch stats cho táº¥t cáº£ userIds trong 1 query thay vÃ¬ N+1.
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
        // Äáº£m báº£o manager owns cafe trÆ°á»›c khi tráº£ data.
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
                ApiErrorMessages.Tournament.MatchEditOnlyCompleted(match.Status));
        }

        if (match.IsFinal)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.FinalMatchCannotEdit);
        }

        // Chá»‰ cho sá»­a khi CHÆ¯A build round káº¿ tiáº¿p â€” trÃ¡nh revert Swiss score.
        var nextRound = match.RoundNumber + 1;
        if (tournament.Matches.Any(m => m.RoundNumber == nextRound && m.RoundNumber <= tournament.PreliminaryRounds))
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.MatchEditRoundConflict(nextRound, match.RoundNumber));
        }

        // Validate + apply giá»‘ng RecordMatchResultAsync nhÆ°ng vá»›i correctionReason
        var playerSlots = new[] { match.Player1Id, match.Player2Id, match.Player3Id, match.Player4Id }
            .Where(p => p.HasValue).Select(p => p!.Value).ToList();

        if (!playerSlots.Contains(request.WinnerUserId ?? Guid.Empty))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.WinnerMustBePlayer(matchId));
        }

        if (request.Results.Count != playerSlots.Count)
        {
            throw new BadRequestException(
                ApiErrorMessages.Tournament.ResultsIncomplete(playerSlots.Count, request.Results.Count));
        }

        // === Revert Swiss score cÅ© cá»§a 4 players ===
        await RevertMatchSwissScoresAsync(tournament, match);

        // === Apply Swiss score má»›i ===
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
                    ApiErrorMessages.Validation.TournamentScoreExceedsLimitSimple(
                        r.UserId ?? Guid.Empty, r.Score, maxScorePerPlayer));
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

        // === Revert Elo + apply láº¡i ===
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
        // PlayerNId = User.Id (FK reference to Users table).
        // Tìm participant theo UserId để revert Swiss score cũ (PrestigePoints + CardsBought).
        // Walk-in cã UserId = null → không tham gia slot → không có gì để revert.
        var slotUserIds = new[]
        {
            match.Player1Id, match.Player2Id,
            match.Player3Id, match.Player4Id
        }.Where(id => id.HasValue).Select(id => id!.Value).ToList();

        foreach (var userId in slotUserIds)
        {
            var participant = tournament.Participants.FirstOrDefault(p => p.UserId == userId);
            if (participant == null) continue;

            if (userId == match.Player1Id)
            {
                participant.TotalPrestigePoints = Math.Max(0, participant.TotalPrestigePoints - (match.Player1Score ?? 0));
                participant.TotalCardsBought = Math.Max(0, participant.TotalCardsBought - (match.Player1CardsBought ?? 0));
            }
            else if (userId == match.Player2Id)
            {
                participant.TotalPrestigePoints = Math.Max(0, participant.TotalPrestigePoints - (match.Player2Score ?? 0));
                participant.TotalCardsBought = Math.Max(0, participant.TotalCardsBought - (match.Player2CardsBought ?? 0));
            }
            else if (userId == match.Player3Id)
            {
                participant.TotalPrestigePoints = Math.Max(0, participant.TotalPrestigePoints - (match.Player3Score ?? 0));
                participant.TotalCardsBought = Math.Max(0, participant.TotalCardsBought - (match.Player3CardsBought ?? 0));
            }
            else if (userId == match.Player4Id)
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
        // Láº¥y contributions Ä‘Ã£ lÆ°u Ä‘á»ƒ revert chÃ­nh xÃ¡c tá»«ng player (chá»‰ registered players).
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

        // Revert Swiss counters: dá»±a vÃ o WinnerPlayerId trong match
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

        // XÃ³a contributions cÅ© (sáº½ Ä‘Æ°á»£c táº¡o láº¡i khi apply result má»›i)
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

        // Cafe-level ownership check (consistent vá»›i cÃ¡c endpoint khÃ¡c trong service).
        // Cho phÃ©p cáº£ Co-Manager cá»§a cÃ¹ng cafe, khÃ´ng chá»‰ creator.
        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (match.Status == TournamentMatchStatus.Completed)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.CannotCancelCompleted(tournament.Id));
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

        // Round hiá»‡n táº¡i pháº£i Ä‘Ã£ Completed toÃ n bá»™ matches (má»i status pháº£i lÃ  Completed hoáº·c Cancelled)
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
        // Sort theo Swiss score giáº£m dáº§n (Swiss pairing: ngÆ°á»i cÃ¹ng Ä‘iá»ƒm gáº·p nhau)
        // Tiebreaker: CheckedInAt sá»›m hÆ¡n (FIFO trong nhÃ³m cÃ¹ng Ä‘iá»ƒm)
        var activeParticipants = tournament.Participants
            .Where(p => p.Status == TournamentParticipantStatus.Active)
            .OrderByDescending(p => (p.SwissWins * 1.0) + (p.SwissDraws * 0.5))
            .ThenByDescending(p => p.TotalPrestigePoints)
            .ThenBy(p => p.CheckedInAt ?? p.RegisteredAt)
            .ToList();

        if (nextRound <= tournament.PreliminaryRounds)
        {
            // Swiss round káº¿ tiáº¿p â€” build tá»« active participants
            if (activeParticipants.Count < 2)
            {
                throw new ConflictException(
                    ApiErrorMessages.Tournament.NotEnoughActiveForNextRound(
                        nextRound, activeParticipants.Count));
            }

            var newMatches = BuildRoundMatches(tournament, nextRound, activeParticipants);
            await _tournamentRepository.AddMatchesAsync(newMatches);
        }
        else if (nextRound == tournament.TotalRounds)
        {
            // Build bÃ n chung káº¿t â€” top 4 theo Swiss score (auto) HOáº¶C manual
            var finalExists = tournament.Matches.Any(m => m.IsFinal);
            if (finalExists)
            {
                throw new ConflictException(
                    ApiErrorMessages.Tournament.CannotAdvanceRoundFinalAlreadyBuilt(tournamentId));
            }

            // Manual Final: build tá»« pairings; Auto: gá»i BuildFinalMatchAsync
            var finalJson = tournament.FinalPairingsJson;
            if (!string.IsNullOrWhiteSpace(finalJson))
            {
                var pairings = ParseManualJson(finalJson);
                if (pairings.Count != 1 || pairings[0].PlayerIds.Count != tournament.FinalistCount)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Tournament.FinalPairingsInvalid(tournament.FinalistCount));
                }

                // Walk-in Ä‘Æ°á»£c tham gia Final náº¿u náº±m trong manual pairings.
                // Hiá»ƒn thá»‹ tÃªn vá»›i ðŸš¶ prefix, khÃ´ng update Elo/Karma.
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
                // Auto Final: build top N theo Swiss score ngay táº¡i AdvanceRound (khÃ´ng Ä‘á»£i RecordMatchResultAsync)
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
            // Tournament chÆ°a cÃ³ ai Ä‘Äƒng kÃ½ + Ä‘Ã£ háº¿t háº¡n â†’ bá» qua, khÃ´ng chuyá»ƒn sang Closed.
            // Náº¿u khÃ´ng skip, tournament sáº½ káº¹t á»Ÿ RegistrationOpen mÃ£i mÃ£i cho tá»›i khi manager cancel.
            // Manager tá»± xá»­ lÃ½ 0-participant tournament (cancel thá»§ cÃ´ng).
            // F12: Logic nháº¥t quÃ¡n giá»¯a 2 overloads (cÃ³ CT vÃ  khÃ´ng CT).
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
    /// Cancellable variant â€” pass stoppingToken xuá»‘ng DB calls.
    /// Background job nÃªn dÃ¹ng overload nÃ y Ä‘á»ƒ shutdown nhanh khi app táº¯t.
    /// </summary>
    public async Task<int> AutoCloseExpiredRegistrationsAsync(DateTime cutoffTime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tournaments = await _tournamentRepository.GetUpcomingForClosingAsync(cutoffTime);
        var count = 0;
        foreach (var t in tournaments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // F12: ThÃªm guard giá»‘ng overload khÃ´ng CT Ä‘á»ƒ nháº¥t quÃ¡n logic.
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
    /// Gá»­i reminder notification cho participants chÆ°a check-in cá»§a cÃ¡c giáº£i Ä‘áº¥u sáº¯p báº¯t Ä‘áº§u.
    /// Reminder schedule: T-30, T-15, T-5 phÃºt.
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

            // XÃ¡c Ä‘á»‹nh reminder type dá»±a trÃªn thá»i gian cÃ²n láº¡i
            string? reminderType = minutesUntilStart switch
            {
                <= 30 and > 15 => "30min",
                <= 15 and > 5 => "15min",
                <= 5 => "5min",
                _ => null
            };

            if (reminderType == null) continue;

            // Láº¥y participants chÆ°a check-in (chá»‰ gá»­i reminder cho user online)
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
                    var reminderTypeLabel = reminderType switch
                    {
                        "30min" => "30 phÃºt",
                        "15min" => "15 phÃºt",
                        "5min" => "5 phÃºt",
                        _ => reminderType
                    };

                    await _pushNotificationService.SendToUsersAsync(
                        new[] { participant.UserId ?? Guid.Empty },
                        new PushNotificationPayload
                        {
                            Type = "TournamentReminder",
                            Title = $"Nháº¯c nhá»Ÿ: Giáº£i Ä‘áº¥u '{tournament.Title}' báº¯t Ä‘áº§u sau {reminderTypeLabel}",
                            Body = message,
                            Data = new Dictionary<string, string>
                            {
                                ["tournamentId"] = tournament.Id.ToString()
                            }
                        });

                    _logger.LogInformation(
                        "[TournamentReminder] Sent '{ReminderType}' reminder to User {UserId} for Tournament {TournamentId}: {Message}",
                        reminderType, participant.UserId, tournament.Id, message);
                    sentCount++;
                }
            }
        }

        return sentCount;
    }

    /// <summary>
    /// Tá»± Ä‘á»™ng Ä‘Ã¡nh dáº¥u no-show cho participants Ä‘Ã£ Ä‘Äƒng kÃ½ nhÆ°ng khÃ´ng check-in
    /// khi giáº£i Ä‘áº¥u báº¯t Ä‘áº§u (OnGoing + CurrentRound = 1).
    /// Ãp dá»¥ng Karma penalty náº¿u cÃ³ cáº¥u hÃ¬nh.
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

            // TÃ¬m participants Ä‘Ã£ Ä‘Äƒng kÃ½ nhÆ°ng chÆ°a check-in vÃ  chÆ°a Active (chÆ°a chÆ¡i round nÃ o)
            var noShowParticipants = tournament.Participants
                .Where(p => p.UserId.HasValue &&
                    p.Status == TournamentParticipantStatus.Registered)
                .ToList();

            if (noShowParticipants.Count == 0)
            {
                continue;
            }

            // H3 fix: N+1 - batch-fetch all user profiles in one query.
            var userIds = noShowParticipants.Select(p => p.UserId!.Value).Distinct().ToList();
            var profileMap = await _userProfileRepository.GetProfilesByUserIdsAsync(userIds);

            var now = DateTime.UtcNow;
            var karmaPenalty = tournament.NoShowKarmaPenalty;
            var karmaLogs = new List<KarmaLog>();

            foreach (var participant in noShowParticipants)
            {
                ct.ThrowIfCancellationRequested();

                participant.Status = TournamentParticipantStatus.NoShow;
                participant.UpdatedAt = now;
                markedIds.Add(participant.Id);

                result.TotalKarmaPenalty += karmaPenalty;

                // Apply Karma penalty + audit log (batch loaded profile).
                if (karmaPenalty != 0 && participant.UserId.HasValue
                    && profileMap.TryGetValue(participant.UserId.Value, out var profile))
                {
                    var before = profile.KarmaPoints;
                    var after = TournamentKarmaPolicy.ClampKarma(before + karmaPenalty);
                    var actualDelta = after - before;

                    profile.KarmaPoints = after;
                    profile.GamerTier = KarmaRatingHelper.ResolveTier(after);
                    profile.UpdatedAt = now;

                    karmaLogs.Add(new KarmaLog
                    {
                        Id = Guid.NewGuid(),
                        UserId = participant.UserId.Value,
                        ViolationCategory = KarmaViolationCategory.NoShow,
                        Source = KarmaLogSource.TournamentReward,
                        KarmaPointsChange = actualDelta,
                        KarmaBefore = before,
                        KarmaAfter = after,
                        Reason = $"[Tournament {tournament.Id}] KhÃ´ng Ä‘áº¿n tham dá»± (no-show)",
                        RelatedLobbyId = null,
                        PerformedByUserId = tournament.CreatedByManagerId,
                        IsAdminAdjustment = false,
                        CreatedAt = now
                    });
                }

                _logger.LogInformation(
                    "[TournamentNoShow] User {UserId} marked no-show for Tournament {TournamentId}. Karma penalty: {Penalty}",
                    participant.UserId, tournament.Id, karmaPenalty);

                // T-01: Send no-show push notification
                if (participant.UserId.HasValue)
                {
                    await _pushNotificationService.SendToUsersAsync(
                        new[] { participant.UserId.Value },
                        new PushNotificationPayload
                        {
                            Type = "TournamentNoShow",
                            Title = "Báº¡n bá»‹ Ä‘Ã¡nh dáº¥u váº¯ng máº·t",
                            Body = $"Báº¡n bá»‹ Ä‘Ã¡nh dáº¥u váº¯ng máº·t (no-show) táº¡i giáº£i Ä‘áº¥u '{tournament.Title}'.",
                            Data = new Dictionary<string, string>
                            {
                                ["tournamentId"] = tournament.Id.ToString()
                            }
                        });
                }
            }

            if (karmaLogs.Count > 0)
            {
                foreach (var log in karmaLogs)
                {
                    await _karmaRatingRepository.AddKarmaLogAsync(log);
                }
                await _karmaRatingRepository.SaveChangesAsync();
            }
            await _tournamentRepository.SaveChangesAsync();

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
    /// Chá»‰ cháº¥p nháº­n game cÃ³ <see cref="GameTemplate.IsTournamentSupported"/> = true
    /// (config-driven thay cho hardcode tÃªn "Splendor").
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

        // Fallback: chá»n game Ä‘Æ°á»£c flag TournamentSupported Ä‘áº§u tiÃªn (Splendor hiá»‡n táº¡i).
        // CÃ³ thá»ƒ thay báº±ng danh sÃ¡ch cho phÃ©p manager chá»n game trong tÆ°Æ¡ng lai.
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

        // DÃ¹ng Adaptive Balanced Swiss algorithm (SwissPairingHelper).
        // - Round 1: Snake draft by Elo (top vs bottom).
        // - Round 2+: Constraint solver vá»›i anti-repeat + Elo balance.
        var tables = SwissPairingHelper.BuildBalancedPairings(
            participants,
            roundNumber,
            previousMatches ?? new List<TournamentMatchBracket>());

        var matchNumber = 1;
        foreach (var table in tables)
        {
            // PlayerNId = User.Id (FK reference to Users table).
            // Walk-in cÃ³ UserId = null, khÃ´ng thá»ƒ táº¡o match há»£p lá»‡ â†’ skip.
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
                MatchType = Core.Enum.MatchType.Swiss,
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
        // Walk-in cÃ³ UserId = null â†’ skip Elo update.
        var playerIds = new[] { match.Player1Id, match.Player2Id, match.Player3Id, match.Player4Id }
            .Where(id => id.HasValue).Select(id => id!.Value).ToList();

        if (playerIds.Count < 2) return;

        // Chá»‰ registered players (UserId not null) má»›i cÃ³ Elo
        var currentEloByUser = tournament.Participants
            .Where(p => p.UserId.HasValue && playerIds.Contains(p.UserId.Value))
            .ToDictionary(p => p.UserId!.Value, p => p.FinalElo);

        if (currentEloByUser.Count < 2) return; // Cáº§n â‰¥ 2 registered players

        var configuredK = await _systemConfigurationProvider.GetIntAsync(SystemConfigKeys.EloKFactor, 32);
        match.EloKFactorUsed = configuredK;

        // Splendor 4-player: 1 winner, 3 losers. Draw semantics váº«n support cho future-proof
        var isDraw = !match.WinnerPlayerId.HasValue;
        var eloChanges = TournamentEloCalculator.CalculateMatchEloChanges(
            currentEloByUser,
            match.WinnerPlayerId,
            isDraw,
            configuredK);

        // Táº¥t cáº£ participants trong match (registered + walk-in)
        var participantsInMatch = tournament.Participants
            .Where(p => p.UserId.HasValue && playerIds.Contains(p.UserId.Value))
            .ToList();

        // Swiss counters: chá»‰ cho registered players
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
            // TÃ¬m winner: WinnerPlayerId = User.Id
            var winner = participantsInMatch.FirstOrDefault(p => p.UserId == match.WinnerPlayerId);
            var losers = participantsInMatch.Where(p => p.UserId != match.WinnerPlayerId).ToList();
            if (winner != null)
            {
                TournamentEloCalculator.UpdateSwissCounters(match, winner, losers);
            }
        }

        // Elo changes: chá»‰ cho registered players (UserId not null)
        var registeredInMatch = participantsInMatch.Where(p => p.UserId.HasValue).ToList();
        TournamentEloCalculator.ApplyEloChanges(registeredInMatch, eloChanges, isFinal: match.IsFinal);

        // LÆ°u Elo contributions cho registered players (Ä‘á»ƒ revert chÃ­nh xÃ¡c)
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
        // Walk-in Ä‘Æ°á»£c vÃ o Final (hiá»ƒn thá»‹ tÃªn vá»›i ðŸš¶ prefix, khÃ´ng update Elo/Karma).
        // BR-13 analogy: walk-in khÃ´ng cÃ³ UserId â†’ khÃ´ng nháº­n Elo/Karma rewards.
        //
        // PlayerNId = TournamentParticipant.Id (khÃ´ng pháº£i UserId).
        // Walk-in cÃ³ Participant.Id nhÆ°ng UserId = null.
        // DÃ¹ng ParticipantId Ä‘á»ƒ walk-in cÃ³ thá»ƒ tham gia Final.
        // PlayerNId = User.Id (FK reference to Users table).
        // Walk-in cã UserId = null → skip khõi Final slot (BR-13).
        // Top theo Swiss score, chỉ lấy registered players.
        var topParticipants = TournamentEloCalculator.RankBySwiss(
            tournament.Participants.Where(p =>
                p.Status == TournamentParticipantStatus.Active && p.UserId.HasValue),
            tournament.FinalistCount).ToList();

        if (topParticipants.Count < tournament.FinalistCount)
        {
            var activeCount = tournament.Participants
                .Count(p => p.Status == TournamentParticipantStatus.Active);
            throw new ConflictException(
                ApiErrorMessages.Tournament.FinalRequiresFourActiveParticipants(
                    activeCount, tournament.FinalistCount));
        }

        // Final match: top 2 participants. Player3Id/Player4Id = null (reserved for future expansion).
            // ThirdPlaceMatch gets the remaining 2 from top 4 (see BuildThirdPlaceMatchAsync below).
            var finalMatch = new TournamentMatchBracket
            {
                Id = Guid.NewGuid(),
                TournamentId = tournament.Id,
                RoundNumber = tournament.TotalRounds,
                MatchNumber = 1,
                IsFinal = true,
                MatchType = Core.Enum.MatchType.Final,
                Player1Id = topParticipants.ElementAtOrDefault(0)?.UserId,
                Player2Id = topParticipants.ElementAtOrDefault(1)?.UserId,
                Player3Id = null, // Reserved for expansion; do NOT assign topParticipants[2] here
                Player4Id = null, // — they belong to ThirdPlaceMatch
                Status = TournamentMatchStatus.Scheduled,
                CreatedAt = DateTime.UtcNow
            };

            await _tournamentRepository.AddMatchAsync(finalMatch);

            // ThirdPlaceMatch: uses the REMAINING 2 from top N (NOT the same as Final).
            // Before fix: used topParticipants[2] and topParticipants[3] — DUPLICATED with Final's Player3Id/Player4Id.
            // After fix: uses topParticipants[2] and topParticipants[3] ONLY when FinalistCount > 2;
            // for FinalistCount = 4 (the default), these are distinct from Final's top 2.
            //
            // Safe access: topParticipants always has >= FinalistCount items (enforced by guard above).
            // When FinalistCount = 4: Final gets [0,1], ThirdPlace gets [2,3] — no overlap.
            // When FinalistCount = 2 + HasThirdPlaceMatch=true: this branch is unreachable
            //   (2 players can't fill a Final + ThirdPlace), but guard above throws anyway.
            if (tournament.HasThirdPlaceMatch && topParticipants.Count >= 4)
            {
                await BuildThirdPlaceMatchAsync(tournament, topParticipants);
            }
    }

    private async Task BuildThirdPlaceMatchAsync(Tournament tournament, List<TournamentParticipant> topParticipants)
    {
        // Third place match: player xáº¿p háº¡ng 3 vÃ  4 tá»« Swiss
        // DÃ¹ng RoundNumber = tournament.TotalRounds (cÃ¹ng round vá»›i Final, phÃ¢n biá»‡t báº±ng MatchNumber = 2)
        var thirdPlaceMatch = new TournamentMatchBracket
        {
            Id = Guid.NewGuid(),
            TournamentId = tournament.Id,
            RoundNumber = tournament.TotalRounds,
            MatchNumber = 2,
            IsFinal = false,
            MatchType = Core.Enum.MatchType.ThirdPlaceMatch,
            Player1Id = topParticipants.Count > 2 ? topParticipants[2].UserId : null,
            Player2Id = topParticipants.Count > 3 ? topParticipants[3].UserId : null,
            Status = TournamentMatchStatus.Scheduled,
            CreatedAt = DateTime.UtcNow
        };

        await _tournamentRepository.AddMatchAsync(thirdPlaceMatch);
    }

    private void AssignFinalRanks(Tournament tournament, TournamentMatchBracket finalMatch)
    {
        // Walk-in Ä‘Æ°á»£c xáº¿p rank trong Final (hiá»ƒn thá»‹ vá»›i ðŸš¶ prefix trong response).
        // BR-13 analogy: walk-in khÃ´ng nháº­n Elo/Karma rewards (UserId = null).
        //
        // Lưu ý: PlayerNId trong match slot = User.Id (FK reference to Users table).
        // Walk-in cã UserId = null → không tham gia Final slot → không có trong playerIds.
        var playerIds = new[]
        {
            finalMatch.Player1Id, finalMatch.Player2Id,
            finalMatch.Player3Id, finalMatch.Player4Id
        }.Where(id => id.HasValue).Select(id => id!.Value).ToList();

        // Táº¥t cáº£ finalists (registered + walk-in) by UserId
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

            // Fallback: rank táº¥t cáº£ finalists theo PrestigePoints
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

        // Losers: táº¥t cáº£ finalists trá»« winner, rank theo PrestigePoints
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

        // BR-13/14 mirror + BR-12 invariant: walk-in khÃ´ng cÃ³ UserId â†’ khÃ´ng nháº­n Karma bonus.
        // Respect FinalistCount config: chá»‰ thÆ°á»Ÿng cho Top FinalistCount (khÃ´ng hardcode 4).
        if (winner != null && !winner.IsWalkIn && winner.UserId.HasValue && tournament.WinnerKarmaBonus > 0)
        {
            await ApplyKarmaDeltaAsync(winner.UserId.Value, tournament.WinnerKarmaBonus,
                "GiÃ nh vÃ´ Ä‘á»‹ch tournament Splendor", tournament.Id, performer);
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
    /// Sync FinalElo tá»« má»—i TournamentParticipant vá» UserProfile.GlobalElo.
    /// Winner nháº­n thÃªm WinnerEloBonus (máº·c Ä‘á»‹nh +20 elo bonus).
    /// Chá»‰ cháº¡y khi Tournament.Status = Completed.
    /// </summary>
    private async Task SyncFinalEloToProfilesAsync(Tournament tournament)
    {
        // Bonus winner sau tournament Completed. Chá»‰ Ã¡p dá»¥ng khi FinalRank = 1.
        // GiÃ¡ trá»‹ +20 ~ báº±ng 1 Swiss win tháº¯ng bÃ¬nh thÆ°á»ng (delta +12~+20 tuá»³ split rating)
        // â†’ winner bonus khÃ´ng láº¥n Ã¡t pháº§n Elo tÃ­ch lÅ©y tá»« cÃ¡c vÃ¡n Swiss Ä‘Ã£ chÆ¡i.
        // CÃ³ thá»ƒ promote thÃ nh Tournament field náº¿u sau nÃ y cáº§n config per-tournament.
        const int WinnerEloBonus = 20;

        // M4: Batch fetch all profiles in 1 query thay vÃ¬ N queries.
        var eligibleUserIds = tournament.Participants
            .Where(p => !p.IsWalkIn
                && p.UserId.HasValue
                && (p.Status == TournamentParticipantStatus.Finished || p.FinalRank.HasValue))
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToList();

        var profileMap = eligibleUserIds.Count > 0
            ? await _userProfileRepository.GetProfilesByUserIdsAsync(eligibleUserIds)
            : new Dictionary<Guid, UserProfile>();

        foreach (var participant in tournament.Participants
            .Where(p => p.Status == TournamentParticipantStatus.Finished
                || p.FinalRank.HasValue))
        {
            // BR-13/14 mirror: walk-in khÃ´ng cÃ³ UserId, khÃ´ng sync Elo vá» profile
            // (khÃ´ng cÃ³ profile Ä‘á»ƒ Ä‘á»“ng bá»™ + khÃ´ng cÃ³ trÃ¡ch nhiá»‡m tÃ i sáº£n cÃ¡ nhÃ¢n).
            if (participant.IsWalkIn || participant.UserId == null) continue;

            if (!profileMap.TryGetValue(participant.UserId.Value, out var profile) || profile == null) continue;

            var totalDelta = TournamentEloCalculator.SyncToUserProfile(
                profile, participant, WinnerEloBonus);

            // Update participant.FinalElo = profile.GlobalElo (sau khi cá»™ng bonus)
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
        // GameTemplate Ä‘Ã£ Ä‘Æ°á»£c Include trong cÃ¡c query list (GetAllOpenAsync, GetByCafeAsync, ...).
        // Chá»‰ fallback GetByIdAsync khi navigation null (vd: GetByIdAsync single-row path).
        var game = tournament.GameTemplate
            ?? await _gameTemplateRepository.GetByIdAsync(tournament.GameTemplateId);

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
            HasThirdPlaceMatch = tournament.HasThirdPlaceMatch,
            CurrentRound = tournament.CurrentRound,
            StartedAt = tournament.StartedAt,
            MinKarmaRequirement = tournament.MinKarmaRequirement,
            MinEloRequirement = tournament.MinEloRequirement,
            MaxEloRequirement = tournament.MaxEloRequirement,
            WinnerKarmaBonus = tournament.WinnerKarmaBonus,
            FinalistKarmaBonus = tournament.FinalistKarmaBonus,
            NoShowKarmaPenalty = tournament.NoShowKarmaPenalty,
            CancellationReason = tournament.CancellationReason,
            CancelledAt = tournament.CancelledAt,
            Status = tournament.Status,
            // RegisteredCount: player Ä‘Ã£ Ä‘Äƒng kÃ½ nhÆ°ng chÆ°a check-in.
            // Náº¿u tournament Ä‘Ã£ check-in xong, count = 0.
            // Náº¿u tournament bá»‹ cancel trÆ°á»›c check-in, player váº«n á»Ÿ Registered.
            RegisteredCount = tournament.Participants?
                .Count(p => p.Status == TournamentParticipantStatus.Registered) ?? 0,
            // CheckedInCount = player Ä‘Ã£ cÃ³ máº·t táº¡i quÃ¡n nhÆ°ng tournament chÆ°a káº¿t thÃºc.
            // Loáº¡i trá»« Finished (Ä‘Ã£ hoÃ n thÃ nh) â€” Ä‘áº¿m nháº§m sáº½ lÃ m manager tÆ°á»Ÿng cÃ²n check-in thÃªm Ä‘Æ°á»£c.
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
            MatchType = m.MatchType,
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
        // F15: Cho phep Auto -> Manual khi da OnGoing, mien la cac ban dau chua bat dau.
        // Manager co the dieu chinh ghep doi truoc khi bat dau vong neu pairing auto khong can bang.
        // Chi block neu co ban dang dien ra (OnGoing) hoac da ket thuc (Completed).
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        if (tournament.Status == TournamentStatus.OnGoing && mode == TournamentPairingMode.Manual)
        {
            var currentRoundMatches = tournament.Matches
                .Where(m => m.RoundNumber == tournament.CurrentRound)
                .ToList();
            
            if (currentRoundMatches.Any(m => m.Status == TournamentMatchStatus.OnGoing || m.Status == TournamentMatchStatus.Completed))
            {
                throw new ConflictException(
                    ApiErrorMessages.Tournament.CannotSwitchManualWithActiveMatches);
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

        // Náº¿u Ä‘Ã£ cÃ³ manual, tráº£ vá» manual hiá»‡n táº¡i. Náº¿u khÃ´ng, build auto preview (DÃ™NG helper tháº­t).
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

        // Auto preview: dÃ¹ng SwissPairingHelper.BuildBalancedPairings tháº­t,
        // giá»‘ng vá»›i nhá»¯ng gÃ¬ BuildRoundMatches sáº½ sinh ra.
        var orderedParticipants = GetOrderedActiveParticipants(tournament, roundNumber);
        var warnings = new List<string>();

        // Round Swiss: dÃ¹ng balanced pairing helper
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

        // KhÃ´ng cho set manual náº¿u round Ä‘Ã£ build matches (trÃ¡nh xung Ä‘á»™t vá»›i matches Ä‘Ã£ cÃ³)
        var roundExists = tournament.Matches.Any(m => m.RoundNumber == request.RoundNumber);
        if (roundExists)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.RoundHasMatches(request.RoundNumber));
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
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        ValidateRoundNumber(roundNumber, tournament);

        var roundMatches = tournament.Matches
            .Where(m => m.RoundNumber == roundNumber)
            .ToList();

        if (roundMatches.Count > 0)
        {
            var hasCompletedMatches = roundMatches.Any(m =>
                m.Status == TournamentMatchStatus.Completed
                || m.Status == TournamentMatchStatus.OnGoing);

            if (hasCompletedMatches)
            {
                throw new ConflictException(
                    ApiErrorMessages.Tournament.RoundCannotResetPairings(roundNumber));
            }

            await _tournamentRepository.DeleteMatchesByRoundAsync(tournamentId, roundNumber);
        }

        SetRoundPairingsJson(tournament, roundNumber, null);
        tournament.PairingMode = TournamentPairingMode.Auto;
        tournament.UpdatedAt = DateTime.UtcNow;

        await _tournamentRepository.SaveChangesAsync();

        return await PreviewPairingsAsync(managerId, tournamentId, roundNumber);
    }

    /// <summary>
    /// Hoán đổi vị trí 2 người chơi giữa 2 bàn trong cùng round.
    /// Cho phép sửa pairings ngay cả khi round đã có matches.
    /// </summary>
    public async Task<RoundPairingsResponseDto> SwapPairingAsync(
        Guid managerId, Guid tournamentId, SwapPairingRequestDto request)
    {
        var tournament = await _tournamentRepository.GetByIdWithDetailsAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        await EnsureManagerOwnsCafeAsync(managerId, tournament.CafeId);

        // Check tournament đang trong quá trình thi đấu
        if (tournament.Status != TournamentStatus.OnGoing)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.SwapOnlyAllowedWhenOnGoing);
        }

        ValidateRoundNumber(request.RoundNumber, tournament);

        // Lấy các match trong round này
        var roundMatches = tournament.Matches
            .Where(m => m.RoundNumber == request.RoundNumber)
            .ToList();

        if (roundMatches.Count == 0)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.RoundHasNoMatches(request.RoundNumber));
        }

        // Tìm match chứa Player A
        var matchWithPlayerA = roundMatches
            .FirstOrDefault(m => m.Player1Id == request.PlayerAId
                || m.Player2Id == request.PlayerAId
                || m.Player3Id == request.PlayerAId
                || m.Player4Id == request.PlayerAId);

        if (matchWithPlayerA == null || matchWithPlayerA.MatchNumber != request.FromMatchNumber)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.PlayerNotInMatch(
                    request.PlayerAId, request.FromMatchNumber));
        }

        // Tìm match chứa Player B
        var matchWithPlayerB = roundMatches
            .FirstOrDefault(m => m.Player1Id == request.PlayerBId
                || m.Player2Id == request.PlayerBId
                || m.Player3Id == request.PlayerBId
                || m.Player4Id == request.PlayerBId);

        if (matchWithPlayerB == null || matchWithPlayerB.MatchNumber != request.ToMatchNumber)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.PlayerNotInMatch(
                    request.PlayerBId, request.ToMatchNumber));
        }

        // Check: 2 người cùng bàn
        if (matchWithPlayerA.Id == matchWithPlayerB.Id)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.SwapSameMatch);
        }

        // Check: không swap match đã hoàn thành
        if (matchWithPlayerA.Status == TournamentMatchStatus.Completed
            || matchWithPlayerB.Status == TournamentMatchStatus.Completed)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.SwapMatchAlreadyCompleted);
        }

        // Check: không swap match đang đấu (có thể cho phép nhưng cảnh báo)
        if (matchWithPlayerA.Status == TournamentMatchStatus.OnGoing
            || matchWithPlayerB.Status == TournamentMatchStatus.OnGoing)
        {
            throw new ConflictException(
                ApiErrorMessages.Tournament.SwapMatchOnGoing);
        }

        // Hoán đổi vị trí giữa 2 match
        SwapPlayerBetweenMatches(matchWithPlayerA, request.PlayerAId, matchWithPlayerB, request.PlayerBId);

        // Cập nhật RoundXPairingsJson để đồng bộ với entity
        var updatedPairings = BuildPairingsFromMatches(roundMatches);
        var json = SerializeManualJson(updatedPairings);
        SetRoundPairingsJson(tournament, request.RoundNumber, json);

        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        // Trả về danh sách pairings mới sau khi hoán đổi
        return new RoundPairingsResponseDto
        {
            TournamentId = tournamentId,
            RoundNumber = request.RoundNumber,
            Source = "Manual (Swapped)",
            Pairings = updatedPairings,
            Warnings = new List<string>()
        };
    }

    private void SwapPlayerBetweenMatches(
        TournamentMatchBracket matchA, Guid playerA,
        TournamentMatchBracket matchB, Guid playerB)
    {
        // Xác định vị trí của playerA và playerB
        int? posA = GetPlayerPosition(matchA, playerA);
        int? posB = GetPlayerPosition(matchB, playerB);

        // Xóa playerA khỏi vị trí cũ trong matchA
        ClearPlayerFromMatch(matchA, playerA);
        // Xóa playerB khỏi vị trí cũ trong matchB
        ClearPlayerFromMatch(matchB, playerB);

        // Đặt playerA vào vị trí của playerB trong matchB
        SetPlayerAtPosition(matchB, playerA, posB);
        // Đặt playerB vào vị trí của playerA trong matchA
        SetPlayerAtPosition(matchA, playerB, posA);

        matchA.UpdatedAt = DateTime.UtcNow;
        matchB.UpdatedAt = DateTime.UtcNow;
    }

    private int? GetPlayerPosition(TournamentMatchBracket match, Guid playerId)
    {
        if (match.Player1Id == playerId) return 1;
        if (match.Player2Id == playerId) return 2;
        if (match.Player3Id == playerId) return 3;
        if (match.Player4Id == playerId) return 4;
        return null;
    }

    private void ClearPlayerFromMatch(TournamentMatchBracket match, Guid playerId)
    {
        if (match.Player1Id == playerId) match.Player1Id = null;
        else if (match.Player2Id == playerId) match.Player2Id = null;
        else if (match.Player3Id == playerId) match.Player3Id = null;
        else if (match.Player4Id == playerId) match.Player4Id = null;
    }

    private void SetPlayerAtPosition(TournamentMatchBracket match, Guid playerId, int? position)
    {
        if (position == 1) match.Player1Id = playerId;
        else if (position == 2) match.Player2Id = playerId;
        else if (position == 3) match.Player3Id = playerId;
        else if (position == 4) match.Player4Id = playerId;
    }

    private List<ManualPairingDto> BuildPairingsFromMatches(List<TournamentMatchBracket> matches)
    {
        return matches
            .OrderBy(m => m.MatchNumber)
            .Select(m =>
            {
                var playerIds = new List<Guid>();
                if (m.Player1Id.HasValue) playerIds.Add(m.Player1Id.Value);
                if (m.Player2Id.HasValue) playerIds.Add(m.Player2Id.Value);
                if (m.Player3Id.HasValue) playerIds.Add(m.Player3Id.Value);
                if (m.Player4Id.HasValue) playerIds.Add(m.Player4Id.Value);
                return new ManualPairingDto
                {
                    MatchNumber = m.MatchNumber,
                    PlayerIds = playerIds
                };
            })
            .ToList();
    }

    // === Helpers cho Manual Pairing ===

    private static void ValidateRoundNumber(int roundNumber, Tournament tournament)
    {
        if (roundNumber < 1 || roundNumber > tournament.TotalRounds)
        {
            throw new BadRequestException(
                ApiErrorMessages.Tournament.RoundNumberOutOfRange(roundNumber, tournament.TotalRounds));
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

        // 2. PlayerId unique across toÃ n bá»™ pairings
        var allPlayerIds = pairings.SelectMany(p => p.PlayerIds).ToList();
        if (allPlayerIds.Distinct().Count() != allPlayerIds.Count)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.PlayerCannotAppearInMultipleTables);
        }

        // 3. Táº¥t cáº£ PlayerIds pháº£i thuá»™c Active/CheckedIn participants
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
                ApiErrorMessages.Tournament.InvalidPairingUserIds(string.Join(", ", invalidUserIds)));
        }

        // 4. Final round pháº£i Ä‘Ãºng 4 ngÆ°á»i trÃªn 1 bÃ n
        if (roundNumber == tournament.TotalRounds)
        {
            if (pairings.Count != 1 || pairings[0].PlayerIds.Count != tournament.FinalistCount)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Tournament.FinalPairingInvalidSingle(tournament.FinalistCount));
            }
        }

        // 5. Má»—i pairing pháº£i cÃ³ 2-4 ngÆ°á»i (Splendor rule)
        foreach (var p in pairings)
        {
            if (p.PlayerIds.Count < 2 || p.PlayerIds.Count > 4)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Tournament.PairingSizeInvalid(p.MatchNumber, p.PlayerIds.Count));
            }
        }
    }

    /// <summary>
    /// Build match list cho 1 round: dÃ¹ng Manual náº¿u cÃ³, khÃ´ng thÃ¬ Auto.
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
                MatchType = roundNumber == tournament.TotalRounds ? Core.Enum.MatchType.Final : Core.Enum.MatchType.Swiss,
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
            // BuildFinalMatchAsync sáº½ Ä‘Æ°á»£c gá»i riÃªng trong RecordMatchResultAsync flow
            return new List<TournamentMatchBracket>();
        }

        // Truyá»n matches trÆ°á»›c Ä‘Ã³ lÃ m anti-repeat history cho balanced algorithm
        var previousMatches = (tournament.Matches ?? new List<TournamentMatchBracket>()).ToList();
        return BuildSwissRound(participants, tournament.Id, roundNumber, previousMatches);
    }

    /// <summary>
    /// Láº¥y active participants Ä‘Ã£ sort theo Swiss score cho round > 1, hoáº·c FIFO cho Round 1.
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

        // Round 2+ : sort theo Swiss score giáº£m dáº§n
        return active
            .OrderByDescending(p => (p.SwissWins * 1.0) + (p.SwissDraws * 0.5))
            .ThenByDescending(p => p.TotalPrestigePoints)
            .ThenBy(p => p.CheckedInAt ?? p.RegisteredAt)
            .ToList();
    }

    // ====================================================================
    // ADMIN: FULL CRUD + REPORTS
    // ====================================================================

    public async Task<AdminTournamentListResponseDto> GetAdminTournamentsAsync(
        int page, int pageSize, string? searchTerm, string? status, Guid? cafeId)
    {
        TournamentStatus? tournamentStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TournamentStatus>(status, true, out var parsed))
        {
            tournamentStatus = parsed;
        }

        var (items, totalCount) = await _tournamentRepository.GetAdminListAsync(
            page, pageSize, searchTerm, tournamentStatus, cafeId);

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new AdminTournamentListResponseDto
        {
            Items = items.Select(t => new AdminTournamentListItemDto
            {
                Id = t.Id,
                Title = t.Title,
                CafeId = t.CafeId,
                CafeName = t.Cafe?.Name ?? "N/A",
                GameName = t.GameTemplate?.Name ?? "N/A",
                StartTime = t.StartTime,
                RegistrationDeadline = t.RegistrationDeadline,
                MinParticipants = t.MinParticipants,
                MaxParticipants = t.MaxParticipants,
                CurrentParticipants = t.Participants?.Count ?? 0,
                Status = t.Status.ToString(),
                CreatedAt = t.CreatedAt
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            HasPreviousPage = page > 1,
            HasNextPage = page < totalPages
        };
    }

    public async Task<AdminTournamentDetailDto?> GetAdminTournamentDetailAsync(Guid tournamentId)
    {
        var t = await _tournamentRepository.GetAdminDetailAsync(tournamentId);
        if (t == null)
        {
            return null;
        }

        return new AdminTournamentDetailDto
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            CafeId = t.CafeId,
            CafeName = t.Cafe?.Name ?? "N/A",
            GameTemplateId = t.GameTemplateId,
            GameName = t.GameTemplate?.Name ?? "N/A",
            StartTime = t.StartTime,
            RegistrationDeadline = t.RegistrationDeadline,
            RoundDurationMinutes = t.RoundDurationMinutes,
            MinParticipants = t.MinParticipants,
            MaxParticipants = t.MaxParticipants,
            CurrentParticipants = t.Participants?.Count ?? 0,
            CurrentRound = t.CurrentRound,
            TotalRounds = t.TotalRounds,
            MinKarmaRequirement = t.MinKarmaRequirement,
            MinEloRequirement = t.MinEloRequirement,
            MaxEloRequirement = t.MaxEloRequirement,
            Status = t.Status.ToString(),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            Participants = t.Participants?.Select(p => new AdminTournamentParticipantDto
            {
                Id = p.Id,
                UserId = p.UserId,
                Username = p.User?.Username ?? "N/A",
                Status = p.Status.ToString(),
                CheckedInAt = p.CheckedInAt,
                FinalRank = p.FinalRank,
                SwissWins = p.SwissWins,
                SwissDraws = p.SwissDraws,
                TotalPrestigePoints = p.TotalPrestigePoints,
                RegisteredAt = p.RegisteredAt
            }).ToList() ?? new List<AdminTournamentParticipantDto>()
        };
    }

    public async Task<TournamentResponseDto> AdminCreateTournamentAsync(Guid adminUserId, AdminCreateTournamentRequestDto request)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.TitleRequired);
        }

        if (request.StartTime <= DateTime.UtcNow)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.StartTimeMustBeFuture);
        }

        var gameTemplate = await _gameTemplateRepository.GetByIdAsync(request.GameTemplateId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.GameTemplateNotFound(request.GameTemplateId));

        var cafe = await _cafeRepository.GetByIdAsync(request.CafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.CafeNotFound(request.CafeId));

        var minParticipants = request.MinParticipants > 0 ? request.MinParticipants : gameTemplate.TournamentMinPlayersPerTable;
        var deadline = request.RegistrationDeadline != default ? request.RegistrationDeadline : request.StartTime.AddHours(-24);

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            CafeId = request.CafeId,
            CreatedByManagerId = Guid.Empty, // Admin doesn't have manager ID
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            GameTemplateId = request.GameTemplateId,
            StartTime = request.StartTime,
            RegistrationDeadline = deadline,
            RoundDurationMinutes = request.RoundDurationMinutes,
            MinParticipants = minParticipants,
            MaxParticipants = request.MaxParticipants,
            EntryFee = request.EntryFee,
            TotalRounds = request.TotalRounds > 0 ? request.TotalRounds : 4,
            PreliminaryRounds = request.PreliminaryRounds > 0 ? request.PreliminaryRounds : 3,
            FinalistCount = request.FinalistCount > 0 ? request.FinalistCount : 4,
            CurrentRound = 0,
            MinKarmaRequirement = request.MinKarmaRequirement,
            MinEloRequirement = request.MinEloRequirement,
            MaxEloRequirement = request.MaxEloRequirement,
            WinnerKarmaBonus = 20,
            FinalistKarmaBonus = 10,
            NoShowKarmaPenalty = -10,
            PairingMode = request.PairingMode,
            Status = TournamentStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _tournamentRepository.AddAsync(tournament);
        await _tournamentRepository.SaveChangesAsync();

        return await BuildResponseAsync(tournament, null);
    }

    public async Task<TournamentResponseDto> AdminUpdateTournamentAsync(Guid adminUserId, Guid tournamentId, AdminUpdateTournamentRequestDto request)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            tournament.Title = request.Title.Trim();
        }

        if (request.Description != null)
        {
            tournament.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        if (request.StartTime.HasValue)
        {
            tournament.StartTime = request.StartTime.Value;
        }

        if (request.RegistrationDeadline.HasValue)
        {
            tournament.RegistrationDeadline = request.RegistrationDeadline.Value;
        }

        if (request.RoundDurationMinutes.HasValue && request.RoundDurationMinutes.Value > 0)
        {
            tournament.RoundDurationMinutes = request.RoundDurationMinutes.Value;
        }

        if (request.MinParticipants.HasValue && request.MinParticipants.Value > 0)
        {
            tournament.MinParticipants = request.MinParticipants.Value;
        }

        if (request.MaxParticipants.HasValue && request.MaxParticipants.Value > 0)
        {
            tournament.MaxParticipants = request.MaxParticipants.Value;
        }

        if (request.EntryFee.HasValue)
        {
            tournament.EntryFee = request.EntryFee.Value;
        }

        if (request.MinKarmaRequirement.HasValue)
        {
            tournament.MinKarmaRequirement = request.MinKarmaRequirement.Value;
        }

        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        var result = await GetAdminTournamentDetailAsync(tournamentId);
        if (result == null)
            throw new InternalServerErrorException(ApiErrorMessages.System.TournamentRetrieveFailed(tournamentId));
        var updated = await _tournamentRepository.GetByIdAsync(tournamentId);
        return await BuildResponseAsync(updated!, null);
    }

    public async Task AdminDeleteTournamentAsync(Guid adminUserId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (tournament.Status != TournamentStatus.Draft &&
            tournament.Status != TournamentStatus.Cancelled &&
            tournament.Status != TournamentStatus.Completed)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.OnlyDeleteInSpecificStatus);
        }

        await _tournamentRepository.UpdateAsync(tournament);
        await _tournamentRepository.SaveChangesAsync();
    }

    public async Task<AdminTournamentParticipantsResponseDto> GetAdminTournamentParticipantsAsync(
        Guid tournamentId, string? status)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        var query = tournament.Participants.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TournamentParticipantStatus>(status, true, out var parsed))
        {
            query = query.Where(p => p.Status == parsed);
        }

        var allParticipants = query.ToList();
        var totalCount = allParticipants.Count;

        var participants = allParticipants
            .Select(p => new AdminTournamentParticipantDto
            {
                Id = p.Id,
                UserId = p.UserId,
                Username = p.User?.Username ?? "N/A",
                Status = p.Status.ToString(),
                CheckedInAt = p.CheckedInAt,
                FinalRank = p.FinalRank,
                SwissWins = p.SwissWins,
                SwissDraws = p.SwissDraws,
                TotalPrestigePoints = p.TotalPrestigePoints,
                RegisteredAt = p.RegisteredAt
            }).ToList();

        return new AdminTournamentParticipantsResponseDto
        {
            TournamentId = tournamentId,
            TournamentTitle = tournament.Title,
            Participants = participants,
            TotalCount = totalCount
        };
    }

    public async Task<TournamentResponseDto> AdminOpenRegistrationAsync(Guid adminUserId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (tournament.Status != TournamentStatus.Draft)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.OnlyOpenRegistrationDraft);
        }

        if (tournament.RegistrationDeadline <= DateTime.UtcNow)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.RegistrationDeadlineMustBeFuture);
        }

        tournament.Status = TournamentStatus.RegistrationOpen;
        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        return await BuildResponseAsync(tournament, null);
    }

    public async Task<TournamentResponseDto> AdminCloseRegistrationAsync(Guid adminUserId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (tournament.Status != TournamentStatus.RegistrationOpen)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.OnlyCloseRegistrationOpen);
        }

        tournament.Status = TournamentStatus.RegistrationClosed;
        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        return await BuildResponseAsync(tournament, null);
    }

    public async Task<TournamentResponseDto> AdminStartTournamentAsync(Guid adminUserId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (tournament.Status != TournamentStatus.RegistrationClosed)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.OnlyStartRegistrationClosed);
        }

        var activeCount = tournament.Participants?.Count(p =>
            p.Status == TournamentParticipantStatus.Active ||
            p.Status == TournamentParticipantStatus.CheckedIn) ?? 0;

        if (activeCount < tournament.MinParticipants)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.NotEnoughParticipants(tournament.MinParticipants, activeCount));
        }

        tournament.Status = TournamentStatus.OnGoing;
        tournament.CurrentRound = 1;
        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        return await BuildResponseAsync(tournament, null);
    }

    public async Task<TournamentResponseDto> AdminCompleteTournamentAsync(Guid adminUserId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (tournament.Status != TournamentStatus.OnGoing)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.OnlyOnGoingCompletable);
        }

        tournament.Status = TournamentStatus.Completed;
        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        return await BuildResponseAsync(tournament, null);
    }

    public async Task<TournamentResponseDto> AdminCancelTournamentAsync(Guid adminUserId, Guid tournamentId, string? reason)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (tournament.Status == TournamentStatus.Completed || tournament.Status == TournamentStatus.Cancelled)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.AlreadyEndedOrCancelled);
        }

        tournament.Status = TournamentStatus.Cancelled;
        tournament.CancellationReason = reason;
        tournament.CancelledAt = DateTime.UtcNow;
        tournament.UpdatedAt = DateTime.UtcNow;
        await _tournamentRepository.SaveChangesAsync();

        return await BuildResponseAsync(tournament, null);
    }
}
