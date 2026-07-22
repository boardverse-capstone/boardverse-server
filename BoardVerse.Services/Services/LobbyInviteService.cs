using BoardVerse.Core.DTOs.LobbyInvite;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services;

public class LobbyInviteService : ILobbyInviteService
{
    private readonly ILobbyInviteRepository _inviteRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly ILobbyService _lobbyService;
    private readonly IFriendshipRepository _friendshipRepository;

    public LobbyInviteService(
        ILobbyInviteRepository inviteRepository,
        ILobbyRepository lobbyRepository,
        ILobbyService lobbyService,
        IFriendshipRepository friendshipRepository)
    {
        _inviteRepository = inviteRepository;
        _lobbyRepository = lobbyRepository;
        _lobbyService = lobbyService;
        _friendshipRepository = friendshipRepository;
    }

    public async Task<LobbyInviteResponseDto> SendInviteAsync(Guid lobbyId, Guid inviterId, SendLobbyInviteRequestDto request)
    {
        if (request.InviteeId == inviterId)
        {
            throw new BadRequestException(ApiErrorMessages.LobbyInvite.CannotInviteSelf);
        }

        var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
            ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

        var inviterMembership = lobby.Members.FirstOrDefault(m => m.UserId == inviterId && m.IsActive);
        if (inviterMembership == null)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.InviterNotMember);
        }

        if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full)
        {
            throw new ConflictException("Phòng chờ đã đóng/không khả dụng.");
        }

        if (lobby.Members.Any(m => m.UserId == request.InviteeId && m.IsActive))
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteeAlreadyMember);
        }

        // BR-FRIEND-02 / BR-LOBBY-INVITE-04: Kiểm tra inviter và invitee có phải bạn bè.
        // Nếu không phải bạn bè → không cho gửi invite trừ khi lobby public (chỉ cần block check).
        var pair = await _friendshipRepository.GetByPairAsync(inviterId, request.InviteeId);
        if (pair?.Status == FriendshipStatus.Blocked)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.BlockedByOtherParty);
        }

        // BR-LOBBY-INVITE-NEW-01: Với private lobby, inviter PHẢI là bạn bè của invitee.
        if (lobby.IsPrivate && (pair == null || pair.Status != FriendshipStatus.Accepted))
        {
            throw new ForbiddenException("Phòng chờ riêng tư chỉ cho phép mời bạn bè.");
        }

        // Check pending invite đã tồn tại
        var existing = await _inviteRepository.GetPendingInviteAsync(lobbyId, request.InviteeId);
        if (existing != null)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.PendingInviteAlreadyExists);
        }

        var invite = new LobbyInvite
        {
            Id = Guid.NewGuid(),
            LobbyId = lobbyId,
            InviterId = inviterId,
            InviteeId = request.InviteeId,
            Status = LobbyInviteStatus.Pending,
            Message = request.Message,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        await _inviteRepository.AddAsync(invite);
        await _inviteRepository.SaveChangesAsync();

        return MapToDto(invite, lobby);
    }

    public async Task<LobbyInviteResponseDto> AcceptInviteAsync(Guid inviteId, Guid currentUserId)
    {
        var invite = await _inviteRepository.GetByIdAsync(inviteId)
            ?? throw new NotFoundException(ApiErrorMessages.LobbyInvite.InviteNotFound(inviteId));

        if (invite.InviteeId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.NotInviteRecipient);
        }

        if (invite.Status != LobbyInviteStatus.Pending || invite.ExpiresAt <= DateTime.UtcNow)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteExpired);
        }

        var lobby = await _lobbyRepository.GetByIdAsync(invite.LobbyId)
            ?? throw new NotFoundException("Phòng chờ không còn tồn tại.");

        if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full)
        {
            // Tự động đánh dấu Expired nếu lobby đã đóng
            invite.Status = LobbyInviteStatus.Expired;
            invite.RespondedAt = DateTime.UtcNow;
            await _inviteRepository.SaveChangesAsync();
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteExpired);
        }

        // P1-FIX: Lobby đã đủ người → set invite Expired và báo lỗi
        if (lobby.Members.Count(m => m.IsActive) >= lobby.MaxMembers)
        {
            invite.Status = LobbyInviteStatus.Expired;
            invite.RespondedAt = DateTime.UtcNow;
            await _inviteRepository.SaveChangesAsync();
            throw new ConflictException("Phòng chờ đã đầy. Không thể accept lời mời này.");
        }

        // BR-LOBBY-INVITE-NEW-02: Nếu 2 bên đã unfriend trước khi accept → reject.
        if (lobby.IsPrivate)
        {
            var pair = await _friendshipRepository.GetByPairAsync(invite.InviterId, invite.InviteeId);
            if (pair == null || pair.Status != FriendshipStatus.Accepted)
            {
                invite.Status = LobbyInviteStatus.Cancelled;
                invite.RespondedAt = DateTime.UtcNow;
                await _inviteRepository.SaveChangesAsync();
                throw new ForbiddenException("Phòng chờ riêng tư yêu cầu quan hệ bạn bè đang hoạt động.");
            }
        }

        // Join lobby
        await _lobbyService.JoinLobbyAsync(invite.LobbyId, currentUserId);

        invite.Status = LobbyInviteStatus.Accepted;
        invite.RespondedAt = DateTime.UtcNow;

        await _inviteRepository.SaveChangesAsync();

        return MapToDto(invite, lobby);
    }

    public async Task<LobbyInviteResponseDto> DeclineInviteAsync(Guid inviteId, Guid currentUserId)
    {
        var invite = await _inviteRepository.GetByIdAsync(inviteId)
            ?? throw new NotFoundException(ApiErrorMessages.LobbyInvite.InviteNotFound(inviteId));

        if (invite.InviteeId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.NotInviteRecipient);
        }

        if (invite.Status != LobbyInviteStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteNotPending);
        }

        invite.Status = LobbyInviteStatus.Declined;
        invite.RespondedAt = DateTime.UtcNow;

        await _inviteRepository.SaveChangesAsync();

        return MapToDto(invite, null);
    }

    public async Task CancelInviteAsync(Guid inviteId, Guid currentUserId)
    {
        var invite = await _inviteRepository.GetByIdAsync(inviteId)
            ?? throw new NotFoundException(ApiErrorMessages.LobbyInvite.InviteNotFound(inviteId));

        if (invite.InviterId != currentUserId)
        {
            throw new ForbiddenException("Chỉ người gửi lời mời mới có thể hủy.");
        }

        if (invite.Status != LobbyInviteStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteNotPending);
        }

        invite.Status = LobbyInviteStatus.Cancelled;
        invite.RespondedAt = DateTime.UtcNow;

        await _inviteRepository.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<LobbyInviteResponseDto>> GetMyPendingInvitesAsync(Guid inviteeId)
    {
        var list = await _inviteRepository.GetPendingByInviteeAsync(inviteeId);
        return list.Select(i => MapToDto(i, i.Lobby)).ToList();
    }

    public async Task<IReadOnlyList<LobbyInviteResponseDto>> GetMyInvitesAsync(Guid inviteeId, string? status)
    {
        LobbyInviteStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<LobbyInviteStatus>(status, ignoreCase: true, out var s))
            {
                throw new BadRequestException($"Trạng thái lời mời không hợp lệ: '{status}'.");
            }
            parsed = s;
        }

        var list = await _inviteRepository.GetAllByInviteeAsync(inviteeId, parsed);
        return list.Select(i => MapToDto(i, i.Lobby)).ToList();
    }

    public async Task<LobbyShareInfoDto> GetShareInfoAsync(Guid lobbyId, Guid currentUserId)
    {
        var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
            ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{lobbyId}'.");

        // Chỉ thành viên mới xem được share code (kể cả khi lobby public) để tránh spam.
        var isMember = lobby.Members.Any(m => m.UserId == currentUserId && m.IsActive);
        if (!isMember)
        {
            throw new ForbiddenException("Chỉ thành viên của phòng chờ mới có thể lấy share code.");
        }

        return new LobbyShareInfoDto
        {
            LobbyId = lobby.Id,
            ShareCode = lobby.ShareCode,
            IsPrivate = lobby.IsPrivate,
            LobbyStatus = lobby.Status.ToString()
        };
    }

    private static LobbyInviteResponseDto MapToDto(LobbyInvite invite, Lobby? lobby)
    {
        return new LobbyInviteResponseDto
        {
            InviteId = invite.Id,
            LobbyId = invite.LobbyId,
            LobbyName = lobby?.Description,
            GameName = lobby?.GameTemplate?.Name,
            ScheduledStartTime = lobby?.ScheduledStartTime,
            InviterId = invite.InviterId,
            InviterUsername = invite.Inviter?.Username ?? string.Empty,
            InviteeId = invite.InviteeId,
            InviteeUsername = invite.Invitee?.Username ?? string.Empty,
            Status = invite.Status.ToString(),
            CreatedAt = invite.CreatedAt,
            ExpiresAt = invite.ExpiresAt,
            RespondedAt = invite.RespondedAt,
            Message = invite.Message
        };
    }
}
