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

        public LobbyService(
            ILobbyRepository lobbyRepository,
            IGameTemplateRepository gameTemplateRepository,
            IUserManagementRepository userManagementRepository,
            ILobbyInviteRepository lobbyInviteRepository,
            ILobbyHubService hubService,
            ILobbyMessageService lobbyMessageService)
        {
            _lobbyRepository = lobbyRepository;
            _gameTemplateRepository = gameTemplateRepository;
            _userManagementRepository = userManagementRepository;
            _lobbyInviteRepository = lobbyInviteRepository;
            _hubService = hubService;
            _lobbyMessageService = lobbyMessageService;
        }

        public async Task<LobbyResponseDto> CreateLobbyAsync(Guid hostUserId, CreateLobbyRequestDto request)
        {
            if (request.ScheduledStartTime < DateTime.UtcNow.AddMinutes(5))
            {
                throw new BadRequestException("Thời gian bắt đầu dự kiến phải ít nhất 5 phút từ hiện tại.");
            }

            var game = await _gameTemplateRepository.GetByIdWithComponentsAsync(request.GameTemplateId)
                ?? throw new NotFoundException(ApiErrorMessages.BoardGame.MasterNotFound(request.GameTemplateId));

            // BR-07: MaxMembers phải nằm trong [MinPlayers, MaxPlayers] của GameTemplate
            if (request.MaxMembers < game.MinPlayers || request.MaxMembers > game.MaxPlayers)
            {
                throw new BadRequestException(
                    $"Số người tối đa ({request.MaxMembers}) phải nằm trong khoảng [{game.MinPlayers}, {game.MaxPlayers}] của game '{game.Name}'.");
            }

            // MinPlayers default = 2, validate > 0 và <= MaxMembers
            var minPlayers = request.MinPlayers ?? 2;
            if (minPlayers < 2 || minPlayers > request.MaxMembers)
            {
                throw new BadRequestException("Số người tối thiểu phải từ 2 đến MaxMembers.");
            }

            // Nếu SeatCount được set → validate với MaxMembers (BR-07)
            if (request.SeatCount.HasValue && (request.SeatCount.Value < request.MaxMembers || request.SeatCount.Value > game.MaxPlayers * 2))
            {
                throw new BadRequestException("SeatCount không hợp lệ so với MaxMembers.");
            }

            // Nếu có CafeId thì validate cafe có chứa GameTemplate này
            if (request.CafeId.HasValue)
            {
                var hasGame = await _gameTemplateRepository.CafeHasGameAsync(request.CafeId.Value, request.GameTemplateId);
                if (!hasGame)
                {
                    throw new BadRequestException("Quán đã chọn không có sẵn game này trong kho.");
                }
            }

            // Nếu có BookingId thì validate booking đã CONFIRMED
            if (request.BookingId.HasValue)
            {
                var booking = await _lobbyRepository.GetBookingByIdAsync(request.BookingId.Value)
                    ?? throw new NotFoundException("Không tìm thấy đơn đặt chỗ.");
                if (booking.UserId != hostUserId)
                {
                    throw new ForbiddenException("Bạn không phải chủ sở hữu đơn đặt chỗ này.");
                }
                if (booking.Status != BookingDepositStatus.Paid)
                {
                    throw new ConflictException("Đơn đặt chỗ chưa được xác nhận thanh toán.");
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
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.Status != LobbyStatus.Open)
            {
                throw new ConflictException("Phòng chờ này không còn mở.");
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
                throw new ConflictException("Bạn đã là thành viên của phòng này.");
            }

            if (lobby.Members.Count(m => m.IsActive) >= lobby.MaxMembers)
            {
                throw new ConflictException("Phòng chờ đã đủ số người tối đa.");
            }

            // BR-07
            if (lobby.SeatCount.HasValue && lobby.Members.Count(m => m.IsActive) >= lobby.SeatCount.Value)
            {
                throw new ConflictException("Số thành viên đã vượt quá số ghế cho phép.");
            }

            var now = DateTime.UtcNow;
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

        public async Task<LobbyResponseDto> LeaveLobbyAsync(Guid lobbyId, Guid userId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            var member = lobby.Members.FirstOrDefault(m => m.UserId == userId && m.IsActive);
            if (member == null)
            {
                throw new NotFoundException("Bạn không phải là thành viên của phòng này.");
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
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            // LOBBY-P0-FIX-8: Private lobby → enforce access control
            if (lobby.IsPrivate)
            {
                if (requestingUserId == null)
                {
                    throw new ForbiddenException("Phòng chờ riêng tư. Cần đăng nhập để xem.");
                }
                var isMember = lobby.Members.Any(m => m.UserId == requestingUserId.Value && m.IsActive);
                var hasInvite = await _lobbyInviteRepository.GetAcceptedInviteAsync(lobbyId, requestingUserId.Value);
                if (!isMember && hasInvite == null && lobby.HostUserId != requestingUserId.Value)
                {
                    throw new ForbiddenException("Bạn không có quyền xem phòng chờ riêng tư này.");
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

        public async Task<IReadOnlyList<LobbyResponseDto>> SearchLobbiesAsync(SearchLobbiesRequestDto request)
        {
            // BR-10: Filter by game, geo proximity, and karma (NOT Elo)
            // Private lobby bị loại khỏi kết quả search
            if (request.Latitude.HasValue && request.Longitude.HasValue && request.RadiusKm.HasValue)
            {
                var lobbies = await _lobbyRepository.SearchLobbiesNearbyAsync(
                    request.GameTemplateId,
                    request.Latitude.Value,
                    request.Longitude.Value,
                    request.RadiusKm.Value,
                    request.MinKarmaScore);

                var filtered = lobbies.Where(l => !l.IsPrivate).ToList();

                // LOBBY-P0-FIX-10: Trả DistanceKm cho mỗi lobby
                var result = new List<LobbyResponseDto>();
                foreach (var l in filtered)
                {
                    double? dist = null;
                    if (l.Latitude.HasValue && l.Longitude.HasValue)
                    {
                        dist = GeoHelper.HaversineKm(
                            request.Latitude.Value, request.Longitude.Value,
                            l.Latitude.Value, l.Longitude.Value);
                    }
                    result.Add(MapLobbyDto(l, dist));
                }
                return result;
            }

            var allLobbies = await _lobbyRepository.GetActiveLobbiesForGameAsync(request.GameTemplateId, null);
            allLobbies = allLobbies.Where(l => !l.IsPrivate).ToList();

            if (request.MinKarmaScore.HasValue)
            {
                allLobbies = allLobbies
                    .Where(l => l.Members.All(m => (m.User.Profile?.KarmaPoints ?? 100) >= request.MinKarmaScore.Value))
                    .ToList();
            }

            return allLobbies.Select(l => MapLobbyDto(l, null)).ToList();
        }

        public async Task<LobbyResponseDto> CloseLobbyAsync(Guid lobbyId, Guid hostUserId, string? reason)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException("Chỉ Host mới có thể đóng phòng chờ.");
            }

            if (lobby.Status == LobbyStatus.Closed || lobby.Status == LobbyStatus.HostCancelled || lobby.Status == LobbyStatus.TimeoutFailed)
            {
                throw new ConflictException("Phòng chờ đã đóng.");
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

        public async Task<LobbyResponseDto> LockLobbyAsync(Guid lobbyId, Guid hostUserId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException("Chỉ Host mới có thể khóa phòng chờ.");
            }

            if (lobby.Status != LobbyStatus.Open)
            {
                throw new ConflictException("Phòng chờ không ở trạng thái mở.");
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
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.HostUserId != hostUserId)
            {
                throw new ForbiddenException("Chỉ Host mới có thể mở cửa sổ đánh giá.");
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
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException("Chỉ phòng ở trạng thái FULL mới chuyển sang IN_PROGRESS được.");
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

            return await JoinLobbyAsync(lobby.Id, userId);
        }

        public async Task<LobbyResponseDto> TransitionToClosedAsync(Guid lobbyId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.Status != LobbyStatus.InProgress && lobby.Status != LobbyStatus.RatingOpen)
            {
                throw new ConflictException("Chỉ phòng đang chơi hoặc đang đánh giá mới đóng được.");
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
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException("Chỉ chuyển host được khi phòng đang mở hoặc đầy.");
            }

            var currentHost = lobby.Members.FirstOrDefault(m => m.UserId == currentHostUserId && m.IsHost && m.IsActive);
            if (currentHost == null)
            {
                throw new ForbiddenException("Bạn không phải Host hiện tại của phòng này.");
            }

            if (currentHostUserId == newHostUserId)
            {
                throw new BadRequestException("Bạn đã là Host rồi.");
            }

            var newHost = lobby.Members.FirstOrDefault(m => m.UserId == newHostUserId && m.IsActive);
            if (newHost == null)
            {
                throw new NotFoundException("Thành viên được chọn không còn trong phòng.");
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
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException("Không thể kick thành viên khi phòng đã đóng.");
            }

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException("Chỉ Host mới có thể kick thành viên.");
            }

            if (hostUserId == targetUserId)
            {
                throw new BadRequestException("Host không thể tự kick mình. Hãy dùng Leave thay thế.");
            }

            var target = lobby.Members.FirstOrDefault(m => m.UserId == targetUserId && m.IsActive);
            if (target == null)
            {
                throw new NotFoundException("Thành viên không còn trong phòng.");
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
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException("Chỉ Host mới có thể cập nhật phòng chờ.");
            }

            if (lobby.Status == LobbyStatus.InProgress ||
                lobby.Status == LobbyStatus.Closed ||
                lobby.Status == LobbyStatus.HostCancelled ||
                lobby.Status == LobbyStatus.TimeoutFailed)
            {
                throw new ConflictException("Không thể cập nhật phòng chờ đã đóng hoặc đang chơi.");
            }

            if (request.MaxMembers.HasValue)
            {
                if (lobby.Status == LobbyStatus.Full)
                {
                    throw new ConflictException("Không thể giảm MaxMembers khi phòng đã đầy.");
                }

                var game = await _gameTemplateRepository.GetByIdAsync(lobby.GameTemplateId);
                if (game == null)
                {
                    throw new NotFoundException("Không tìm thấy thông tin game.");
                }

                if (request.MaxMembers.Value < game.MinPlayers || request.MaxMembers.Value > game.MaxPlayers)
                {
                    throw new BadRequestException(
                        $"Số người tối đa phải nằm trong [{game.MinPlayers}, {game.MaxPlayers}].");
                }

                if (lobby.Members.Count(m => m.IsActive) > request.MaxMembers.Value)
                {
                    throw new ConflictException("Không thể giảm MaxMembers xuống dưới số thành viên hiện tại.");
                }

                lobby.MaxMembers = request.MaxMembers.Value;
            }

            if (request.MinPlayers.HasValue)
            {
                if (request.MinPlayers.Value < 2 || request.MinPlayers.Value > lobby.MaxMembers)
                {
                    throw new BadRequestException("MinPlayers phải từ 2 đến MaxMembers.");
                }
                lobby.MinPlayers = request.MinPlayers.Value;
            }

            if (request.ScheduledStartTime.HasValue)
            {
                if (request.ScheduledStartTime.Value < DateTime.UtcNow.AddMinutes(5))
                {
                    throw new BadRequestException("Thời gian bắt đầu phải cách hiện tại ít nhất 5 phút.");
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
                    throw new BadRequestException("CancellationLeadTimeMinutes phải từ 5 đến 1440.");
                }
                lobby.CancellationLeadTimeMinutes = request.CancellationLeadTimeMinutes.Value;
            }

            lobby.UpdatedAt = DateTime.UtcNow;
            await _lobbyRepository.SaveChangesAsync();

            await _hubService.NotifyLobbyUpdated(lobbyId);

            return MapLobbyDto(lobby, null);
        }

        public async Task<LobbyResponseDto> SetMemberReadyAsync(Guid lobbyId, Guid userId, bool isReady)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException("Chỉ có thể bấm Ready khi phòng đã đầy.");
            }

            var member = lobby.Members.FirstOrDefault(m => m.UserId == userId && m.IsActive);
            if (member == null)
            {
                throw new ForbiddenException("Bạn không phải thành viên của phòng này.");
            }

            if (isReady)
            {
                if (member.Status == LobbyMemberStatus.Kicked || member.Status == LobbyMemberStatus.Left)
                {
                    throw new ConflictException("Không thể Ready khi đã rời/bị kick.");
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
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.HostUserId == reporterId)
            {
                throw new BadRequestException("Bạn không thể báo cáo phòng chờ mà bạn là Host.");
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

        private async Task<string> GenerateUniqueShareCodeAsync()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var rng = new Random();

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var code = new string(
                    Enumerable.Repeat(chars, 8)
                        .Select(s => s[rng.Next(s.Length)])
                        .ToArray());

                var existing = await _lobbyRepository.GetByShareCodeAsync(code);
                if (existing == null)
                {
                    return code;
                }
            }

            return Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
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