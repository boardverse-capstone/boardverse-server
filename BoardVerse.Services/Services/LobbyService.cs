using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using GeoHelper = BoardVerse.Core.Helpers.GeoLocationHelper;

namespace BoardVerse.Services.Services
{
    /// <summary>
    /// Lobby business logic.
    /// Public lobby: any user can join via /search.
    /// Private lobby: chỉ join được qua LobbyInvite hoặc ShareCode; không hiển thị trong search.
    /// BR-07: Lobby.MaxMembers nằm trong [GameTemplate.MinPlayers, GameTemplate.MaxPlayers].
    /// BR-08: Auto-hủy nếu trước giờ hẹn X phút mà chưa đạt MinPlayers.
    /// BR-10: Filter theo Karma (không dùng Elo).
    /// </summary>
    public class LobbyService : ILobbyService
    {
        private readonly ILobbyRepository _lobbyRepository;
        private readonly IGameTemplateRepository _gameTemplateRepository;
        private readonly IUserManagementRepository _userManagementRepository;
        private readonly ILobbyInviteRepository _lobbyInviteRepository;
        private readonly ILobbyHubService _hubService;
        private readonly ILobbyMessageService _lobbyMessageService;
        private readonly ILobbyMessageRepository _lobbyMessageRepository;
        private readonly IFriendshipRepository _friendshipRepository;
        private readonly IReservationRepository _reservationRepository;
        private readonly EligibilityValidator _eligibilityValidator;

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
            EligibilityValidator eligibilityValidator)
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
            _eligibilityValidator = eligibilityValidator;
        }

        public async Task<LobbyResponseDto> CreateLobbyAsync(Guid hostUserId, CreateLobbyRequestDto request)
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

        public async Task<LobbyResponseDto> JoinLobbyAsync(Guid lobbyId, Guid userId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            if (lobby.Status != LobbyStatus.Open)
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

            if (lobby.Members.Any(m => m.UserId == userId && m.IsActive))
            {
                throw new ConflictException(ApiErrorMessages.Lobby.MemberAlreadyInLobby);
            }

            // BR-LOBBY-01: chặn join sau recruitmentDeadline.
            var now = DateTime.UtcNow;
            if (lobby.RecruitmentDeadline.HasValue && now > lobby.RecruitmentDeadline.Value)
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

            var filledToMax = lobby.Members.Count(m => m.IsActive) >= lobby.MaxMembers;
            if (filledToMax)
            {
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

            return MapLobbyDto(lobby, null);
        }

        /// <summary>
        /// BR-USER-LIMIT-* + BR-RISK-04 cho member join lobby:
        /// - BR-USER-LIMIT-05: User đang là host của lobby ACTIVE → không được join.
        /// - BR-USER-LIMIT-01: Member đã tham gia 1 lobby active → không join thêm.
        /// - BR-USER-LIMIT-02: Lịch của member trùng với lobby đang join (+30p buffer).
        /// - BR-RISK-04: Account bị suspended/banned → chặn.
        /// </summary>
        private async Task ValidateMemberEligibilityAsync(Guid userId, Lobby lobby, DateTime now)
        {
            // BR-USER-LIMIT-05: User đang host lobby ACTIVE → không được join lobby khác.
            var activeHostLobbies = await _lobbyRepository.GetActiveLobbiesByHostAsync(userId);
            if (activeHostLobbies.Count > 0)
            {
                throw new InvalidOperationException(ApiErrorMessages.Reservation.HostCannotJoinLobby);
            }

            // BR-USER-LIMIT-01 + BR-NEW-02: Member đã có 1 lobby member active → không join thêm.
            var activeMemberLobbies = await _lobbyRepository.GetActiveLobbiesByMemberAsync(userId);
            if (activeMemberLobbies.Count >= 1)
            {
                throw new InvalidOperationException(ApiErrorMessages.Reservation.ActiveLobbyMemberLimitReached);
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
                    lobby.TimeSlot!.Value,
                    lobby.RecruitmentDeadline.Value,
                    lobby.ScheduledStartTime.Value);

                if (overlapList.Any())
                {
                    var firstOverlap = overlapList.First();
                    throw new InvalidOperationException(ApiErrorMessages.Reservation.OverlappingLobbyExists(
                        firstOverlap.RecruitmentDeadline ?? now,
                        firstOverlap.ScheduledStartTime ?? now));
                }
            }
        }

        public async Task<LobbyResponseDto> LeaveLobbyAsync(Guid lobbyId, Guid userId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            // P1 Fix #1: Block leaving during terminal or in-progress states
            if (lobby.Status is LobbyStatus.InProgress or LobbyStatus.Closed or
                LobbyStatus.TimeoutFailed or LobbyStatus.HostCancelled)
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
                    var newHost = otherActiveMembers.First();
                    newHost.IsHost = true;
                    newHostUserId = newHost.UserId;
                    await _lobbyMessageService.AddSystemMessageAsync(
                        lobby.Id,
                        $"Host đã rời phòng. {newHost.User?.Username ?? "Thành viên"} trở thành Host mới.");

                    // Nếu lobby đang FULL nhưng không còn đủ MaxMembers → chuyển về OPEN
                    var activeAfter = lobby.Members.Count(m => m.IsActive) - 1; // trừ host hiện tại
                    if (lobby.Status == LobbyStatus.Full && activeAfter < lobby.MaxMembers)
                    {
                        lobby.Status = LobbyStatus.Open;
                    }
                }
            }

            member.IsActive = false;
            member.Status = LobbyMemberStatus.Left;
            member.LeftAt = DateTime.UtcNow;
            lobby.UpdatedAt = DateTime.UtcNow;

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

        public async Task<LobbyResponseDto> GetLobbyAsync(Guid lobbyId, Guid? requestingUserId = null)
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

        public async Task<IReadOnlyList<LobbyResponseDto>> SearchLobbiesAsync(SearchLobbiesRequestDto request, Guid? requestingUserId = null)
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
                    .Where(l => l.Members.All(m => (m.User.Profile?.KarmaPoints ?? 100) >= request.MinKarmaScore.Value))
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
            Guid? requestingUserId = null)
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
            if (requestingUserId.HasValue)
            {
                var lobbyIds = result.Select(r => r.Id).ToList();
                var filteredLobbies = await FilterOverlappingLobbiesAsync(
                    (await Task.WhenAll(lobbyIds.Select(id => _lobbyRepository.GetByIdAsync(id)))).Where(l => l != null).Cast<Lobby>().ToList(),
                    requestingUserId.Value);
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
            var userHostingLobbies = await _lobbyRepository.GetActiveLobbiesByHostAsync(userId);
            var userJoinedLobbies = await _lobbyRepository.GetJoinedLobbiesAsync(userId);

            var userLobbies = userHostingLobbies.Concat(userJoinedLobbies).DistinctBy(l => l.Id).ToList();

            if (userLobbies.Count == 0)
            {
                return lobbies; // Không có lobby nào → không cần filter
            }

            // Tính scheduledTime của các lobby user đang tham gia
            var userScheduledRanges = userLobbies
                .Where(l => l.PlayDate.HasValue && l.TimeSlot.HasValue)
                .Select(l => new
                {
                    l.PlayDate,
                    l.TimeSlot,
                    Start = GetScheduledTime(l.PlayDate!.Value, l.TimeSlot!.Value),
                    End = GetScheduledTime(l.PlayDate!.Value, l.TimeSlot!.Value).AddMinutes(30) // +30 phút buffer
                }).ToList();

            // Loại bỏ lobby trùng lịch
            return lobbies.Where(lobby =>
            {
                if (!lobby.PlayDate.HasValue || !lobby.TimeSlot.HasValue)
                {
                    return true; // Không có thông tin schedule → không filter
                }

                var lobbyStart = GetScheduledTime(lobby.PlayDate.Value, lobby.TimeSlot.Value);
                var lobbyEnd = lobbyStart.AddMinutes(30);

                // Kiểm tra overlap
                return !userScheduledRanges.Any(userRange =>
                    userRange.PlayDate == lobby.PlayDate &&
                    userRange.TimeSlot == lobby.TimeSlot &&
                    userRange.Start < lobbyEnd &&
                    lobbyStart < userRange.End);
            }).ToList();
        }

        /// <summary>
        /// Tính scheduledTime từ PlayDate + TimeSlot (giống Lobby.ScheduledTime).
        /// </summary>
        private static DateTime GetScheduledTime(DateOnly playDate, TimeSlot timeSlot)
        {
            var timeOnly = timeSlot switch
            {
                TimeSlot.Morning => new TimeOnly(9, 0),
                TimeSlot.Afternoon => new TimeOnly(13, 0),
                TimeSlot.Evening => new TimeOnly(18, 0),
                TimeSlot.Night => new TimeOnly(19, 0),
                _ => new TimeOnly(9, 0)
            };
            return playDate.ToDateTime(timeOnly);
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

            await _lobbyMessageService.AddSystemMessageAsync(lobby.Id, $"Phòng chờ đã đóng: {lobby.ClosedReason}");

            return MapLobbyDto(lobby, null);
        }

        /// <summary>
        /// Host giải tán lobby — hard delete toàn bộ records (Lobby + Members + Messages + Invites + Reports).
        /// Chỉ áp dụng khi lobby chưa check-in tại quán.
        /// Giải phóng reservation → Holding để host có thể tạo lobby mới cùng slot.
        /// </summary>
        public async Task<DissolveLobbyResponseDto> DissolveLobbyAsync(Guid lobbyId, Guid hostUserId, string? reason = null)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.OnlyHostCanDissolve);
            }

            // Không cho phép dissolve khi đã check-in / đang chơi / đã đóng / đang rating
            if (lobby.Status == LobbyStatus.InProgress
                || lobby.Status == LobbyStatus.Closed
                || lobby.Status == LobbyStatus.RatingOpen
                || lobby.Status == LobbyStatus.HostCancelled
                || lobby.Status == LobbyStatus.TimeoutFailed
                || lobby.Status == LobbyStatus.RejectedByCafe
                || lobby.Status == LobbyStatus.ExpiredByCafe)
            {
                throw new ConflictException(
                    ApiErrorMessages.Lobby.DissolveInvalidState(lobby.Status));
            }

            var reservationId = lobby.ReservationId;
            var dissolvedAt = DateTime.UtcNow;

            // 1. Cancel pending invites
            await _lobbyInviteRepository.CancelAllPendingForLobbyAsync(lobbyId);

            // 2. Hard-delete messages (dùng repo trực tiếp)
            await _lobbyMessageRepository.RemoveByLobbyAsync(lobbyId);

            // 3. Hard-delete lobby + members + invites + reports
            await _lobbyRepository.RemoveAsync(lobby);

            await _lobbyRepository.SaveChangesAsync();

            // 4. Giải phóng reservation về Holding nếu có
            if (reservationId.HasValue)
            {
                var reservation = await _reservationRepository.GetByIdAsync(reservationId.Value);
                if (reservation != null && reservation.Status == ReservationStatus.Confirmed)
                {
                    reservation.Status = ReservationStatus.Holding;
                    reservation.UpdatedAt = dissolvedAt;
                    await _reservationRepository.UpdateAsync(reservation);
                    await _reservationRepository.SaveChangesAsync();
                }
            }

            return new DissolveLobbyResponseDto
            {
                LobbyId = lobbyId,
                ReservationId = reservationId,
                Reason = reason ?? "Host đã giải tán phòng chờ.",
                DissolvedAt = dissolvedAt
            };
        }

        public async Task<LobbyResponseDto> LockLobbyAsync(Guid lobbyId, Guid hostUserId)
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
                    $"Phòng chờ cần ít nhất {lobby.MinPlayers} người để khóa (hiện có {activeCount}).");
            }

            lobby.Status = LobbyStatus.Full;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            await _hubService.NotifyLobbyFull(lobbyId);

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> OpenKarmaWindowAsync(Guid lobbyId, Guid hostUserId)
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

        public async Task<LobbyResponseDto> TransitionToInProgressAsync(Guid lobbyId, Guid? activeSessionId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            if (lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.OnlyFullLobbyCanInProgress);
            }

            lobby.Status = LobbyStatus.InProgress;
            lobby.ActiveSessionId = activeSessionId;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> JoinLobbyByShareCodeAsync(string shareCode, Guid userId)
        {
            if (string.IsNullOrWhiteSpace(shareCode))
            {
                throw new BadRequestException(ApiErrorMessages.LobbyInvite.ShareCodeInvalid);
            }

            var lobby = await _lobbyRepository.GetByShareCodeAsync(shareCode)
                ?? throw new NotFoundException(ApiErrorMessages.LobbyInvite.ShareCodeInvalid);

            // BR-LOBBY-PRIVACY-03: Private lobby — share code chỉ join được nếu user là bạn bè
            // (Friendship.Status = Accepted) của ít nhất 1 thành viên active.
            if (lobby.IsPrivate)
            {
                var memberIds = lobby.Members
                    .Where(m => m.IsActive)
                    .Select(m => m.UserId)
                    .ToList();

                var isFriendOfAnyMember = false;
                foreach (var memberId in memberIds)
                {
                    var pair = await _friendshipRepository.GetByPairAsync(userId, memberId);
                    if (pair != null && pair.Status == FriendshipStatus.Accepted)
                    {
                        isFriendOfAnyMember = true;
                        break;
                    }
                }

                if (!isFriendOfAnyMember)
                {
                    throw new ForbiddenException(ApiErrorMessages.LobbyInvite.PrivateLobbyShareCodeRequiresFriendship);
                }
            }

            return await JoinLobbyAsync(lobby.Id, userId);
        }

        public async Task<LobbyResponseDto> TransitionToClosedAsync(Guid lobbyId)
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

            return MapLobbyDto(lobby, null);
        }

        // ============================ P1 Features ============================

        public async Task<LobbyResponseDto> TransferHostAsync(Guid lobbyId, Guid currentHostUserId, Guid newHostUserId)
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

        public async Task<LobbyResponseDto> KickMemberAsync(Guid lobbyId, Guid hostUserId, Guid targetUserId, string? reason)
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

        public async Task<LobbyResponseDto> UpdateLobbyAsync(Guid lobbyId, Guid hostUserId, UpdateLobbyRequestDto request)
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

        public async Task<LobbyResponseDto> SetMemberReadyAsync(Guid lobbyId, Guid userId, bool isReady)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            if (lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException(ApiErrorMessages.Lobby.OnlyFullLobbyCanReady);
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

            lobby.UpdatedAt = DateTime.UtcNow;
            await _lobbyRepository.SaveChangesAsync();

            await _hubService.NotifyMemberReady(lobbyId, userId, isReady);

            // Check: nếu tất cả members đều Ready → tự động chuyển sang InProgress
            var allReady = lobby.Members
                .Where(m => m.IsActive)
                .All(m => m.Status == LobbyMemberStatus.Ready);

            if (allReady && lobby.Members.Count(m => m.IsActive) >= lobby.MinPlayers)
            {
                lobby.Status = LobbyStatus.InProgress;
                lobby.UpdatedAt = DateTime.UtcNow;
                await _lobbyRepository.SaveChangesAsync();
                await _hubService.NotifyLobbyInProgress(lobbyId);
            }

            return MapLobbyDto(lobby, null);
        }

        public async Task<IReadOnlyList<LobbyResponseDto>> GetLobbiesByHostAsync(Guid hostUserId)
        {
            var lobbies = await _lobbyRepository.GetLobbiesByHostAsync(hostUserId);
            return lobbies.Select(l => MapLobbyDto(l, null)).ToList();
        }

        public async Task<IReadOnlyList<LobbyResponseDto>> GetJoinedLobbiesAsync(Guid userId)
        {
            var lobbies = await _lobbyRepository.GetJoinedLobbiesAsync(userId);
            return lobbies.Select(l => MapLobbyDto(l, null)).ToList();
        }

        public async Task<LobbyResponseDto> ReportLobbyAsync(Guid lobbyId, Guid reporterId, CreateLobbyReportDto request)
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

        // ============================ Helpers ============================

        private static readonly Random _secureRng = new();

        private async Task<string> GenerateUniqueShareCodeAsync()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

            // P1 Fix #6: Use static Random (not instantiated per-call) for better randomness
            // Also use GUID fallback to avoid predictability
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var code = new string(
                    Enumerable.Range(0, 6)
                        .Select(_ => chars[_secureRng.Next(chars.Length)])
                        .ToArray());

                var existing = await _lobbyRepository.GetByShareCodeAsync(code);
                if (existing == null)
                {
                    return code;
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