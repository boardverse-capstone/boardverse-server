using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class LobbyMessageService : ILobbyMessageService
    {
        private readonly ILobbyMessageRepository _lobbyMessageRepository;
        private readonly ILobbyRepository _lobbyRepository;
        private readonly ILobbyHubService _hubService;
        private readonly IUserManagementRepository _userManagementRepository;

        public LobbyMessageService(
            ILobbyMessageRepository lobbyMessageRepository,
            ILobbyRepository lobbyRepository,
            ILobbyHubService hubService,
            IUserManagementRepository userManagementRepository)
        {
            _lobbyMessageRepository = lobbyMessageRepository;
            _lobbyRepository = lobbyRepository;
            _hubService = hubService;
            _userManagementRepository = userManagementRepository;
        }

        public async Task<LobbyMessageDto> SendMessageAsync(Guid lobbyId, Guid senderId, string content)
        {
            if (string.IsNullOrWhiteSpace(content) || content.Length > 1000)
            {
                throw new BadRequestException("Tin nhắn phải có từ 1 đến 1000 ký tự.");
            }

            var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
                ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

            var member = lobby.Members.FirstOrDefault(m => m.UserId == senderId && m.IsActive);
            if (member == null)
            {
                throw new ForbiddenException("Bạn không phải thành viên của phòng chờ này.");
            }

            var sender = await _userManagementRepository.GetByIdAsync(senderId);

            var msg = new LobbyMessage
            {
                Id = Guid.NewGuid(),
                LobbyId = lobbyId,
                SenderId = senderId,
                Content = content.Trim(),
                IsSystem = false,
                CreatedAt = DateTime.UtcNow
            };
            await _lobbyMessageRepository.AddAsync(msg);
            await _lobbyMessageRepository.SaveChangesAsync();

            var dto = new LobbyMessageDto
            {
                Id = msg.Id,
                LobbyId = lobbyId,
                SenderId = senderId,
                SenderName = sender?.Username ?? string.Empty,
                SenderAvatarUrl = sender?.Profile?.AvatarUrl,
                Content = msg.Content,
                IsSystem = false,
                CreatedAt = msg.CreatedAt
            };

            await _hubService.NotifyMessagePosted(lobbyId, dto);

            return dto;
        }

        public async Task<IReadOnlyList<LobbyMessageDto>> GetMessagesAsync(Guid lobbyId, DateTime? beforeCursor, int limit = 50)
        {
            if (limit is < 1 or > 200) limit = 50;

            var msgs = await _lobbyMessageRepository.GetByLobbyAsync(lobbyId, beforeCursor, limit);

            return msgs.Select(m => new LobbyMessageDto
            {
                Id = m.Id,
                LobbyId = m.LobbyId,
                SenderId = m.SenderId,
                SenderName = m.IsSystem
                    ? "Hệ thống"
                    : (m.Sender?.Username ?? "Unknown"),
                SenderAvatarUrl = m.Sender?.Profile?.AvatarUrl,
                Content = m.Content,
                IsSystem = m.IsSystem,
                CreatedAt = m.CreatedAt
            }).ToList();
        }

        public async Task AddSystemMessageAsync(Guid lobbyId, string content)
        {
            var msg = new LobbyMessage
            {
                Id = Guid.NewGuid(),
                LobbyId = lobbyId,
                SenderId = null, // System message has no sender
                Content = content,
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            };
            await _lobbyMessageRepository.AddAsync(msg);
            await _lobbyMessageRepository.SaveChangesAsync();

            await _hubService.NotifyMessagePosted(lobbyId, new LobbyMessageDto
            {
                Id = msg.Id,
                LobbyId = lobbyId,
                SenderId = null,
                SenderName = "Hệ thống",
                Content = msg.Content,
                IsSystem = true,
                CreatedAt = msg.CreatedAt
            });
        }

        public async Task AddMemberJoinedMessageAsync(Guid lobbyId, Guid userId)
        {
            var user = await _userManagementRepository.GetByIdAsync(userId);
            var name = user?.Username ?? "Một người dùng";
            await AddSystemMessageAsync(lobbyId, $"{name} đã tham gia phòng chờ.");
        }
    }
}