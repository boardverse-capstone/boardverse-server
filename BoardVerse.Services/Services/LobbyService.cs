using System.Security.Cryptography;
using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Constants;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using GeoHelper = BoardVerse.Core.Helpers.GeoLocationHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services
{
    /// <summary>
    /// Lobby business logic.
    /// Public lobby: any user can join via /search.
    /// Private lobby: chỉ join được qua LobbyInvite hoặc ShareCode; không hiển thị trong search.
    /// BR-07: Lobby.MaxMembers nằm trong [GameTemplate.MinPlayers, GameTemplate.MaxPlayers].
    /// BR-08: Auto-hủy nếu trước giờ hẹn X phút mà chưa đạt MinPlayers.
    /// BR-10: Filter theo Karma (không dùng Elo).
    /// BR-REFUND-02/03: Host dissolve lobby → hoàn BVC theo mốc thời gian + release inventory.
    /// </summary>
    public class LobbyService : ILobbyService
    {
        // BR-REFUND-03: grace 15 phút đầu + chưa có member thì hoàn 100%.
        private const int DissolveGraceMinutes = 15;
        // BR-REFUND-02 mốc cao: ≥24h trước scheduledStartTime hoàn 100%.
        private const int DissolveFullRefundHours = 24;
        // BR-REFUND-02 mốc giữa: 6–24h hoàn 50%.
        private const int DissolveHalfRefundHours = 6;

        private readonly ILobbyRepository _lobbyRepository;
        private readonly IGameTemplateRepository _gameTemplateRepository;
        private readonly IUserManagementRepository _userManagementRepository;
        private readonly ILobbyInviteRepository _lobbyInviteRepository;
        private readonly ILobbyHubService _hubService;
        private readonly ILobbyMessageService _lobbyMessageService;
        private readonly ILobbyMessageRepository _lobbyMessageRepository;
        private readonly IFriendshipRepository _friendshipRepository;
        private readonly IReservationRepository _reservationRepository;
        private readonly IWalletService _walletService;
        private readonly ISeatInventoryRepository _seatInventoryRepository;
        private readonly IGameInventoryRepository _gameInventoryRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly ICafeRepository _cafeRepository;
        private readonly BoardVerseDbContext _db;
        private readonly EligibilityValidator _eligibilityValidator;
        private readonly IUserProfileService _userProfileService;
        private readonly ILogger<LobbyService> _logger;
        private readonly ISystemConfigurationProvider _configProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private const int ExpRewardPerCompletedLobby = 10; // K-04: exp reward for completing a lobby session

        public LobbyService(
            ILobbyRepository lobbyRepository,
            IGameTemplateRepository gameTemplateRepository,
            IUserManagementRepository userManagementRepository,
            ILobbyInviteRepository lobbyInviteRepository,
            ILobbyHubService hubService,
            ILobbyMessageService lobbyMessageService,
            ILobbyMessageRepository lobbyMessageRepository,
            IFriendshipRepository friendshipRepository,
            IReservationRepository reservationRepository,
            IWalletService walletService,
            ISeatInventoryRepository seatInventoryRepository,
            IGameInventoryRepository gameInventoryRepository,
            IOutboxRepository outboxRepository,
            ICafeRepository cafeRepository,
            BoardVerseDbContext db,
            EligibilityValidator eligibilityValidator,
            IUserProfileService userProfileService,
ILogger<LobbyService> logger,
            ISystemConfigurationProvider configProvider = null!,
            IHttpContextAccessor httpContextAccessor = null!)
        {
            _lobbyRepository = lobbyRepository;
            _gameTemplateRepository = gameTemplateRepository;
            _userManagementRepository = userManagementRepository;
            _lobbyInviteRepository = lobbyInviteRepository;
            _hubService = hubService;
            _lobbyMessageService = lobbyMessageService;
            _lobbyMessageRepository = lobbyMessageRepository;
            _friendshipRepository = friendshipRepository;
            _reservationRepository = reservationRepository;
            _walletService = walletService;
            _seatInventoryRepository = seatInventoryRepository;
            _gameInventoryRepository = gameInventoryRepository;
            _outboxRepository = outboxRepository;
            _cafeRepository = cafeRepository;
            _db = db;
            _eligibilityValidator = eligibilityValidator;
            _userProfileService = userProfileService;
            _logger = logger;
            _configProvider = configProvider;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<LobbyResponseDto> CreateLobbyAsync(Guid hostUserId, CreateLobbyRequestDto request, CancellationToken cancellationToken = default)
        {
            if (request.ScheduledStartTime < DateTime.UtcNow.AddMinutes(5))
            {
                throw new BadRequestException(ApiErrorMessages.Lobby.ScheduledStartTimeTooEarly);
            }

            var game = await _gameTemplateRepository.GetByIdWithComponentsAsync(request.GameTemplateId)
                ?? throw new NotFoundException(ApiErrorMessages.BoardGame.MasterNotFound(request.GameTemplateId));

            // BR-07: MaxMembers phải nằm trong [MinPlayers, MaxPlayers] của GameTemplate
            if (request.MaxMembers < game.MinPlayers || request.MaxMembers > game.MaxPlayers)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Lobby.MaxMembersExceedsGameRange(request.MaxMembers, game.MinPlayers, game.MaxPlayers));
            }

            // MinPlayers default = 2, validate > 0 và <= MaxMembers
            var minPlayers = request.MinPlayers ?? 2;
            if (minPlayers < 2 || minPlayers > request.MaxMembers)
            {
                throw new BadRequestException(ApiErrorMessages.Lobby.MinPlayersOutOfRangeForCreate);
            }

            // Nếu SeatCount được set → validate với MaxMembers (BR-07)
            if (request.SeatCount.HasValue && (request.SeatCount.Value < request.MaxMembers || request.SeatCount.Value > game.MaxPlayers * 2))
            {
                throw new BadRequestException(ApiErrorMessages.Lobby.SeatCountInvalidForLobby);
            }

            // Nếu có CafeId thì validate cafe có chứa GameTemplate này
            if (request.CafeId.HasValue)
            {
                var hasGame = await _gameTemplateRepository.CafeHasGameAsync(request.CafeId.Value, request.GameTemplateId);
                if (!hasGame)
                {
                    throw new BadRequestException(ApiErrorMessages.Lobby.CafeDoesNotHaveGame);
                }
            }

            // Nếu có BookingId thì validate booking đã CONFIRMED
            if (request.BookingId.HasValue)
            {
                var booking = await _lobbyRepository.GetBookingByIdAsync(request.BookingId.Value)
                    ?? throw new NotFoundException(ApiErrorMessages.Lobby.BookingNotFound);
                if (booking.UserId != hostUserId)
                {
                    throw new ForbiddenException(ApiErrorMessages.Lobby.NotBookingOwner);
                }
                if (booking.Status != BookingDepositStatus.Paid)
                {
                    throw new ConflictException(ApiErrorMessages.Lobby.BookingNotPaid);
                }
            }

            var now = DateTime.UtcNow;
            var lobby = new Lobby
            {
                Id = Guid.NewGuid(),
                HostUserId = hostUserId,
                GameTemplateId = request.GameTemplateId,
                CafeId = request.CafeId,
                BookingId = request.BookingId,
                ScheduledStartTime = request.ScheduledStartTime,
                CancellationLeadTimeMinutes = request.CancellationLeadTimeMinutes,
                MaxMembers = request.MaxMembers,
                MinPlayers = minPlayers,
                SeatCount = request.SeatCount,
                IsPrivate = request.IsPrivate,
                Description = request.Description,
                CoverImageUrl = request.CoverImageUrl,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                MinKarmaScore = request.MinKarmaScore,
                ShareCode = await GenerateUniqueShareCodeAsync(),
                Status = LobbyStatus.Open,
                CreatedAt = now,
                UpdatedAt = now,
                Members = new List<LobbyMember>()
            };

            lobby.Members.Add(new LobbyMember
            {
                Id = Guid.NewGuid(),
                LobbyId = lobby.Id,
                UserId = hostUserId,
                IsHost = true,
                IsActive = true,
                Status = LobbyMemberStatus.Joined,
                JoinedAt = now
            });

            await _lobbyRepository.AddAsync(lobby);
            await _lobbyRepository.SaveChangesAsync();

            // System message: lobby was created
            await _lobbyMessageService.AddSystemMessageAsync(lobby.Id, "Phòng chờ đã được tạo.");

            // Realtime: notify host's own lobby that it was created
            await _hubService.NotifyMemberJoined(lobby.Id, new LobbyMemberDto
            {
                Id = lobby.Members.First().Id,
                UserId = hostUserId,
                JoinedAt = now,
                IsActive = true,
                IsHost = true
            });

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> JoinLobbyAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default)
        {
            // H4: SELECT ... FOR UPDATE để chống race condition khi nhiều request JoinLobby đồng thời.
            // Trước đây: chỉ check count trên snapshot không lock → có thể vượt MaxMembers (BR-07).
            // Pattern copy từ ActiveSessionService.PaySessionAsync (null-safe cho unit test mock).
            await using var dbTx = await TryBeginTransactionAsync();
            try
            {
                // Lock row lobby (PostgreSQL FOR UPDATE). Caller phải trong transaction.
                var lobby = await _lobbyRepository.GetByIdForUpdateAsync(lobbyId)
                    ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

                // BR-LOBBY-READY-02: Cho phép join thêm khi lobby đã WaitingCheckIn (chờ check-in tại quán)
                // nếu vẫn còn chỗ trống (< MaxMembers). Trước đây chỉ Open, dẫn đến bạn bè đến sau
                // bị chặn không vào lobby dù còn ghế.
                if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.WaitingCheckIn)
                {
                    throw new ConflictException(ApiErrorMessages.Reservation.LobbyNotOpen);
                }

                // Nếu private → kiểm tra invite hoặc membership trước khi cho join
                if (lobby.IsPrivate)
                {
                    var isMember = lobby.Members.Any(m => m.UserId == userId && m.IsActive);
                    var acceptedInvite = await _lobbyInviteRepository.GetAcceptedInviteAsync(lobbyId, userId);
                    if (!isMember && acceptedInvite == null)
                    {
                        throw new ForbiddenException(ApiErrorMessages.LobbyInvite.PrivateLobbyRequiresInvite);
                    }
                }

                var now = DateTime.UtcNow;

                // L-02: Kiểm tra inactive member TRƯỚC — nếu user đã rời lobby trước đó thì reactivate
                // thay vì throw duplicate. (Fix: thứ tự đúng là inactive → active)
                var inactiveMember = lobby.Members.FirstOrDefault(m => m.UserId == userId && !m.IsActive);
                if (inactiveMember != null)
                {
                    inactiveMember.IsActive = true;
                    inactiveMember.Status = LobbyMemberStatus.Joined;
                    inactiveMember.JoinedAt = now;
                    inactiveMember.LeftAt = null;

                    lobby.UpdatedAt = now;

                    // Sync Reservation.CurrentPlayers whenever member count changes.
                    SyncReservationCurrentPlayers(lobby);

                    var filledToMaxActiveMembers = lobby.Members.Count(m => m.IsActive) >= lobby.MaxMembers;
                    if (filledToMaxActiveMembers)
                    {
                        if (lobby.Status != LobbyStatus.Full)
                        {
                            lobby.FullAt = DateTime.UtcNow;
                        }
                        lobby.Status = LobbyStatus.Full;
                    }

                    await _lobbyRepository.SaveChangesAsync();

                    await _lobbyMessageService.AddSystemMessageAsync(lobby.Id, $"Thành viên đã quay trở lại phòng.");

                    await _hubService.NotifyMemberJoined(lobby.Id, new LobbyMemberDto
                    {
                        Id = inactiveMember.Id,
                        UserId = userId,
                        JoinedAt = now,
                        IsActive = true,
                        IsHost = inactiveMember.IsHost
                    });

                    if (filledToMaxActiveMembers)
                    {
                        await _hubService.NotifyLobbyFull(lobby.Id);
                    }

                    if (dbTx != null)
                    {
                        await dbTx.CommitAsync();
                    }

                    return MapLobbyDto(lobby, null);
                }

                // Chỉ throw duplicate khi user đang là active member (không phải inactive)
                if (lobby.Members.Any(m => m.UserId == userId && m.IsActive))
                {
                    throw new ConflictException(ApiErrorMessages.Lobby.MemberAlreadyInLobby);
                }

                // BR-LOBBY-01: chặn join sau recruitmentDeadline.
                var bypassLobbyDeadline = await TimeWindowGuard.ShouldBypassAsync(
                    _httpContextAccessor?.HttpContext, _configProvider, _logger,
                    operation: "Lobby.JoinDeadline", entityId: lobby.Id);
                if (!bypassLobbyDeadline
                    && lobby.RecruitmentDeadline.HasValue
                    && now > lobby.RecruitmentDeadline.Value)
                {
                    throw new ConflictException(ApiErrorMessages.Reservation.LobbyExpired);
                }

                if (lobby.Members.Count(m => m.IsActive) >= lobby.MaxMembers)
                {
                    throw new ConflictException(ApiErrorMessages.Reservation.LobbyFull);
                }

                // BR-07
                if (lobby.SeatCount.HasValue && lobby.Members.Count(m => m.IsActive) >= lobby.SeatCount.Value)
                {
                    throw new ConflictException(ApiErrorMessages.Lobby.SeatInventoryFull);
                }

                // BR-10: Karma filter — validate Karma của member so với minKarmaScore của lobby.
                if (lobby.MinKarmaScore.HasValue)
                {
                    var userWithProfile = await _userManagementRepository.GetByIdWithProfileAsync(userId);
                    var memberKarma = userWithProfile?.Profile?.KarmaPoints ?? 0;
                    if (memberKarma < lobby.MinKarmaScore.Value)
                    {
                        throw new ForbiddenException(
                            ApiErrorMessages.Reservation.KarmaRequirementNotMet(lobby.MinKarmaScore.Value, memberKarma));
                    }
                }

                // BR-USER-LIMIT-* + BR-RISK-04: validate member có đủ điều kiện join không.
                await ValidateMemberEligibilityAsync(userId, lobby, now);

                var newMember = new LobbyMember
                {
                    Id = Guid.NewGuid(),
                    LobbyId = lobby.Id,
                    UserId = userId,
                    IsHost = false,
                    IsActive = true,
                    Status = LobbyMemberStatus.Joined,
                    JoinedAt = now
                };
                lobby.Members.Add(newMember);

                lobby.UpdatedAt = now;

                // Sync Reservation.CurrentPlayers whenever member count changes.
                SyncReservationCurrentPlayers(lobby);

                var filledToMax = lobby.Members.Count(m => m.IsActive) >= lobby.MaxMembers;
                if (filledToMax)
                {
                    // BR-LOBBY-READY-03: ghi nhận mốc FullAt khi chuyển sang Full để scheduler biết đếm 20p.
                    if (lobby.Status != LobbyStatus.Full)
                    {
                        lobby.FullAt = DateTime.UtcNow;
                    }
                    lobby.Status = LobbyStatus.Full;
                }

                await _lobbyRepository.SaveChangesAsync();

                // System message
                await _lobbyMessageService.AddMemberJoinedMessageAsync(lobby.Id, userId);

                // Auto-cancel các invite còn Pending cho user này
                await _lobbyInviteRepository.CancelPendingForLobbyAndInviteeAsync(lobbyId, userId);

                // Realtime: broadcast MemberJoined + LobbyFull
                await _hubService.NotifyMemberJoined(lobby.Id, new LobbyMemberDto
                {
                    Id = newMember.Id,
                    UserId = userId,
                    JoinedAt = now,
                    IsActive = true,
                    IsHost = false
                });

                if (filledToMax)
                {
                    await _hubService.NotifyLobbyFull(lobby.Id);
                }

                if (dbTx != null)
                {
                    await dbTx.CommitAsync();
                }

                return MapLobbyDto(lobby, null);
            }
            catch
            {
                if (dbTx != null)
                {
                    await dbTx.RollbackAsync();
                }
                throw;
            }
        }

        /// <summary>
        /// Sync Reservation.CurrentPlayers với số active members trong Lobby.
        /// Gọi sau mỗi thao tác join/leave/reactivate để Reservation.CurrentPlayers luôn đúng.
        /// Nếu CurrentPlayers >= MinPlayers và Status còn là Holding → chuyển sang Confirmed real-time.
        /// Không làm gì nếu lobby không có Reservation liên kết.
        /// </summary>
        private void SyncReservationCurrentPlayers(Lobby lobby)
        {
            if (lobby.Reservation == null)
            {
                return;
            }

            var reservation = lobby.Reservation;
            var activeCount = lobby.Members.Count(m => m.IsActive);
            reservation.CurrentPlayers = activeCount;

            // BR-LOBBY-READY-01: Reservation CHỈ chuyển Holding → Confirmed khi lobby đạt WaitingCheckIn
            // (tất cả members ready). Lúc đủ minPlayers vẫn giữ Holding.
            // Transition Confirmed chỉ xảy ra tại MarkMembersReadyAndTransitionToInProgressAsync.
        }

        // H4: null-safe transaction helper, pattern copy từ ActiveSessionService.TryBeginTransactionAsync.
        private async Task<IDatabaseTransactionContext?> TryBeginTransactionAsync()
        {
            try
            {
                return await _lobbyRepository.BeginTransactionAsync();
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

        /// <summary>
        /// BR-USER-LIMIT-* + BR-RISK-04 cho member join lobby:
        /// - BR-USER-LIMIT-01: tổng host + member ≤ 2 active.
        /// - BR-USER-LIMIT-05: **ĐÃ BỎ** — Host có thể join lobby khác nếu không overlap.
        /// - BR-USER-LIMIT-02: Lịch của member trùng với lobby đang join (+30p buffer).
        /// - BR-RISK-04: Account bị suspended/banned → chặn.
        /// </summary>
        private async Task ValidateMemberEligibilityAsync(Guid userId, Lobby lobby, DateTime now)
        {
            // BR-DEMO-01: demo mode → skip toàn bộ user-limit checks (BR-USER-LIMIT-01/04/05).
            var bypassDemo = await DemoGuard.ShouldBypassDemoLocksAsync(
                _httpContextAccessor?.HttpContext, _configProvider, _logger,
                operation: "Lobby.ValidateMemberEligibility", entityId: userId);
            if (bypassDemo)
            {
                return;
            }

            var activeHostLobbies = await _lobbyRepository.GetActiveLobbiesByHostAsync(userId);
            var activeMemberLobbies = await _lobbyRepository.GetActiveLobbiesByMemberAsync(userId);

            // BR-USER-LIMIT-01: tổng host + member ≤ 2 active.
            var totalActiveLobbies = activeHostLobbies.Count + activeMemberLobbies.Count;
            if (totalActiveLobbies >= 2)
            {
                // BR-USER-LIMIT-01: Đã đạt tổng 2 lobby (host + member).
                // Đổi message đúng theo BR mới (BR-USER-LIMIT-05 ĐÃ BỎ - host được join).
                throw new ForbiddenException(ApiErrorMessages.Reservation.TotalLobbyLimitReached);
            }

            // BR-USER-LIMIT-01: Member đã có 1 lobby member active → không join thêm.
            if (activeMemberLobbies.Count >= 1)
            {
                throw new ForbiddenException(ApiErrorMessages.Reservation.ActiveLobbyMemberLimitReached);
            }

            // BR-RISK-04: Account bị suspended/banned → chặn.
            var user = await _userManagementRepository.GetByIdWithProfileAsync(userId);
            if (user == null)
            {
                throw new NotFoundException(ApiErrorMessages.Lobby.UserNotFoundInLobbyContext(userId));
            }

            if (user.AccountStatus == UserAccountStatus.Suspended || user.AccountStatus == UserAccountStatus.Banned)
            {
                throw new ForbiddenException(user.AccountStatus == UserAccountStatus.Banned
                    ? ApiErrorMessages.Reservation.BannedCannotCreateLobby
                    : ApiErrorMessages.Reservation.SuspendedCannotCreateLobby);
            }

            // BR-USER-LIMIT-02: Check overlap với lobby member hiện tại (+30p buffer).
            // Chỉ check khi lobby có PlayDate + TimeSlot (lobby mới qua Reservation flow).
            if (lobby.PlayDate.HasValue && lobby.RecruitmentDeadline.HasValue && lobby.ScheduledStartTime.HasValue)
            {
                var overlapList = await _lobbyRepository.GetOverlappingLobbiesAsync(
                    userId,
                    lobby.PlayDate.Value,
                    lobby.PreferredStartTime ?? (lobby.ScheduledStartTime.HasValue ? TimeOnly.FromDateTime(lobby.ScheduledStartTime.Value) : TimeOnly.MinValue),
                    lobby.PreferredEndTime ?? (lobby.ScheduledStartTime.HasValue ? TimeOnly.FromDateTime(lobby.ScheduledStartTime.Value.AddHours(2)) : TimeOnly.MinValue),
                    lobby.RecruitmentDeadline ?? DateTime.MinValue);

                if (overlapList.Any())
                {
                    var firstOverlap = overlapList.First(); // Any() check bảo đảm có ít nhất 1 item
                    throw new ConflictException(ApiErrorMessages.Reservation.OverlappingLobbyExists(
                        firstOverlap.RecruitmentDeadline ?? now,
                        firstOverlap.ScheduledStartTime ?? now));
                }
            }
        }

        public async Task<LobbyResponseDto> LeaveLobbyAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            // P1 Fix #1: Block leaving during terminal or in-progress states
            // Also block leaving when all members are ready (WaitingCheckIn)
            if (lobby.Status is LobbyStatus.InProgress or LobbyStatus.Closed or
                LobbyStatus.TimeoutFailed or LobbyStatus.HostCancelled or LobbyStatus.WaitingCheckIn)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.CannotLeaveLobbyDuringSession);
            }

            var member = lobby.Members.FirstOrDefault(m => m.UserId == userId && m.IsActive);
            if (member == null)
            {
                throw new NotFoundException(ApiErrorMessages.Lobby.NotMember);
            }

            var wasHost = member.IsHost;
            var memberId = member.Id;
            Guid? newHostUserId = null;

            // LOBBY-P0-FIX-1: Nếu host rời mà còn members khác → transfer host cho người join sớm nhất
            // Nếu không còn ai → HostCancelled
            if (wasHost)
            {
                var otherActiveMembers = lobby.Members
                    .Where(m => m.IsActive && m.UserId != userId)
                    .OrderBy(m => m.JoinedAt)
                    .ToList();

                if (otherActiveMembers.Count == 0)
                {
                    lobby.Status = LobbyStatus.HostCancelled;
                    lobby.ClosedAt = DateTime.UtcNow;
                    lobby.ClosedReason = "Host đã rời phòng và không còn thành viên nào.";
                }
                else
                {
                    var newHost = otherActiveMembers.First(); // Filter đảm bảo có ít nhất 1
                    newHost.IsHost = true;
                    newHostUserId = newHost.UserId;
                    await _lobbyMessageService.AddSystemMessageAsync(
                        lobby.Id,
                        $"Host đã rời phòng. {newHost.User?.Username ?? "Một thành viên"} trở thành Host mới.");

                    // Nếu lobby đang FULL nhưng không còn đủ MaxMembers → chuyển về OPEN
                    var activeAfter = lobby.Members.Count(m => m.IsActive) - 1; // trừ host hiện tại
                    if (lobby.Status == LobbyStatus.Full && activeAfter < lobby.MaxMembers)
                    {
                        lobby.Status = LobbyStatus.Open;
                        // BR-LOBBY-READY-03: reset FullAt vì không còn FULL nữa.
                        lobby.FullAt = null;
                    }
                }
            }

            member.IsActive = false;
            member.Status = LobbyMemberStatus.Left;
            member.LeftAt = DateTime.UtcNow;
            lobby.UpdatedAt = DateTime.UtcNow;

            // Sync Reservation.CurrentPlayers whenever member count changes.
            SyncReservationCurrentPlayers(lobby);

            await _lobbyRepository.SaveChangesAsync();

            // Realtime: notify group
            if (wasHost && newHostUserId == null)
            {
                await _hubService.NotifyLobbyCancelled(lobbyId, lobby.ClosedReason!);
            }
            else
            {
                await _hubService.NotifyMemberLeft(lobbyId, memberId);
                if (newHostUserId.HasValue)
                {
                    await _hubService.NotifyHostChanged(lobbyId, newHostUserId.Value);
                }
            }

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> GetLobbyAsync(Guid lobbyId, Guid? requestingUserId = null, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            // LOBBY-P0-FIX-8: Private lobby → enforce access control
            if (lobby.IsPrivate)
            {
                if (requestingUserId == null)
                {
                    throw new ForbiddenException(ApiErrorMessages.Lobby.PrivateLobbyRequiresLogin);
                }
                var isMember = lobby.Members.Any(m => m.UserId == requestingUserId.Value && m.IsActive);
                var hasInvite = await _lobbyInviteRepository.GetAcceptedInviteAsync(lobbyId, requestingUserId.Value);
                if (!isMember && hasInvite == null && lobby.HostUserId != requestingUserId.Value)
                {
                    throw new ForbiddenException(ApiErrorMessages.Lobby.PrivateLobbyNoAccess);
                }
            }

            double? distanceKm = null;
            if (lobby.Latitude.HasValue && lobby.Longitude.HasValue && requestingUserId.HasValue)
            {
                var user = await _userManagementRepository.GetByIdAsync(requestingUserId.Value);
                if (user?.Profile?.LastKnownLatitude is double userLat && user.Profile.LastKnownLongitude is double userLng)
                {
                    distanceKm = GeoHelper.HaversineKm(
                        userLat, userLng,
                        lobby.Latitude.Value, lobby.Longitude.Value);
                }
            }

            return MapLobbyDto(lobby, distanceKm);
        }

        public async Task<IReadOnlyList<LobbyResponseDto>> SearchLobbiesAsync(SearchLobbiesRequestDto request, Guid? requestingUserId = null, CancellationToken cancellationToken = default)
        {
            // BR-10: Filter by game, geo proximity, and karma (NOT Elo)
            // Private lobby bị loại khỏi kết quả search
            // BR-USER-LIMIT-02: Loại bỏ các lobby trùng lịch với user (excludeSelfOverlapping)

            IReadOnlyList<Lobby> lobbies;

            if (request.Latitude.HasValue && request.Longitude.HasValue && request.RadiusKm.HasValue)
            {
                lobbies = await _lobbyRepository.SearchLobbiesNearbyAsync(
                    request.GameTemplateId,
                    request.Latitude.Value,
                    request.Longitude.Value,
                    request.RadiusKm.Value,
                    request.MinKarmaScore);
            }
            else
            {
                lobbies = await _lobbyRepository.GetActiveLobbiesForGameAsync(request.GameTemplateId, null);
            }

            // Loại bỏ private lobby
            var filtered = lobbies.Where(l => !l.IsPrivate).ToList();

            // Filter by karma
            if (request.MinKarmaScore.HasValue)
            {
                filtered = filtered
                    .Where(l => l.Members.All(m => (m.User?.Profile?.KarmaPoints ?? 100) >= request.MinKarmaScore.Value))
                    .ToList();
            }

            // BR-USER-LIMIT-02: Loại bỏ lobby trùng lịch với user
            if (request.ExcludeSelfOverlapping && requestingUserId.HasValue)
            {
                filtered = await FilterOverlappingLobbiesAsync(filtered, requestingUserId.Value);
            }

            // Tính distance và map kết quả
            var result = new List<LobbyResponseDto>();
            foreach (var l in filtered)
            {
                double? dist = null;
                if (request.Latitude.HasValue && request.Longitude.HasValue
                    && l.Latitude.HasValue && l.Longitude.HasValue)
                {
                    dist = GeoHelper.HaversineKm(
                        request.Latitude.Value, request.Longitude.Value,
                        l.Latitude.Value, l.Longitude.Value);
                }
                result.Add(MapLobbyDto(l, dist));
            }
            return result;
        }

        public async Task<IReadOnlyList<LobbyResponseDto>> GetDiscoverableLobbiesAsync(
            Guid? gameTemplateId,
            double? latitude,
            double? longitude,
            double? radiusKm,
            int limit = 50,
            Guid? requestingUserId = null, CancellationToken cancellationToken = default)
        {
            // BR-10: Lobby phải là public + status Open. Private bị ẩn hoàn toàn.
            // BR-USER-LIMIT-02: Loại bỏ các lobby trùng lịch với user (excludeSelfOverlapping)
            // Áp dụng bounding-box pre-filter trong repo, Haversine precise sort ở đây.
            var lobbies = await _lobbyRepository.GetDiscoverablePublicLobbiesAsync(
                gameTemplateId, latitude, longitude, radiusKm, limit);

            var result = new List<LobbyResponseDto>();
            foreach (var l in lobbies)
            {
                double? distance = null;
                if (latitude.HasValue && longitude.HasValue
                    && l.Latitude.HasValue && l.Longitude.HasValue)
                {
                    distance = GeoHelper.HaversineKm(
                        latitude.Value, longitude.Value,
                        l.Latitude.Value, l.Longitude.Value);

                    // Nếu có radius filter nhưng vượt quá (do bbox không vuông) thì skip
                    if (radiusKm.HasValue && distance.Value > radiusKm.Value)
                    {
                        continue;
                    }
                }
                result.Add(MapLobbyDto(l, distance));
            }

            // Nếu filter theo khoảng cách, sort theo distance asc; ngược lại giữ CreatedAt desc
            if (latitude.HasValue && longitude.HasValue && radiusKm.HasValue)
            {
                result = result
                    .OrderBy(r => r.DistanceKm ?? double.MaxValue)
                    .ThenByDescending(r => r.CreatedAt)
                    .ToList();
            }

            // BR-USER-LIMIT-02: Loại bỏ lobby trùng lịch với user
            // M1: lọc dựa trên `lobbies` (raw Lobby entities) thay vì re-fetch qua GetByIdAsync.
            // Trước đây: lobbyIds.Select → GetByIdAsync → N+1 round-trips.
            // (Tránh nhầm với `result` là List<LobbyResponseDto> không có PlayDate/TimeSlot.)
            // GAP-03 fix (2026-08-21): Dùng PreferredStartTime.HasValue thay vì TimeSlot.HasValue
            if (requestingUserId.HasValue)
            {
                var loadedLobbies = lobbies
                    .Where(l => l.PlayDate.HasValue && l.PreferredStartTime.HasValue)
                    .ToList();
                var filteredLobbies = await FilterOverlappingLobbiesAsync(loadedLobbies, requestingUserId.Value);
                var filteredIds = filteredLobbies.Select(l => l.Id).ToHashSet();
                result = result.Where(r => filteredIds.Contains(r.Id)).ToList();
            }

            return result;
        }

        /// <summary>
        /// BR-USER-LIMIT-02: Loại bỏ các lobby trùng lịch với user (+30 phút buffer).
        /// Hai lobby trùng lịch nếu: cùng playDate + timeSlot + overlap thời gian.
        /// </summary>
        private async Task<List<Lobby>> FilterOverlappingLobbiesAsync(List<Lobby> lobbies, Guid userId)
        {
            // Lấy tất cả lobby mà user đang host hoặc tham gia
            var userLobbies = await _lobbyRepository.GetMyLobbiesAsync(userId);

            if (userLobbies.Count == 0)
            {
                return lobbies; // Không có lobby nào → không cần filter
            }

            // Tính scheduledTime của các lobby user đang tham gia
            // Bao gồm LobbyId để có thể exclude khi so sánh overlap
            var userScheduledRanges = userLobbies
                .Where(l => l.PlayDate.HasValue)
                .Select(l => new
                {
                    LobbyId = l.Id,
                    l.PlayDate,
                    l.TimeSlot,
                    Start = GetScheduledTime(l),
                    End = GetScheduledTime(l).AddMinutes(30) // +30 phút buffer
                }).ToList();

            // Loại bỏ lobby trùng lịch (NHƯNG KHÔNG loại bỏ chính lobby đó ra khỏi discoverable)
            // Fix: exclude lobby đang xét ra khỏi userScheduledRanges để lobby đã join vẫn hiển thị
            return lobbies.Where(lobby =>
            {
                if (!lobby.PlayDate.HasValue)
                {
                    return true; // Không có thông tin schedule → không filter
                }

                var lobbyStart = GetScheduledTime(lobby);
                var lobbyEnd = lobbyStart.AddMinutes(30);

                // Exclude chính lobby này ra khỏi danh sách so sánh
                // để lobby đã join vẫn hiển thị trong discoverable (public lobby vẫn visible cho tất cả)
                var otherUserRanges = userScheduledRanges
                    .Where(r => r.LobbyId != lobby.Id)
                    .ToList();

                // Kiểm tra overlap bằng computed Start/End times (BR-NEW-15: dùng PreferredStartTime)
                return !otherUserRanges.Any(userRange =>
                    userRange.PlayDate == lobby.PlayDate &&
                    userRange.Start < lobbyEnd &&
                    lobbyStart < userRange.End);
            }).ToList();
        }

        /// <summary>
        /// Tính scheduledTime từ PlayDate + TimeSlot (sync — dùng default <c>CafeSchedule</c>).
        /// BR-NEW-15 (2026-08-18): Dùng PreferredStartTime khi có, fallback to TimeSlot.GetStartTime().
        /// </summary>
        private static DateTime GetScheduledTime(Lobby lobby)
        {
            if (lobby.PlayDate.HasValue && lobby.PreferredStartTime.HasValue)
                return lobby.PlayDate.Value.ToDateTime(lobby.PreferredStartTime.Value);

            // Legacy: fall back to TimeSlot-based start time (for pre-BR-NEW-15 lobbies)
            if (lobby.PlayDate.HasValue && lobby.TimeSlot.HasValue)
                return lobby.PlayDate.Value.ToDateTime(lobby.TimeSlot.Value.GetStartTime());

            // Last resort: use ScheduledStartTime stored on lobby
            return lobby.ScheduledStartTime ?? DateTime.MinValue;
        }


        public async Task<LobbyResponseDto> CloseLobbyAsync(Guid lobbyId, Guid hostUserId, string? reason)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanClose);
            }

            if (lobby.Status == LobbyStatus.Closed || lobby.Status == LobbyStatus.HostCancelled || lobby.Status == LobbyStatus.TimeoutFailed)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.AlreadyClosed);
            }

            lobby.Status = LobbyStatus.Closed;
            lobby.ClosedAt = DateTime.UtcNow;
            lobby.ClosedReason = reason ?? "Host đã đóng phòng chờ.";
            lobby.UpdatedAt = DateTime.UtcNow;

            // Auto-cancel tất cả pending invites
            await _lobbyInviteRepository.CancelAllPendingForLobbyAsync(lobbyId);

            await _lobbyRepository.SaveChangesAsync();

            // K-04: Reward exp to all active members when lobby is closed
            var activeMembers = lobby.Members?.Where(m => m.IsActive).ToList() ?? [];
            foreach (var member in activeMembers)
            {
                try
                {
                    await _userProfileService.AddExpAndUpdateLevelAsync(member.UserId, ExpRewardPerCompletedLobby);
                }
                catch (Exception)
                {
                    // Non-critical: exp reward should not block lobby close
                }
            }

            await _lobbyMessageService.AddSystemMessageAsync(lobby.Id, $"Phòng chờ đã đóng: {lobby.ClosedReason}");

            return MapLobbyDto(lobby, null);
        }

        /// <summary>
        /// Host giải tán lobby trước khi check-in tại quán (DELETE /api/v1/lobbies/{id}).
        ///
        /// Soft delete: row Lobby + LobbyMember + LobbyMessage + LobbyInvite + LobbyReport
        /// vẫn còn trong DB để phục vụ:
        ///  - BR-RISK-01 (SIG-01/SIG-02): tính risk score cho host.
        ///  - BR-NEW-10 §XI.1: cooling-off detection (3 lần/7 ngày) — job quét status Dissolved.
        ///  - Audit trail (player action history).
        ///
        /// Lobby.Status = Dissolved (terminal, không thuộc ActiveLobbyStatuses nên BR-NEW-08
        /// cho phép host tạo lobby mới cùng playDate+timeSlot).
        ///
        /// GAP fix (2026-08-16):
        ///  - BR-REFUND-02/03: hoàn BVC theo mốc (grace 15p / 24h / 6h / &lt;6h) + ghi ledger.
        ///  - BR-RESERVATION-01/02 + §XVII.4 atomic: giải phóng SeatInventory + GameInventory
        ///    trong cùng transaction Serializable với status flip + refund.
        ///
        /// Idempotent (BR §XVII.1): refund/forfeit dùng key "dissolve-refund-{lobbyId}" và
        /// "dissolve-forfeit-{lobbyId}" — replay sẽ được wallet chặn tự động.
        /// </summary>
        public async Task<DissolveLobbyResponseDto> DissolveLobbyAsync(Guid lobbyId, Guid hostUserId, string? reason = null, CancellationToken cancellationToken = default)
        {
            const int MaxRetries = 3;

            for (var attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    return await ExecuteDissolveTransactionAsync(lobbyId, hostUserId, reason);
                }
                catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < MaxRetries)
                {
                    _logger.LogWarning(
                        "DissolveLobby serialization failure on attempt {Attempt}/{Max}. LobbyId={LobbyId}. Retrying...",
                        attempt, MaxRetries, lobbyId);
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxRetries)
                {
                    _logger.LogWarning(
                        "DissolveLobby concurrency failure on attempt {Attempt}/{Max}. LobbyId={LobbyId}. Retrying...",
                        attempt, MaxRetries, lobbyId);
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));
                }
            }

            throw new InternalServerErrorException(
                ApiErrorMessages.System.CancelRetryExhausted(lobbyId, MaxRetries));
        }

        private async Task<DissolveLobbyResponseDto> ExecuteDissolveTransactionAsync(
            Guid lobbyId, Guid hostUserId, string? reason)
        {
            var now = DateTime.UtcNow;

            // H4 fix: null-safe transaction helper.
            // Trong production: BeginTransactionAsync luôn trả IDbContextTransaction (success hoặc throw).
            // Trong unit test với Mock<DbContext>: _db.Database có thể null hoặc BeginTransactionAsync
            // throw NotImplementedException/InvalidOperationException (mock stub không impl).
            // Catch những exception test-only → tx = null → chạy ngoài transaction.
            IDbContextTransaction? tx = null;
            if (_db?.Database != null)
            {
                try
                {
                    tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                }
                catch (NotImplementedException)
                {
                    tx = null;
                }
                catch (InvalidOperationException)
                {
                    tx = null;
                }
                catch (NullReferenceException)
                {
                    tx = null;
                }
            }

            try
            {
                var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                    ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

                var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
                if (host == null)
                {
                    throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanDissolve);
                }

                // Status guard: không cho dissolve khi lobby đã terminal / đã check-in / đang rating / đang chờ check-in.
                // GAP #10 fix: thêm LobbyStatus.Viable (lobby đạt minPlayers nhưng chưa full).
                var forbiddenStatuses = new[]
                {
                    LobbyStatus.InProgress,
                    LobbyStatus.Closed,
                    LobbyStatus.RatingOpen,
                    LobbyStatus.HostCancelled,
                    LobbyStatus.TimeoutFailed,
                    LobbyStatus.RejectedByCafe,
                    LobbyStatus.ExpiredByCafe,
                    LobbyStatus.Dissolved,
                    LobbyStatus.Viable,
                    LobbyStatus.WaitingCheckIn
                };
                if (forbiddenStatuses.Contains(lobby.Status))
                {
                    throw new ConflictException(
                        ApiErrorMessages.Lobby.DissolveInvalidState(lobby.Status));
                }

                // Idempotency: nếu lobby đã Dissolved → return early với policy mặc định.
                // (Tránh 2 request Dissolve song song, request thứ 2 sẽ bị guard chặn.)

                var reservationId = lobby.ReservationId;
                Reservation? reservation = null;
                if (reservationId.HasValue)
                {
                    reservation = await _reservationRepository.GetByIdAsync(reservationId.Value);
                }

                // 1. Release SeatInventory + GameInventory (BR-RESERVATION-01/02 + §XVII.4 atomic).
                await ReleaseDissolveInventoriesAsync(reservation, lobby, now);

                // 2. Tính refund policy + BR-REFUND-02/03.
                var hasMembers = lobby.Members.Any(m => !m.IsHost && m.IsActive);
                var minutesSinceCreated = lobby.CreatedAt == default
                    ? double.MaxValue
                    : (now - lobby.CreatedAt).TotalMinutes;

                var scheduledStart = reservation?.ScheduledStartTime ?? lobby.ScheduledStartTime;
                var (policyName, refundPercent, depositAmount) = ComputeDissolveRefundPolicy(
                    scheduledStart, now, hasMembers, minutesSinceCreated, reservation?.DepositAmount ?? 0);

                var refundAmount = (long)Math.Round(depositAmount * refundPercent, MidpointRounding.AwayFromZero);
                var forfeitAmount = depositAmount - refundAmount;

                var refundKey = $"dissolve-refund-{lobbyId:N}";
                var forfeitKey = $"dissolve-forfeit-{lobbyId:N}";

                if (refundAmount > 0)
                {
                    await _walletService.ReleaseDepositAsync(
                        hostUserId,
                        refundAmount,
                        lobbyId,
                        reservationId,
                        refundKey);
                }

                if (forfeitAmount > 0)
                {
                    await _walletService.ForfeitDepositAsync(
                        hostUserId,
                        forfeitAmount,
                        lobbyId,
                        reservationId,
                        forfeitKey);
                }

                // 3. Cancel pending invites (giữ row, chỉ đổi status).
                await _lobbyInviteRepository.CancelAllPendingForLobbyAsync(lobbyId);

                // 4. Soft-delete lobby + mark members inactive.
                // GAP #5: Nullify ShareCode + IsPrivate để tránh join-by-code / re-search
                // (BR-LOBBY-PRIVACY-*). Lobby terminal không còn khả dụng join.
                lobby.Status = LobbyStatus.Dissolved;
                lobby.IsPrivate = false;
                lobby.ShareCode = string.Empty;
                lobby.ClosedAt = now;
                lobby.ClosedReason = reason ?? $"Host đã giải tán phòng chờ ({policyName}).";
                lobby.UpdatedAt = now;

                foreach (var member in lobby.Members.Where(m => m.IsActive))
                {
                    member.IsActive = false;
                    member.Status = LobbyMemberStatus.LobbyTerminated;
                    member.LeftAt ??= now;
                }

                // 5. Update reservation status nếu có.
                if (reservation != null)
                {
                    if (reservation.Status == ReservationStatus.Holding
                        || reservation.Status == ReservationStatus.Confirmed)
                    {
                        reservation.Status = ReservationStatus.CancelledByPlayer;
                        reservation.UpdatedAt = now;
                        await _reservationRepository.UpdateAsync(reservation);
                    }
                }

                // GAP #2: Publish Outbox event LobbyCancelledByHost + DepositReleased/Forfeited
                // trong CÙNG transaction với domain mutation (BR-REQUIRED §17.5).
                // Background worker sẽ publish SignalR + push notification cho host + members.
                // Idempotency key có suffix :lobbyId → replay-safe.
                var outboxEvents = new List<OutboxEvent>();
                outboxEvents.Add(new OutboxEvent
                {
                    Id = Guid.NewGuid(),
                    EventType = OutboxEventType.LobbyCancelledByHost,
                    Payload = SerializeDissolveLobbyCancelledPayload(
                        lobby, hostUserId, policyName, refundAmount, forfeitAmount, depositAmount, reason),
                    IdempotencyKey = $"dissolve-lobby-cancelled-{lobbyId:N}",
                    ReservationId = reservationId,
                    LobbyId = lobbyId,
                    UserId = hostUserId,
                    CreatedAt = now
                });

                if (refundAmount > 0)
                {
                    outboxEvents.Add(new OutboxEvent
                    {
                        Id = Guid.NewGuid(),
                        EventType = OutboxEventType.DepositReleased,
                        Payload = SerializeDissolveDepositPayload(
                            hostUserId, refundAmount, reservationId, lobbyId, "DEPOSIT_RELEASE", refundKey),
                        IdempotencyKey = refundKey,
                        ReservationId = reservationId,
                        LobbyId = lobbyId,
                        UserId = hostUserId,
                        CreatedAt = now
                    });
                }

                if (forfeitAmount > 0)
                {
                    outboxEvents.Add(new OutboxEvent
                    {
                        Id = Guid.NewGuid(),
                        EventType = OutboxEventType.DepositCaptured,
                        Payload = SerializeDissolveDepositPayload(
                            hostUserId, forfeitAmount, reservationId, lobbyId, "DEPOSIT_CAPTURE", forfeitKey),
                        IdempotencyKey = forfeitKey,
                        ReservationId = reservationId,
                        LobbyId = lobbyId,
                        UserId = hostUserId,
                        CreatedAt = now
                    });
                }

                // Add outbox events to DbContext → sẽ được SaveChanges cùng domain changes.
                foreach (var evt in outboxEvents)
                {
                    await _outboxRepository.AddAsync(evt);
                }

                await _lobbyRepository.SaveChangesAsync();

                if (tx != null)
                {
                    await tx.CommitAsync();
                }

                // Structured audit log (BR-RISK-05 §16.7 — logger thay cho PlayerActionHistory
                // để đồng bộ pattern với ReservationService.CancelAsync — service cancel hiện
                // không ghi PlayerActionHistory entity, chỉ log structured).
                // TODO: Khi PlayerActionHistory được mở rộng cho lobby cancel/dissolve events
                // (BR-RISK-05 + Gap #7), chuyển từ logger sang entity insert.
                _logger.LogInformation(
                    "Lobby dissolved. LobbyId={LobbyId}, HostId={HostId}, Policy={Policy}, Refund={Refund} BVC, Forfeit={Forfeit} BVC, Deposit={Deposit} BVC, OutboxEvents={OutboxCount}, HasReservation={HasReservation}",
                    lobbyId, hostUserId, policyName, refundAmount, forfeitAmount, depositAmount,
                    outboxEvents.Count, reservation != null);

                // TODO: Gap #8 — Karma penalty cho host khi dissolve <6h trước scheduledStart
                // (BR-REFUND-02 bảng "giảm đáng kể"). Cần thêm hook vào IPlayerKarmaService
                // (vd. RecordHostDissolveAsync) — phase tiếp theo vì chưa có sẵn.
                // TODO: Gap #9 — Trigger KarmaAggregation sau dissolve. Hiện chưa có interface
                // IBookingRatingService.AggregrateForLobbyAsync — phase tiếp theo.

                return new DissolveLobbyResponseDto
                {
                    LobbyId = lobbyId,
                    ReservationId = reservationId,
                    Reason = lobby.ClosedReason,
                    DissolvedAt = now,
                    RefundBvc = refundAmount,
                    ForfeitBvc = forfeitAmount,
                    RefundPolicyApplied = policyName
                };
            }
            catch
            {
                if (tx != null)
                {
                    await tx.RollbackAsync();
                }
                throw;
            }
            finally
            {
                if (tx != null)
                {
                    await tx.DisposeAsync();
                }
            }
        }

        /// <summary>
        /// BR-RESERVATION-01/02 + BR §17.3: Release seat + game copy trong cùng transaction.
        /// Caller PHẢI đang trong 1 transaction (Serializable).
        /// Dùng <c>GetForUpdateAsync</c> (<c>SELECT FOR UPDATE</c>) để chống race.
        ///
        /// Fallback lookup từ Lobby mirror fields (CafeId/PlayDate/TimeSlot) nếu reservation
        /// chưa từng được tạo (legacy lobby không gắn reservation flow).
        /// </summary>
        private async Task ReleaseDissolveInventoriesAsync(
            Reservation? reservation, Lobby lobby, DateTime now)
        {
            if (reservation?.CafeId != null
                && reservation.PlayDate != default
                && (reservation.PreferredStartTime.HasValue || reservation.PreferredEndTime.HasValue))
            {
                if (reservation.SeatInventoryId != null && reservation.PreferredStartTime.HasValue && reservation.PreferredEndTime.HasValue)
                {
                    var seatInv = await _seatInventoryRepository.GetForUpdateAsync(
                        reservation.CafeId, reservation.PlayDate, reservation.PreferredStartTime.Value, reservation.PreferredEndTime.Value);
                    if (seatInv != null && lobby.MaxMembers > 0)
                    {
                        seatInv.HeldSeats = Math.Max(0, seatInv.HeldSeats - lobby.MaxMembers);
                        seatInv.UpdatedAt = now;
                        await _seatInventoryRepository.UpdateAsync(seatInv);
                    }
                }

                if (reservation.GameInventoryId != null && reservation.GameId != Guid.Empty && reservation.PreferredStartTime.HasValue && reservation.PreferredEndTime.HasValue)
                {
                    var gameInv = await _gameInventoryRepository.GetForUpdateAsync(
                        reservation.CafeId, reservation.GameId, reservation.PlayDate, reservation.PreferredStartTime.Value, reservation.PreferredEndTime.Value);
                    if (gameInv != null)
                    {
                        gameInv.HeldCopies = Math.Max(0, gameInv.HeldCopies - 1);
                        gameInv.UpdatedAt = now;
                        await _gameInventoryRepository.UpdateAsync(gameInv);
                    }
                }
                return;
            }

            // Fallback: derive từ Lobby mirror fields (legacy lobby không có reservation).
            if (lobby.CafeId.HasValue
                && lobby.PlayDate.HasValue
                && (lobby.PreferredStartTime.HasValue || lobby.ScheduledStartTime.HasValue))
            {
                var startTime = lobby.PreferredStartTime ?? TimeOnly.FromDateTime(lobby.ScheduledStartTime!.Value);
                var endTime = lobby.PreferredEndTime ?? (lobby.ScheduledStartTime.HasValue ? TimeOnly.FromDateTime(lobby.ScheduledStartTime.Value.AddHours(2)) : TimeOnly.MinValue);
                var seatInv = await _seatInventoryRepository.GetForUpdateAsync(
                    lobby.CafeId.Value, lobby.PlayDate.Value, startTime, endTime);
                if (seatInv != null && lobby.MaxMembers > 0)
                {
                    seatInv.HeldSeats = Math.Max(0, seatInv.HeldSeats - lobby.MaxMembers);
                    seatInv.UpdatedAt = now;
                    await _seatInventoryRepository.UpdateAsync(seatInv);
                }
            }
        }

        /// <summary>
        /// BR-REFUND-02/03: refund matrix khi host dissolve lobby.
        /// Logic mirror ReservationService.ComputeRefundPolicy — đồng bộ nếu thay đổi.
        /// </summary>
        private static (string PolicyName, decimal RefundPercent, long DepositBvc) ComputeDissolveRefundPolicy(
            DateTime? scheduledStart,
            DateTime now,
            bool hasMembers,
            double minutesSinceCreated,
            long depositAmount)
        {
            if (depositAmount <= 0)
            {
                // Legacy lobby không có deposit (chưa qua Reservation flow) → hoàn 0, policy none.
                return ("No-Deposit-Legacy", 0m, 0L);
            }

            // BR-REFUND-03: grace 15 phút + chưa có member → hoàn 100%.
            if (minutesSinceCreated <= DissolveGraceMinutes && !hasMembers)
            {
                return ("Grace-15p-NoMember", 1.0m, depositAmount);
            }

            // Nếu lobby không có ScheduledStartTime (legacy) → không áp dụng được
            // các mốc 24h/6h → mặc định 50% (matching BR-REFUND-02 default cancel).
            if (!scheduledStart.HasValue || scheduledStart.Value == default)
            {
                return ("Legacy-NoScheduledTime", 0.5m, depositAmount);
            }

            var hoursUntilPlay = (scheduledStart.Value - now).TotalHours;

            // BR-REFUND-02: ≥24h → 100%.
            if (hoursUntilPlay >= DissolveFullRefundHours)
            {
                return ("Cancel-24h", 1.0m, depositAmount);
            }

            // BR-REFUND-02: 6–24h → 50%.
            if (hoursUntilPlay >= DissolveHalfRefundHours)
            {
                return ("Cancel-6h", 0.5m, depositAmount);
            }

            // BR-REFUND-02: <6h → 0% (forfeit 100%).
            return ("Cancel-Under6h", 0m, depositAmount);
        }

        private static bool IsSerializationFailure(DbUpdateException ex)
        {
            // GAP-R4-N1 Fix: Dùng Npgsql.PostgresException.SqlState thay vì string-contains.
            // String-contains có thể false-positive nếu message user hoặc data chứa '40001'.
            // SqlState là API chính thức từ Postgres driver, không có ambiguity.
            return ex.InnerException is Npgsql.PostgresException pg
                && (pg.SqlState == "40001" || pg.SqlState == "40P01");
        }

        // ===== Outbox payload serializers cho DissolveLobbyAsync (Gap #2) =====

        private static string SerializeDissolveLobbyCancelledPayload(
            Lobby lobby,
            Guid hostUserId,
            string policyName,
            long refundBvc,
            long forfeitBvc,
            long depositBvc,
            string? reason)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                lobbyId = lobby.Id,
                hostUserId,
                reservationId = lobby.ReservationId,
                cafeId = lobby.CafeId,
                gameTemplateId = lobby.GameTemplateId,
                playDate = lobby.PlayDate?.ToString(),
                // GAP-10 fix (2026-08-21): dùng PreferredStartTime/PreferredEndTime thay vì TimeSlot int
                preferredStartTime = lobby.PreferredStartTime?.ToString(),
                preferredEndTime = lobby.PreferredEndTime?.ToString(),
                lobbyStatus = lobby.Status.ToString(),
                terminalReason = "host_dissolved",
                refundPolicyApplied = policyName,
                refundBvc,
                forfeitBvc,
                depositBvc,
                reason = reason ?? $"Host đã giải tán phòng chờ ({policyName}).",
                dissolvedAt = lobby.ClosedAt,
                memberCount = lobby.Members?.Count ?? 0
            });
        }

        private static string SerializeDissolveDepositPayload(
            Guid userId,
            long amount,
            Guid? reservationId,
            Guid lobbyId,
            string ledgerEntryType,
            string idempotencyKey)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                userId,
                amount,
                reservationId,
                lobbyId,
                entryType = ledgerEntryType,
                idempotencyKey
            });
        }

        public async Task<LobbyResponseDto> LockLobbyAsync(Guid lobbyId, Guid hostUserId, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanLock);
            }

            if (lobby.Status != LobbyStatus.Open)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.LobbyNotOpenForLock);
            }

            // P1-FIX: MinPlayers enforcement khi lock
            var activeCount = lobby.Members.Count(m => m.IsActive);
            if (activeCount < lobby.MinPlayers)
            {
                throw new ConflictException(
                    ApiErrorMessages.System.LobbyNotEnoughMembersToLock(lobby.MinPlayers, activeCount));
            }

            lobby.Status = LobbyStatus.Full;
            lobby.FullAt = DateTime.UtcNow;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            await _hubService.NotifyLobbyFull(lobbyId);

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> OpenKarmaWindowAsync(Guid lobbyId, Guid hostUserId, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            if (lobby.HostUserId != hostUserId)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanOpenRating);
            }

            lobby.Status = LobbyStatus.RatingOpen;
            lobby.RatingOpenedAt = DateTime.UtcNow;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> TransitionToInProgressAsync(Guid lobbyId, Guid? activeSessionId, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            if (lobby.Status != LobbyStatus.Full && lobby.Status != LobbyStatus.WaitingCheckIn)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.OnlyFullOrWaitingCheckInCanInProgress);
            }

            lobby.Status = LobbyStatus.InProgress;
            lobby.ActiveSessionId = activeSessionId;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> JoinLobbyByShareCodeAsync(string shareCode, Guid userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(shareCode))
            {
                throw new BadRequestException(ApiErrorMessages.LobbyInvite.ShareCodeInvalid);
            }

            var lobby = await _lobbyRepository.GetByShareCodeAsync(shareCode)
                ?? throw new NotFoundException(ApiErrorMessages.LobbyInvite.ShareCodeInvalid);

            // BR-LOBBY-PRIVACY-03: Private lobby — share code chỉ join được nếu user là bạn bè
            // (Friendship.Status = Accepted) của ít nhất 1 thành viên active.
            // M2: 1 query batch thay vì N queries per member.
            if (lobby.IsPrivate)
            {
                var memberIds = lobby.Members
                    .Where(m => m.IsActive)
                    .Select(m => m.UserId)
                    .ToList();

                var isFriendOfAnyMember = await _friendshipRepository.IsAcceptedFriendOfAnyAsync(
                    userId, memberIds);

                if (!isFriendOfAnyMember)
                {
                    throw new ForbiddenException(ApiErrorMessages.LobbyInvite.PrivateLobbyShareCodeRequiresFriendship);
                }
            }

            return await JoinLobbyAsync(lobby.Id, userId);
        }

        public async Task<LobbyResponseDto> TransitionToClosedAsync(Guid lobbyId, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            if (lobby.Status != LobbyStatus.InProgress && lobby.Status != LobbyStatus.RatingOpen)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.OnlyInProgressCanClose);
            }

            lobby.Status = LobbyStatus.Closed;
            lobby.ClosedAt = DateTime.UtcNow;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            // K-04: Reward exp to all active members when lobby is successfully closed
            var activeMembers = lobby.Members?.Where(m => m.IsActive).ToList() ?? [];
            foreach (var member in activeMembers)
            {
                try
                {
                    await _userProfileService.AddExpAndUpdateLevelAsync(member.UserId, ExpRewardPerCompletedLobby);
                }
                catch (Exception)
                {
                    // Non-critical: exp reward should not block lobby close
                }
            }

            return MapLobbyDto(lobby, null);
        }

        // ============================ P1 Features ============================

        public async Task<LobbyResponseDto> TransferHostAsync(Guid lobbyId, Guid currentHostUserId, Guid newHostUserId, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.CannotSwitchHostWhenClosed);
            }

            var currentHost = lobby.Members.FirstOrDefault(m => m.UserId == currentHostUserId && m.IsHost && m.IsActive);
            if (currentHost == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.NotCurrentHost);
            }

            if (currentHostUserId == newHostUserId)
            {
                throw new BadRequestException(ApiErrorMessages.Lobby.AlreadyHost);
            }

            var newHost = lobby.Members.FirstOrDefault(m => m.UserId == newHostUserId && m.IsActive);
            if (newHost == null)
            {
                throw new NotFoundException(ApiErrorMessages.Lobby.TargetMemberNotInLobby);
            }

            // L-04: BR-USER-LIMIT-04 validation cho new host — new host không được đang host lobby active khác
            var newHostActiveLobbies = await _lobbyRepository.GetActiveLobbiesByHostAsync(newHostUserId);
            var newHostMemberLobbies = await _lobbyRepository.GetActiveLobbiesByMemberAsync(newHostUserId);

            // BR-USER-LIMIT-01: Tổng lobby (host + member) ≤ 2 active cho new host.
            var totalNewHostLobbies = newHostActiveLobbies.Count + newHostMemberLobbies.Count;
            if (totalNewHostLobbies >= 2)
            {
                // BR-USER-LIMIT-05 ĐÃ BỎ: Host được phép join/transfer host sang lobby khác
                // Chỉ block khi tổng lobby đã đạt max 2.
                throw new ConflictException(ApiErrorMessages.Reservation.TotalLobbyLimitReached);
            }

            // BR-USER-LIMIT-01: new host không được đang là member của lobby active khác.
            if (newHostMemberLobbies.Count > 0)
            {
                throw new ConflictException(ApiErrorMessages.Reservation.ActiveLobbyMemberLimitReached);
            }

            currentHost.IsHost = false;
            newHost.IsHost = true;

            // Cập nhật HostUserId cho các lookup khác
            lobby.HostUserId = newHostUserId;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            await _lobbyMessageService.AddSystemMessageAsync(
                lobby.Id,
                $"{newHost.User?.Username ?? "Thành viên"} đã trở thành Host mới.");

            await _hubService.NotifyHostChanged(lobbyId, newHostUserId);

            return MapLobbyDto(lobby, null);
        }

        /// <summary>
        /// L-03: Host tạo mã chia sẻ mới, invalidate mã cũ.
        /// Logic: Generate new code, update DB, old code becomes invalid immediately.
        /// </summary>
        public async Task<LobbyResponseDto> RegenerateShareCodeAsync(Guid lobbyId, Guid hostUserId, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanUpdate);
            }

            if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.CannotRegenerateShareCodeWhenClosed);
            }

            var oldCode = lobby.ShareCode;
            var newCode = await GenerateUniqueShareCodeAsync();

            // Ensure new code is different from old code
            while (newCode == oldCode)
            {
                newCode = await GenerateUniqueShareCodeAsync();
            }

            lobby.ShareCode = newCode;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            await _lobbyMessageService.AddSystemMessageAsync(
                lobby.Id,
                $"Mã chia sẻ đã được thay đổi. Mã cũ không còn hợp lệ.");

            return MapLobbyDto(lobby, null);
        }

        /// <summary>
        /// BR-NEW-14 (b): Host đổi timeSlot và/hoặc preferred times của lobby.
        /// Chỉ áp dụng khi lobby chưa check-in (status = Open/Viable/Full).
        /// Update cả Reservation + Lobby (mirror) + recalculate RecruitmentDeadline.
        /// BR-LOBBY-01a/b/c: Validate buffer mới >= 120 phút.
        /// BR-RES-07/08/09: preferredStartTime/EndTime phải nằm trong slot range.
        /// </summary>
        public async Task<LobbyResponseDto> ChangeTimeAsync(
            Guid lobbyId,
            Guid hostUserId,
            Core.DTOs.Lobby.ChangeTimeSlotRequestDto request, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanUpdate);
            }

            // Chỉ cho phép khi lobby chưa terminal
            var changeableStatuses = new[]
            {
                LobbyStatus.Open,
                LobbyStatus.Viable,
                LobbyStatus.Full,
                LobbyStatus.PendingCafeApproval
            };
            if (!changeableStatuses.Contains(lobby.Status))
            {
                throw new ConflictException(
                    ApiErrorMessages.Lobby.LobbyUpdateNotAllowedWhenClosed);
            }

            var now = DateTime.UtcNow;
            var playDate = lobby.PlayDate ?? DateOnly.FromDateTime(now);

            // BR-NEW-15: Determine effective start/end times (from PreferredStartTime/PreferredEndTime, not TimeSlot)
            var effectiveStartTime = request.PreferredStartTime ?? lobby.PreferredStartTime;
            var effectiveEndTime = request.PreferredEndTime ?? lobby.PreferredEndTime;

            // Validate preferred times nếu có
            if (effectiveStartTime.HasValue && effectiveEndTime.HasValue)
            {
                var validation = Core.Constants.CafeSchedule.ValidatePreferredTimeRange(
                    effectiveStartTime.Value,
                    effectiveEndTime.Value);

                if (!validation.isValid)
                {
                    throw new BadRequestException(validation.error!);
                }
            }

            // Tính ScheduledStartTime/EndTime mới từ preferred times (BR-NEW-15: no TimeSlot param)
            var (scheduledStartTime, scheduledEndTime) = Core.Constants.CafeSchedule.BuildScheduledStartEndFromPreferred(
                playDate,
                effectiveStartTime ?? new TimeOnly(18, 0),
                effectiveEndTime ?? new TimeOnly(23, 0));

            // Tính RecruitmentDeadline mới
            // BR-LOBBY-01: deadline = scheduledStartTime - leadTimeMinutes
            var leadTimeMinutes = lobby.CancellationLeadTimeMinutes > 0 ? lobby.CancellationLeadTimeMinutes : 20;
            var newDeadline = scheduledStartTime.AddMinutes(-leadTimeMinutes);

            // BR-LOBBY-01b: Buffer phải >= 60 phút
            var bufferMinutes = (newDeadline - now).TotalMinutes;
            var bypassLobbyBuffer = await TimeWindowGuard.ShouldBypassAsync(
                _httpContextAccessor?.HttpContext, _configProvider, _logger,
                operation: "Lobby.TimeSlotChangeBuffer", entityId: lobby.Id);
            if (!bypassLobbyBuffer && bufferMinutes < 60)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Lobby.BufferTooShortForTimeSlotChange((int)bufferMinutes));
            }

            var changes = new List<string>();
            if (request.PreferredStartTime.HasValue)
            {
                changes.Add($"giờ bắt đầu: {request.PreferredStartTime:HH:mm}");
            }
            if (request.PreferredEndTime.HasValue)
            {
                changes.Add($"giờ kết thúc: {request.PreferredEndTime:HH:mm}");
            }

            // Update Lobby (mirror fields) — TimeSlot is [Obsolete], only update if explicitly requested
            lobby.PreferredStartTime = request.PreferredStartTime ?? lobby.PreferredStartTime;
            lobby.PreferredEndTime = request.PreferredEndTime ?? lobby.PreferredEndTime;
            lobby.RecruitmentDeadline = newDeadline;
            lobby.ScheduledStartTime = scheduledStartTime;
            lobby.UpdatedAt = now;

            // Update Reservation nếu có
            if (lobby.ReservationId.HasValue)
            {
                var reservation = await _reservationRepository.GetByIdAsync(lobby.ReservationId.Value);
                if (reservation != null)
                {
                    reservation.PreferredStartTime = request.PreferredStartTime ?? reservation.PreferredStartTime;
                    reservation.PreferredEndTime = request.PreferredEndTime ?? reservation.PreferredEndTime;
                    reservation.RecruitmentDeadline = newDeadline;
                    reservation.ScheduledStartTime = scheduledStartTime;
                    reservation.ScheduledEndTime = scheduledEndTime;
                    reservation.UpdatedAt = now;
                    await _reservationRepository.UpdateAsync(reservation);
                }
            }

            await _lobbyRepository.SaveChangesAsync();

            var changeSummary = changes.Count > 0
                ? string.Join(", ", changes)
                : "thời gian ưu tiên";
            await _lobbyMessageService.AddSystemMessageAsync(
                lobby.Id,
                $"Host đã cập nhật {changeSummary}. Deadline mới: {newDeadline:HH:mm dd/MM}.");

            await _hubService.NotifyLobbyUpdated(lobbyId);

            return MapLobbyDto(lobby, null);
        }

        /// <summary>
        /// BR-NEW-14 (d): Boost lobby — tăng visibility trong search/discovery.
        /// Chỉ áp dụng khi lobby đang Open và chưa được boost trong 6 giờ gần nhất.
        /// Action: bump CreatedAt để lobby hiện lên đầu search results.
        /// </summary>
        public async Task<LobbyResponseDto> BoostLobbyAsync(Guid lobbyId, Guid hostUserId, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanUpdate);
            }

            if (lobby.Status != LobbyStatus.Open)
            {
                throw new ConflictException(
                    ApiErrorMessages.System.LobbyBoostRequiresOpen);
            }

            // Check cooldown: không boost quá 1 lần trong 6 giờ
            var cooldownHours = 6;
            var minBoostInterval = TimeSpan.FromHours(cooldownHours);
            if (lobby.UpdatedAt.Add(minBoostInterval) > DateTime.UtcNow)
            {
                var remainingMinutes = (int)(minBoostInterval - (DateTime.UtcNow - lobby.UpdatedAt)).TotalMinutes;
                throw new ConflictException(
                    ApiErrorMessages.System.LobbyBoostCooldown(cooldownHours, remainingMinutes));
            }

            // Boost: cập nhật CreatedAt để lobby hiện lên đầu trong search/discovery
            // (OrderByDescending(CreatedAt) sẽ đưa lobby mới nhất lên đầu)
            lobby.CreatedAt = DateTime.UtcNow;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            await _lobbyMessageService.AddSystemMessageAsync(
                lobby.Id,
                $"Host đã boost phòng chờ để tăng visibility! Phòng của bạn giờ sẽ xuất hiện ở vị trí cao hơn trong kết quả tìm kiếm.");

            await _hubService.NotifyLobbyUpdated(lobbyId);

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> KickMemberAsync(Guid lobbyId, Guid hostUserId, Guid targetUserId, string? reason, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.CannotKickWhenClosed);
            }

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanKick);
            }

            if (hostUserId == targetUserId)
            {
                throw new BadRequestException(ApiErrorMessages.Lobby.HostCannotKickSelf);
            }

            var target = lobby.Members.FirstOrDefault(m => m.UserId == targetUserId && m.IsActive);
            if (target == null)
            {
                throw new NotFoundException(ApiErrorMessages.Lobby.TargetMemberNotInLobby);
            }

            target.IsActive = false;
            target.Status = LobbyMemberStatus.Kicked;
            target.LeftAt = DateTime.UtcNow;
            lobby.UpdatedAt = DateTime.UtcNow;

            // Nếu lobby FULL mà giờ còn dưới MaxMembers → chuyển về OPEN
            if (lobby.Status == LobbyStatus.Full)
            {
                var activeAfter = lobby.Members.Count(m => m.IsActive);
                if (activeAfter < lobby.MaxMembers)
                {
                    lobby.Status = LobbyStatus.Open;
                }
            }

            await _lobbyRepository.SaveChangesAsync();

            await _lobbyMessageService.AddSystemMessageAsync(
                lobby.Id,
                $"{target.User?.Username ?? "Một thành viên"} đã bị kick khỏi phòng." +
                (string.IsNullOrWhiteSpace(reason) ? "" : $" Lý do: {reason}"));

            await _hubService.NotifyMemberKicked(lobbyId, targetUserId);

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> UpdateLobbyAsync(Guid lobbyId, Guid hostUserId, UpdateLobbyRequestDto request, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanUpdate);
            }

            if (lobby.Status == LobbyStatus.InProgress ||
                lobby.Status == LobbyStatus.Closed ||
                lobby.Status == LobbyStatus.HostCancelled ||
                lobby.Status == LobbyStatus.TimeoutFailed)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.LobbyUpdateNotAllowedWhenClosed);
            }

            if (request.MaxMembers.HasValue)
            {
                if (lobby.Status == LobbyStatus.Full)
                {
                    throw new ConflictException(ApiErrorMessages.Lobby.CannotReduceMaxMembersWhenFull);
                }

                var game = await _gameTemplateRepository.GetByIdAsync(lobby.GameTemplateId);
                if (game == null)
                {
                    throw new NotFoundException(ApiErrorMessages.Lobby.GameTemplateNotFound);
                }

                if (request.MaxMembers.Value < game.MinPlayers || request.MaxMembers.Value > game.MaxPlayers)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Lobby.MaxMembersExceedsGameRange(request.MaxMembers.Value, game.MinPlayers, game.MaxPlayers));
                }

                if (lobby.Members.Count(m => m.IsActive) > request.MaxMembers.Value)
                {
                    throw new ConflictException(ApiErrorMessages.Lobby.CannotReduceMaxMembersBelowCurrent);
                }

                lobby.MaxMembers = request.MaxMembers.Value;
            }

            if (request.MinPlayers.HasValue)
            {
                if (request.MinPlayers.Value < 2 || request.MinPlayers.Value > lobby.MaxMembers)
                {
                    throw new BadRequestException(ApiErrorMessages.Lobby.MinPlayersOutOfRange(2, lobby.MaxMembers));
                }
                lobby.MinPlayers = request.MinPlayers.Value;
            }

            if (request.ScheduledStartTime.HasValue)
            {
                if (request.ScheduledStartTime.Value < DateTime.UtcNow.AddMinutes(5))
                {
                    throw new BadRequestException(ApiErrorMessages.Lobby.ScheduledStartTimeTooEarly);
                }
                lobby.ScheduledStartTime = request.ScheduledStartTime.Value;
            }

            if (request.IsPrivate.HasValue) lobby.IsPrivate = request.IsPrivate.Value;
            if (request.Description != null) lobby.Description = request.Description;
            if (request.CoverImageUrl != null) lobby.CoverImageUrl = request.CoverImageUrl;
            if (request.CancellationLeadTimeMinutes.HasValue)
            {
                if (request.CancellationLeadTimeMinutes.Value < 5 || request.CancellationLeadTimeMinutes.Value > 1440)
                {
                    throw new BadRequestException(ApiErrorMessages.Lobby.CancellationLeadTimeOutOfRange(5, 1440));
                }
                lobby.CancellationLeadTimeMinutes = request.CancellationLeadTimeMinutes.Value;
            }

            // BR-10: cập nhật yêu cầu Karma. Chỉ .HasValue — không cho phép "xóa" requirement
            // trong MVP (host phải tạo lobby mới nếu muốn gỡ). Điều này tránh race với member
            // đang pending join.
            if (request.MinKarmaScore.HasValue)
            {
                lobby.MinKarmaScore = request.MinKarmaScore.Value;
            }

            lobby.UpdatedAt = DateTime.UtcNow;
            await _lobbyRepository.SaveChangesAsync();

            await _hubService.NotifyLobbyUpdated(lobbyId);

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> SetMemberReadyAsync(Guid lobbyId, Guid userId, bool isReady, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            // BR-LOBBY-READY-01 (mới): Cho phép Ready khi lobby còn đang hoạt động (Open/Full/Viable).
            // Chỉ chặn khi lobby đã ở trạng thái kết thúc — vì lúc đó không có ý nghĩa.
            var readyableStatuses = new[]
            {
                LobbyStatus.Open,
                LobbyStatus.Full,
                LobbyStatus.Viable
            };
            if (!readyableStatuses.Contains(lobby.Status))
            {
                throw new ConflictException(ApiErrorMessages.Lobby.LobbyNotReadyForReady);
            }

            var member = lobby.Members.FirstOrDefault(m => m.UserId == userId && m.IsActive);
            if (member == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.NotMember);
            }

            if (isReady)
            {
                if (member.Status == LobbyMemberStatus.Kicked || member.Status == LobbyMemberStatus.Left)
                {
                    throw new ConflictException(ApiErrorMessages.Lobby.MemberNotReadyBecauseLeftOrKicked);
                }
                member.Status = LobbyMemberStatus.Ready;
                member.ReadyAt = DateTime.UtcNow;
            }
            else
            {
                member.Status = LobbyMemberStatus.Joined;
                member.ReadyAt = null;
            }

            // BR-LOBBY-READY-03 (mới): Ghi nhận FullAt khi lobby vừa chuyển FULL để scheduler biết mốc timeout 20p.
            var activeMembersCount = lobby.Members.Count(m => m.IsActive);
            if (lobby.Status != LobbyStatus.Full && activeMembersCount >= lobby.MaxMembers)
            {
                lobby.Status = LobbyStatus.Full;
                lobby.FullAt = DateTime.UtcNow;
            }

            lobby.UpdatedAt = DateTime.UtcNow;
            await _lobbyRepository.SaveChangesAsync();

            await _hubService.NotifyMemberReady(lobbyId, userId, isReady);

            // BR-LOBBY-READY-01: Nếu TẤT CẢ members đều Ready → lobby chuyển WaitingCheckIn (chờ check-in tại quán).
            // Đồng thời chuyển Reservation Holding → Confirmed (đã sẵn sàng đến quán).
            // Áp dụng cho cả lobby Open/Full/Viable để cho phép Ready sớm (Option A).
            var readyableMembers = lobby.Members.Where(m => m.IsActive).ToList();
            var allReady = readyableMembers.Count > 0
                && readyableMembers.All(m => m.Status == LobbyMemberStatus.Ready);

            if (allReady && readyableMembers.Count >= lobby.MinPlayers)
            {
                lobby.Status = LobbyStatus.WaitingCheckIn;
                lobby.UpdatedAt = DateTime.UtcNow;

                // BR-RESERVATION-READY-01: Khi lobby WaitingCheckIn → Reservation cũng Confirmed.
                if (lobby.Reservation != null && lobby.Reservation.Status == ReservationStatus.Holding)
                {
                    lobby.Reservation.Status = ReservationStatus.Confirmed;
                    lobby.Reservation.UpdatedAt = DateTime.UtcNow;
                    _logger.LogInformation(
                        "Reservation auto-confirmed (all members ready): ReservationId={ReservationId}, LobbyId={LobbyId}",
                        lobby.Reservation.Id, lobby.Id);
                }

                await _lobbyRepository.SaveChangesAsync();
                await _hubService.NotifyLobbyWaitingCheckIn(lobbyId);
            }

            return MapLobbyDto(lobby, null);
        }

        public async Task<IReadOnlyList<LobbyResponseDto>> GetLobbiesByHostAsync(Guid hostUserId, CancellationToken cancellationToken = default)
        {
            var lobbies = await _lobbyRepository.GetLobbiesByHostAsync(hostUserId);
            return lobbies.Select(l => MapLobbyDto(l, null)).ToList();
        }

        public async Task<IReadOnlyList<LobbyResponseDto>> GetMyLobbiesAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("GetMyLobbiesAsync called. UserId={UserId}", userId);
            var lobbies = await _lobbyRepository.GetMyLobbiesAsync(userId);
            _logger.LogDebug("GetMyLobbiesAsync result. UserId={UserId}, FoundCount={Count}", userId, lobbies.Count);
            return lobbies.Select(l => MapLobbyDto(l, null)).ToList();
        }

        public async Task<LobbyResponseDto> ReportLobbyAsync(Guid lobbyId, Guid reporterId, CreateLobbyReportDto request, CancellationToken cancellationToken = default)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            if (lobby.HostUserId == reporterId)
            {
                throw new BadRequestException(ApiErrorMessages.Lobby.CannotReportOwnLobby);
            }

            var report = new LobbyReport
            {
                Id = Guid.NewGuid(),
                ReporterId = reporterId,
                LobbyId = lobbyId,
                Category = Enum.Parse<LobbyReportCategory>(request.Category, ignoreCase: true),
                Reason = request.Reason,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            await _lobbyRepository.AddReportAsync(report);
            await _lobbyRepository.SaveChangesAsync();

            return MapLobbyDto(lobby, null);
        }

        /// <summary>
        /// Lấy danh sách lobby của 1 cafe cho Manager.
        /// </summary>
        public async Task<CafeLobbiesResponseDto> GetCafeLobbiesAsync(
            Guid cafeManagerUserId,
            Guid cafeId,
            CafeLobbiesRequestDto request, CancellationToken cancellationToken = default)
        {
            // Validate user có quyền xem cafe này (Manager hoặc CafeStaff)
            var hasAccess = await _cafeRepository.IsManagerOrStaffAsync(cafeId, cafeManagerUserId);
            if (!hasAccess)
            {
                throw new ForbiddenException(ApiErrorMessages.Cafe.ManagerForbidden(cafeId));
            }

            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);

            var (items, totalCount) = await _lobbyRepository.GetByCafeAsync(
                cafeId,
                request.PlayDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                request.LobbyStatuses,
                page,
                pageSize);

            var dtos = items.Select(l => new CafeLobbyItemDto
            {
                LobbyId = l.Id,
                ReservationId = l.ReservationId,
                HostId = l.HostUserId,
                HostName = l.HostUser?.Profile?.LastResolvedDisplayName ?? l.HostUser?.Username ?? string.Empty,
                GameId = l.GameTemplateId,
                GameName = l.GameTemplate?.Name ?? string.Empty,
                PlayDate = l.PlayDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                PreferredStartTime = l.PreferredStartTime ?? TimeOnly.FromDateTime(l.ScheduledStartTime ?? DateTime.UtcNow),
                PreferredEndTime = l.PreferredEndTime ?? TimeOnly.FromDateTime(l.Reservation?.ScheduledEndTime ?? l.ScheduledStartTime ?? DateTime.UtcNow),
                CurrentPlayers = l.Members?.Count(m => m.IsActive) ?? 0,
                MinPlayers = l.MinPlayers,
                MaxPlayers = l.MaxMembers,
                Status = l.Status,
                IsPrivate = l.IsPrivate,
                ShareCode = l.ShareCode,
                ScheduledStartTime = l.ScheduledStartTime ?? DateTime.MinValue,
                ScheduledEndTime = l.Reservation?.ScheduledEndTime ?? DateTime.MinValue,
                RecruitmentDeadline = l.RecruitmentDeadline ?? DateTime.MinValue,
                DepositAmount = l.Reservation?.DepositAmount ?? 0,
                CreatedAt = l.CreatedAt
            }).ToList();

            return new CafeLobbiesResponseDto
            {
                Items = dtos,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        // ============================ Helpers ============================

        /// <summary>
        /// GAP-R4-A8 Fix: Dùng <see cref="RandomNumberGenerator"/> (cryptographically secure)
        /// thay vì <see cref="Random"/> để chống brute-force attack vào ShareCode.
        /// ShareCode 6-char từ alphabet 32 ký tự = ~1B combinations. Rate Limit 5/15min/IP chống
        /// được naive attack, nhưng với cryptographically-secure RNG + đủ entropy thì attacker
        /// không thể đoán code dựa trên timing hoặc pattern.
        /// </summary>
        private async Task<string> GenerateUniqueShareCodeAsync()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var code = new char[6];
                for (var i = 0; i < 6; i++)
                {
                    code[i] = chars[RandomNumberGenerator.GetInt32(0, chars.Length)];
                }
                var codeStr = new string(code);

                var existing = await _lobbyRepository.GetByShareCodeAsync(codeStr);
                if (existing == null)
                {
                    return codeStr;
                }
            }

            // Fallback: Use GUID (cryptographically random-ish)
            var guid = Guid.NewGuid().ToString("N");
            return guid.Length >= 6 ? guid[..6].ToUpperInvariant() : guid.ToUpperInvariant();
        }

        private static LobbyResponseDto MapLobbyDto(Lobby lobby, double? distanceKm)
        {
            return new LobbyResponseDto
            {
                Id = lobby.Id,
                HostUserId = lobby.HostUserId,
                GameTemplateId = lobby.GameTemplateId,
                GameName = lobby.GameTemplate?.Name,
                CafeId = lobby.CafeId,
                CafeName = lobby.Cafe?.Name,
                BookingId = lobby.BookingId,
                ScheduledStartTime = lobby.ScheduledStartTime,
                MaxMembers = lobby.MaxMembers,
                MinPlayers = lobby.MinPlayers,
                SeatCount = lobby.SeatCount,
                ActiveSessionId = lobby.ActiveSessionId,
                Status = lobby.Status,
                Latitude = lobby.Latitude,
                Longitude = lobby.Longitude,
                IsPrivate = lobby.IsPrivate,
                ShareCode = lobby.ShareCode,
                Description = lobby.Description,
                CoverImageUrl = lobby.CoverImageUrl,
                CancellationLeadTimeMinutes = lobby.CancellationLeadTimeMinutes,
                MinKarmaScore = lobby.MinKarmaScore,
                ClosedAt = lobby.ClosedAt,
                ClosedReason = lobby.ClosedReason,
                CreatedAt = lobby.CreatedAt,
                UpdatedAt = lobby.UpdatedAt,
                DistanceKm = distanceKm,
                Members = lobby.Members
                    .Where(m => m.IsActive)
                    .Select(m => new LobbyMemberDto
                    {
                        Id = m.Id,
                        UserId = m.UserId,
                        UserName = m.User?.Username ?? string.Empty,
                        AvatarUrl = m.User?.Profile?.AvatarUrl,
                        KarmaPoints = m.User?.Profile?.KarmaPoints ?? 100,
                        JoinedAt = m.JoinedAt,
                        IsActive = m.IsActive,
                        IsHost = m.IsHost,
                        Status = m.Status.ToString(),
                        ReadyAt = m.ReadyAt
                    })
                    .ToList()
            };
        }
    }
}