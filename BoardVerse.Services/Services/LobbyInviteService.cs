using BoardVerse.Core.Constants;
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

    public async Task<LobbyInviteResponseDto> SendInviteAsync(Guid lobbyId, Guid inviterId, SendLobbyInviteRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.InviteeId == inviterId)
        {
            throw new BadRequestException(ApiErrorMessages.LobbyInvite.CannotInviteSelf);
        }

        // BR-LOBBY-INVITE-10: Rate limit gửi invite — 30 invite / user / ngày.
        var startOfDayUtc = DateTime.UtcNow.Date;
        var sentToday = await _inviteRepository.CountSentByInviterSinceAsync(inviterId, startOfDayUtc);
        if (sentToday >= LobbyInviteLimits.MaxSentPerUserPerDay)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteRateLimitExceeded);
        }

        // BR-LOBBY-INVITE-10: Rate limit nhận invite — 20 invite Pending / user / ngày.
        var receivedToday = await _inviteRepository.CountPendingByInviteeSinceAsync(request.InviteeId, startOfDayUtc);
        if (receivedToday >= LobbyInviteLimits.MaxReceivedPerUserPerDay)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteRateLimitExceeded);
        }

        var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
            ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

        var inviterMembership = lobby.Members.FirstOrDefault(m => m.UserId == inviterId && m.IsActive);
        if (inviterMembership == null)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.InviterNotMember);
        }

        if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.LobbyClosedOrUnavailable);
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
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.PrivateLobbyInviterMustBeFriend);
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
            ExpiresAt = DateTime.UtcNow.AddHours(LobbyInviteLimits.InviteExpiryHours),
            CreatedAt = DateTime.UtcNow
        };

        await _inviteRepository.AddAsync(invite);
        await _inviteRepository.SaveChangesAsync();

        return MapToDto(invite, lobby);
    }

    public async Task<LobbyInviteResponseDto> AcceptInviteAsync(Guid inviteId, Guid currentUserId, CancellationToken cancellationToken = default)
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
            ?? throw new NotFoundException(ApiErrorMessages.LobbyInvite.LobbyDisappeared);

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
            throw new ConflictException(ApiErrorMessages.LobbyInvite.LobbyFullCannotAcceptInvite);
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
                throw new ForbiddenException(ApiErrorMessages.LobbyInvite.PrivateLobbyRequiresActiveFriendship);
            }
        }

        // Join lobby
        await _lobbyService.JoinLobbyAsync(invite.LobbyId, currentUserId);

        invite.Status = LobbyInviteStatus.Accepted;
        invite.RespondedAt = DateTime.UtcNow;

        await _inviteRepository.SaveChangesAsync();

        return MapToDto(invite, lobby);
    }

    public async Task<LobbyInviteResponseDto> DeclineInviteAsync(Guid inviteId, Guid currentUserId, CancellationToken cancellationToken = default)
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

    public async Task CancelInviteAsync(Guid inviteId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var invite = await _inviteRepository.GetByIdAsync(inviteId)
            ?? throw new NotFoundException(ApiErrorMessages.LobbyInvite.InviteNotFound(inviteId));

        if (invite.InviterId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.OnlyInviterCanCancel);
        }

        if (invite.Status != LobbyInviteStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteNotPending);
        }

        invite.Status = LobbyInviteStatus.Cancelled;
        invite.RespondedAt = DateTime.UtcNow;

        await _inviteRepository.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<LobbyInviteResponseDto>> GetLobbyInvitesAsync(
        Guid lobbyId,
        Guid currentUserId,
        string? status = null,
        int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit < 1) limit = 100;
        if (limit > 200) limit = 200;

        var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
            ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

        // Chỉ thành viên active của lobby mới xem được lịch sử invite.
        var isMember = lobby.Members.Any(m => m.UserId == currentUserId && m.IsActive);
        if (!isMember)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.OnlyLobbyMemberCanViewShareCode);
        }

        LobbyInviteStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<LobbyInviteStatus>(status, ignoreCase: true, out var s))
            {
                throw new BadRequestException(ApiErrorMessages.LobbyInvite.InviteInvalidStatus(status));
            }
            parsed = s;
        }

        var list = await _inviteRepository.GetByLobbyAsync(lobbyId, parsed);
        return list
            .OrderByDescending(i => i.CreatedAt)
            .Take(limit)
            .Select(i => MapToDto(i, lobby))
            .ToList();
    }

    public async Task<LobbyInviteResponseDto> ResendInviteAsync(Guid inviteId, Guid currentUserId)
    {
        var oldInvite = await _inviteRepository.GetByIdAsync(inviteId)
            ?? throw new NotFoundException(ApiErrorMessages.LobbyInvite.InviteNotFound(inviteId));

        // BR-LOBBY-INVITE-NEW-03: Chỉ inviter cũ hoặc host lobby mới gửi lại được.
        var lobby = await _lobbyRepository.GetByIdAsync(oldInvite.LobbyId)
            ?? throw new NotFoundException(ApiErrorMessages.LobbyInvite.LobbyDisappeared);

        var isInviter = oldInvite.InviterId == currentUserId;
        var isHost = lobby.Members.Any(m => m.UserId == currentUserId && m.IsActive && m.IsHost);
        if (!isInviter && !isHost)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.OnlyInviterCanCancel);
        }

        // Chỉ resend được các invite đã ở trạng thái terminal (trừ Accepted).
        if (oldInvite.Status == LobbyInviteStatus.Accepted)
        {
            throw new ConflictException(
                ApiErrorMessages.LobbyInvite.AlreadyAccepted);
        }
        if (oldInvite.Status == LobbyInviteStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.PendingInviteAlreadyExists);
        }

        // BR-LOBBY-INVITE-01: Tạo record mới (giữ lịch sử) thay vì mutate row cũ.
        // Mỗi (LobbyId, InviteeId) chỉ có 1 Pending record tại 1 thời điểm.
        var existingPending = await _inviteRepository.GetPendingInviteAsync(oldInvite.LobbyId, oldInvite.InviteeId);
        if (existingPending != null)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.PendingInviteAlreadyExists);
        }

        // BR-LOBBY-INVITE-02: Inviter mới phải là thành viên active của lobby.
        var inviterMembership = lobby.Members.FirstOrDefault(m => m.UserId == currentUserId && m.IsActive);
        if (inviterMembership == null)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.InviterNotMember);
        }

        // Lobby phải còn mở.
        if (lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.LobbyClosedOrUnavailable);
        }

        // Invitee chưa là member.
        if (lobby.Members.Any(m => m.UserId == oldInvite.InviteeId && m.IsActive))
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteeAlreadyMember);
        }

        // BR-LOBBY-INVITE-NEW-01: Với private lobby, inviter phải là bạn bè của invitee.
        var pair = await _friendshipRepository.GetByPairAsync(currentUserId, oldInvite.InviteeId);
        if (pair?.Status == FriendshipStatus.Blocked)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.BlockedByOtherParty);
        }
        if (lobby.IsPrivate && (pair == null || pair.Status != FriendshipStatus.Accepted))
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.PrivateLobbyInviterMustBeFriend);
        }

        // Rate limit giống lúc gửi mới.
        var startOfDayUtc = DateTime.UtcNow.Date;
        var sentToday = await _inviteRepository.CountSentByInviterSinceAsync(currentUserId, startOfDayUtc);
        if (sentToday >= LobbyInviteLimits.MaxSentPerUserPerDay)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteRateLimitExceeded);
        }
        var receivedToday = await _inviteRepository.CountPendingByInviteeSinceAsync(oldInvite.InviteeId, startOfDayUtc);
        if (receivedToday >= LobbyInviteLimits.MaxReceivedPerUserPerDay)
        {
            throw new ConflictException(ApiErrorMessages.LobbyInvite.InviteRateLimitExceeded);
        }

        var newInvite = new LobbyInvite
        {
            Id = Guid.NewGuid(),
            LobbyId = oldInvite.LobbyId,
            InviterId = currentUserId,
            InviteeId = oldInvite.InviteeId,
            Status = LobbyInviteStatus.Pending,
            Message = oldInvite.Message,
            ExpiresAt = DateTime.UtcNow.AddHours(LobbyInviteLimits.InviteExpiryHours),
            CreatedAt = DateTime.UtcNow
        };

        await _inviteRepository.AddAsync(newInvite);
        await _inviteRepository.SaveChangesAsync();

        return MapToDto(newInvite, lobby);
    }

    public async Task<IReadOnlyList<LobbyInviteResponseDto>> GetMyPendingInvitesAsync(Guid inviteeId, CancellationToken cancellationToken = default)
    {
        var list = await _inviteRepository.GetPendingByInviteeAsync(inviteeId);
        return list.Select(i => MapToDto(i, i.Lobby)).ToList();
    }

    public async Task<IReadOnlyList<LobbyInviteResponseDto>> GetMyInvitesAsync(Guid inviteeId, string? status, CancellationToken cancellationToken = default)
    {
        LobbyInviteStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<LobbyInviteStatus>(status, ignoreCase: true, out var s))
            {
                throw new BadRequestException(ApiErrorMessages.LobbyInvite.InviteInvalidStatus(status.ToString()));
            }
            parsed = s;
        }

        var list = await _inviteRepository.GetAllByInviteeAsync(inviteeId, parsed);
        return list.Select(i => MapToDto(i, i.Lobby)).ToList();
    }

    public async Task<LobbyShareInfoDto> GetShareInfoAsync(Guid lobbyId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
            ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

        // Chỉ thành viên mới xem được share code (kể cả khi lobby public) để tránh spam.
        var isMember = lobby.Members.Any(m => m.UserId == currentUserId && m.IsActive);
        if (!isMember)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.OnlyLobbyMemberCanViewShareCode);
        }

        return new LobbyShareInfoDto
        {
            LobbyId = lobby.Id,
            ShareCode = lobby.ShareCode,
            IsPrivate = lobby.IsPrivate,
            LobbyStatus = lobby.Status.ToString()
        };
    }

    public async Task<IReadOnlyList<LobbyInvitableFriendDto>> GetInvitableFriendsForLobbyAsync(
        Guid lobbyId,
        Guid currentUserId,
        LobbyInvitableFriendsQuery query, CancellationToken cancellationToken = default)
    {
        var limit = query?.Limit ?? 100;
        if (limit < 1) limit = 100;
        if (limit > 200) limit = 200;

        // Parse filter status list (comma-separated).
        var allowedStatuses = ParseStatusFilter(query?.Status);

        var lobby = await _lobbyRepository.GetByIdAsync(lobbyId)
            ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(lobbyId));

        // Chỉ thành viên active của lobby mới xem được danh sách mời.
        var isMember = lobby.Members.Any(m => m.UserId == currentUserId && m.IsActive);
        if (!isMember)
        {
            throw new ForbiddenException(ApiErrorMessages.LobbyInvite.OnlyLobbyMemberCanViewShareCode);
        }

        var lobbyClosed =
            lobby.Status != LobbyStatus.Open && lobby.Status != LobbyStatus.Full;

        // 1) Lấy danh sách friendship Accepted của currentUser.
        var friends = await _friendshipRepository.GetFriendsAsync(currentUserId);
        if (friends.Count == 0)
        {
            return Array.Empty<LobbyInvitableFriendDto>();
        }

        // 2) Lấy map userId → friendship (để lấy FriendsSince).
        var friendUserIds = friends
            .Select(f => f.RequesterId == currentUserId ? f.AddresseeId : f.RequesterId)
            .ToList();

        // 3) Lấy tất cả invite của lobby này liên quan tới các friend trên
        //    để xác định LatestInviteId + LatestInviteStatus.
        //    Single query, lọc theo InviteeeId IN (friendUserIds).
        var lobbyInvites = await _inviteRepository.GetByLobbyAsync(lobbyId);
        var latestInviteByInvitee = lobbyInvites
            .Where(i => friendUserIds.Contains(i.InviteeId))
            .GroupBy(i => i.InviteeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(i => i.CreatedAt).First());

        // 4) Lấy active member UserIds của lobby (IsActive = true).
        var memberUserIds = lobby.Members
            .Where(m => m.IsActive)
            .Select(m => m.UserId)
            .ToHashSet();

        var searchKeyword = query?.Search?.Trim();
        var hasSearch = !string.IsNullOrWhiteSpace(searchKeyword);
        var minKarma = query?.MinKarma ?? 0;

        var result = new List<LobbyInvitableFriendDto>(friends.Count);
        foreach (var f in friends)
        {
            var otherUserId = f.RequesterId == currentUserId ? f.AddresseeId : f.RequesterId;
            var other = f.RequesterId == currentUserId ? f.Addressee : f.Requester;

            // Nếu friend đã là member active của lobby → AlreadyMember (ưu tiên cao nhất).
            if (memberUserIds.Contains(otherUserId))
            {
                result.Add(BuildDto(otherUserId, other, f, LobbyInviteFriendStatus.AlreadyMember, null, null));
                continue;
            }

            // Nếu lobby đã đóng → LobbyClosed (mọi friend còn lại đều không thể mời).
            if (lobbyClosed)
            {
                result.Add(BuildDto(otherUserId, other, f, LobbyInviteFriendStatus.LobbyClosed, null, null));
                continue;
            }

            // Kiểm tra block.
            var invite = latestInviteByInvitee.TryGetValue(otherUserId, out var inv) ? inv : null;
            if (f.Status == FriendshipStatus.Blocked)
            {
                var status = f.BlockerUserId == currentUserId
                    ? LobbyInviteFriendStatus.BlockedByMe
                    : LobbyInviteFriendStatus.BlockedByThem;
                result.Add(BuildDto(otherUserId, other, f, status,
                    invite?.Id, invite?.Status.ToString() ?? null));
                continue;
            }

            // Xác định trạng thái dựa trên invite gần nhất.
            if (invite == null)
            {
                // Chưa từng có invite nào cho friend này → Invitable.
                result.Add(BuildDto(otherUserId, other, f, LobbyInviteFriendStatus.Invitable, null, null));
            }
            else if (invite.Status == LobbyInviteStatus.Pending && invite.ExpiresAt > DateTime.UtcNow)
            {
                // Có invite Pending còn hạn → InvitePending.
                result.Add(BuildDto(otherUserId, other, f, LobbyInviteFriendStatus.InvitePending,
                    invite.Id, invite.Status.ToString()));
            }
            else if (invite.Status == LobbyInviteStatus.Accepted)
            {
                // Đã accept (nhưng không còn là member — ví dụ: đã rời lobby).
                // Vẫn trả InviteAccepted để UI biết đã từng mời + accept.
                result.Add(BuildDto(otherUserId, other, f, LobbyInviteFriendStatus.InviteAccepted,
                    invite.Id, invite.Status.ToString()));
            }
            else
            {
                // Declined / Expired / Cancelled → có thể gửi lại (Invitable)
                // nhưng trả về InviteNotPending + LatestInviteId để UI biết lịch sử.
                result.Add(BuildDto(otherUserId, other, f, LobbyInviteFriendStatus.Invitable,
                    invite.Id, invite.Status.ToString()));
            }
        }

        // ===== Filter (sau khi đã có status) =====
        IEnumerable<LobbyInvitableFriendDto> filtered = result;

        if (allowedStatuses is not null)
        {
            filtered = filtered.Where(r => allowedStatuses.Contains(r.InviteStatus));
        }

        if (hasSearch)
        {
            var kw = searchKeyword!.ToLowerInvariant();
            filtered = filtered.Where(r => r.Username.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        if (minKarma > 0)
        {
            filtered = filtered.Where(r => r.KarmaPoints >= minKarma);
        }

        if (query?.OnlineOnly == true)
        {
            filtered = filtered.Where(r =>
                r.ActivityStatus == "Online" || r.ActivityStatus == "RecentlyActive");
        }

        // Sắp xếp: Invitable trước, rồi InvitePending, rồi AlreadyMember, các status khác cuối.
        // Sau đó sort theo Karma giảm dần (gamer hoạt động mạnh lên đầu).
        return filtered
            .OrderBy(r => (int)r.InviteStatus)
            .ThenByDescending(r => r.KarmaPoints)
            .ThenBy(r => r.Username)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Parse comma-separated string các <see cref="LobbyInviteFriendStatus"/> hợp lệ.
    /// Trả về null nếu filter rỗng (= không filter, trả tất cả).
    /// Throw BadRequest nếu có status name không hợp lệ.
    /// </summary>
    private static HashSet<LobbyInviteFriendStatus>? ParseStatusFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var names = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = new HashSet<LobbyInviteFriendStatus>();
        foreach (var name in names)
        {
            if (!Enum.TryParse<LobbyInviteFriendStatus>(name, ignoreCase: true, out var parsed))
            {
                throw new BadRequestException(
                    ApiErrorMessages.System.LobbyInviteFriendStatusInvalid(
                        name, string.Join(", ", Enum.GetNames<LobbyInviteFriendStatus>())));
            }
            result.Add(parsed);
        }
        return result.Count == 0 ? null : result;
    }

    private static LobbyInvitableFriendDto BuildDto(
        Guid userId,
        User other,
        Friendship f,
        LobbyInviteFriendStatus status,
        Guid? latestInviteId,
        string? latestInviteStatus)
    {
        var lastActive = other.Profile?.LastActiveAt;
        return new LobbyInvitableFriendDto
        {
            UserId = userId,
            Username = other.Username,
            AvatarUrl = other.Profile?.AvatarUrl,
            KarmaPoints = other.Profile?.KarmaPoints ?? 100,
            GamerTier = other.Profile?.GamerTier.ToString() ?? null,
            LastActiveAt = lastActive,
            ActivityStatus = ComputeActivityStatus(lastActive),
            FriendsSince = f.AcceptedAt ?? f.UpdatedAt,
            InviteStatus = status,
            LatestInviteId = latestInviteId,
            LatestInviteStatus = latestInviteStatus
        };
    }

    private static string ComputeActivityStatus(DateTime? lastActiveAt)
    {
        if (lastActiveAt == null) return "Offline";
        var diff = DateTime.UtcNow - lastActiveAt.Value;
        if (diff.TotalMinutes <= 5) return "Online";
        if (diff.TotalHours <= 1) return "RecentlyActive";
        if (diff.TotalDays <= 7) return "Away";
        return "Offline";
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
