using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class LobbyService : ILobbyService
    {
        private readonly ILobbyRepository _lobbyRepository;
        private readonly IGameTemplateRepository _gameTemplateRepository;
        private readonly ILobbyHubService _hubService;

        public LobbyService(
            ILobbyRepository lobbyRepository,
            IGameTemplateRepository gameTemplateRepository,
            ILobbyHubService hubService)
        {
            _lobbyRepository = lobbyRepository;
            _gameTemplateRepository = gameTemplateRepository;
            _hubService = hubService;
        }

        public async Task<LobbyResponseDto> CreateLobbyAsync(Guid hostUserId, CreateLobbyRequestDto request)
        {
            if (request.ScheduledStartTime < DateTime.UtcNow.AddMinutes(5))
            {
                throw new BadRequestException("Thời gian bắt đầu dự kiến phải ít nhất 5 phút từ hiện tại.");
            }

            if (request.MaxMembers < 2)
            {
                throw new BadRequestException("Phòng chờ cần ít nhất 2 người.");
            }

            var game = await _gameTemplateRepository.GetByIdWithComponentsAsync(request.GameTemplateId)
                ?? throw new NotFoundException(ApiErrorMessages.BoardGame.MasterNotFound(request.GameTemplateId));

            var now = DateTime.UtcNow;
            var lobby = new Lobby
            {
                Id = Guid.NewGuid(),
                HostUserId = hostUserId,
                GameTemplateId = request.GameTemplateId,
                ScheduledStartTime = request.ScheduledStartTime,
                CancellationLeadTimeMinutes = request.CancellationLeadTimeMinutes,
                MaxMembers = request.MaxMembers,
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
                JoinedAt = now
            });

            await _lobbyRepository.AddAsync(lobby);
            await _lobbyRepository.SaveChangesAsync();

            // Realtime: notify host's own lobby that it was created (helps mobile UI refresh).
            await _hubService.NotifyMemberJoined(lobby.Id, new LobbyMemberDto
            {
                Id = lobby.Members.First().Id,
                UserId = hostUserId,
                JoinedAt = now,
                IsActive = true,
                IsHost = true
            });

            return MapLobbyDto(lobby);
        }

        public async Task<LobbyResponseDto> JoinLobbyAsync(Guid lobbyId, Guid userId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.Status != LobbyStatus.Open)
            {
                throw new ConflictException("Phòng chờ này không còn mở.");
            }

            if (lobby.Members.Any(m => m.UserId == userId && m.IsActive))
            {
                throw new ConflictException("Bạn đã là thành viên của phòng này.");
            }

            if (lobby.Members.Count(m => m.IsActive) >= lobby.MaxMembers)
            {
                throw new ConflictException("Phòng chờ đã đủ số người tối đa.");
            }

            // BR-07: Nếu có SeatCount được set, kiểm tra không vượt quá
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

            // Realtime: broadcast MemberJoined + LobbyFull nếu vừa đủ.
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

            return MapLobbyDto(lobby);
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
            string? cancelReason = null;

            if (wasHost)
            {
                lobby.Status = LobbyStatus.HostCancelled;
                cancelReason = "Host đã rời phòng chờ.";
            }

            member.IsActive = false;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            // Realtime: notify group.
            if (wasHost)
            {
                await _hubService.NotifyLobbyCancelled(lobbyId, cancelReason!);
            }
            else
            {
                await _hubService.NotifyMemberLeft(lobbyId, memberId);
            }

            return MapLobbyDto(lobby);
        }

        public async Task<LobbyResponseDto> GetLobbyAsync(Guid lobbyId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            return MapLobbyDto(lobby);
        }

        public async Task<IReadOnlyList<LobbyResponseDto>> SearchLobbiesAsync(SearchLobbiesRequestDto request)
        {
            // BR-10: Filter by game, geo proximity, and karma (NOT Elo)
            if (request.Latitude.HasValue && request.Longitude.HasValue && request.RadiusKm.HasValue)
            {
                var lobbies = await _lobbyRepository.SearchLobbiesNearbyAsync(
                    request.GameTemplateId,
                    request.Latitude.Value,
                    request.Longitude.Value,
                    request.RadiusKm.Value,
                    request.MinKarmaScore);
                return lobbies.Select(MapLobbyDto).ToList();
            }

            // Fallback: search by game + karma only
            var allLobbies = await _lobbyRepository.GetActiveLobbiesForGameAsync(request.GameTemplateId, null);

            if (request.MinKarmaScore.HasValue)
            {
                allLobbies = allLobbies
                    .Where(l => l.Members.All(m => (m.User.Profile?.KarmaPoints ?? 100) >= request.MinKarmaScore.Value))
                    .ToList();
            }

            return allLobbies.Select(MapLobbyDto).ToList();
        }

        public async Task<LobbyResponseDto> CloseLobbyAsync(Guid lobbyId, Guid hostUserId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException("Chỉ Host mới có thể đóng phòng chờ.");
            }

            lobby.Status = LobbyStatus.Closed;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            return MapLobbyDto(lobby);
        }

        /// <summary>
        /// Host khóa phòng chờ để bắt đầu ghép đội.
        /// Chuyển trạng thái OPEN → FULL.
        /// </summary>
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

            lobby.Status = LobbyStatus.Full;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            // Realtime: Host vừa khóa phòng → báo cho cả nhóm biết đã đủ điều kiện đặt chỗ.
            await _hubService.NotifyLobbyFull(lobbyId);

            return MapLobbyDto(lobby);
        }

        /// <summary>
        /// Mở cửa sổ đánh giá Karma sau khi phiên chơi kết thúc.
        /// </summary>
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

            return MapLobbyDto(lobby);
        }

        /// <summary>
        /// Chuyển phòng sang trạng thái InProgress khi check-in tại quán.
        /// FULL → IN_PROGRESS
        /// </summary>
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

            return MapLobbyDto(lobby);
        }

        /// <summary>
        /// Chuyển phòng sang trạng thái Closed khi phiên chơi thanh toán xong.
        /// IN_PROGRESS/RATING_OPEN → CLOSED
        /// </summary>
        public async Task<LobbyResponseDto> TransitionToClosedAsync(Guid lobbyId)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

            if (lobby.Status != LobbyStatus.InProgress && lobby.Status != LobbyStatus.RatingOpen)
            {
                throw new ConflictException("Chỉ phòng đang chơi hoặc đang đánh giá mới đóng được.");
            }

            lobby.Status = LobbyStatus.Closed;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _lobbyRepository.SaveChangesAsync();

            return MapLobbyDto(lobby);
        }

        private static LobbyResponseDto MapLobbyDto(Lobby lobby)
        {
            return new LobbyResponseDto
            {
                Id = lobby.Id,
                HostUserId = lobby.HostUserId,
                GameTemplateId = lobby.GameTemplateId,
                ScheduledStartTime = lobby.ScheduledStartTime,
                MaxMembers = lobby.MaxMembers,
                SeatCount = lobby.SeatCount,
                ActiveSessionId = lobby.ActiveSessionId,
                Status = lobby.Status,
                Latitude = lobby.Latitude,
                Longitude = lobby.Longitude,
                CreatedAt = lobby.CreatedAt,
                UpdatedAt = lobby.UpdatedAt,
                Members = lobby.Members
                    .Where(m => m.IsActive)
                    .Select(m => new LobbyMemberDto
                    {
                        Id = m.Id,
                        UserId = m.UserId,
                        UserName = m.User?.Username ?? string.Empty,
                        JoinedAt = m.JoinedAt,
                        IsActive = m.IsActive,
                        IsHost = m.IsHost
                    })
                    .ToList()
            };
        }
    }
}