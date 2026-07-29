using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class FriendService : IFriendService
{
    private readonly IFriendshipRepository _friendshipRepository;
    private readonly IUserManagementRepository _userRepository;
    private readonly ILobbyMemberRepository _lobbyMemberRepository;
    private readonly IFriendNoteService _friendNoteService;
    private readonly ILobbyInviteRepository _lobbyInviteRepository;
    private readonly ILogger<FriendService> _logger;

    private const int MaxFriendRequestsPerHour = 20;
    private const int MaxFriendCount = 5000;

    public FriendService(
        IFriendshipRepository friendshipRepository,
        IUserManagementRepository userRepository,
        ILobbyMemberRepository lobbyMemberRepository,
        IFriendNoteService friendNoteService,
        ILobbyInviteRepository lobbyInviteRepository,
        ILogger<FriendService> logger)
    {
        _friendshipRepository = friendshipRepository;
        _userRepository = userRepository;
        _lobbyMemberRepository = lobbyMemberRepository;
        _friendNoteService = friendNoteService;
        _lobbyInviteRepository = lobbyInviteRepository;
        _logger = logger;
    }

    public async Task<FriendshipResponseDto> SendFriendRequestAsync(Guid requesterId, SendFriendRequestDto request)
    {
        if (request.AddresseeId == requesterId)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.CannotSendToSelf);
        }

        var addressee = await _userRepository.GetByIdAsync(request.AddresseeId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(request.AddresseeId));

        if (!addressee.IsActive || addressee.AccountStatus != UserAccountStatus.Active)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.CannotSendRequestToInactive);
        }

        // BR-FRIEND-RATE-01: Chống spam gửi request quá nhiều.
        var hourlySent = await CountSentRequestsInWindowAsync(requesterId, TimeSpan.FromHours(1));
        if (hourlySent >= MaxFriendRequestsPerHour)
        {
            throw new TooManyRequestsException(ApiErrorMessages.Friend.RateLimitExceeded);
        }

        // Check privacy setting AcceptFriendRequestsFrom = "FriendsOfFriends"
        var addresseeProfile = await _userRepository.GetByIdWithProfileAsync(request.AddresseeId);
        var policy = addresseeProfile?.Profile?.AcceptFriendRequestsFrom ?? "Everyone";
        if (string.Equals(policy, "FriendsOfFriends", StringComparison.OrdinalIgnoreCase))
        {
            var currentFriends = (await _friendshipRepository.GetFriendUserIdsAsync(requesterId)).ToHashSet();
            var isFriendOfFriend = await HasAnyFriendOfFriendAsync(requesterId, request.AddresseeId, currentFriends);
            if (!isFriendOfFriend)
            {
                throw new ForbiddenException(ApiErrorMessages.Friend.PrivacyRequestNotAccepting);
            }
        }

        // BR-FRIEND-CAP-01: Kiểm tra giới hạn số bạn của addressee (nếu requester đã Accepted).
        var existing = await _friendshipRepository.GetByPairAsync(requesterId, request.AddresseeId);

        if (existing != null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
            {
                throw new ConflictException(ApiErrorMessages.Friend.AlreadyFriends);
            }
            if (existing.Status == FriendshipStatus.Pending)
            {
                // BR-FRIEND-02: Phân biệt "tôi đã block" vs "bị họ block".
                if (existing.BlockerIsRequester(existing, requesterId))
                {
                    throw new ForbiddenException(ApiErrorMessages.Friend.AlreadyBlockedOtherParty);
                }
                if (existing.BlockerIsRequester(existing, request.AddresseeId))
                {
                    throw new ForbiddenException(ApiErrorMessages.Friend.BlockedByOtherParty);
                }
                throw new ConflictException(ApiErrorMessages.Friend.PendingRequestAlreadyExists);
            }
            if (existing.Status == FriendshipStatus.Blocked)
            {
                throw new ForbiddenException(ApiErrorMessages.Friend.BlockedByOtherParty);
            }

            // Removed → tái sử dụng record
            existing.RequesterId = requesterId;
            existing.AddresseeId = request.AddresseeId;
            existing.Status = FriendshipStatus.Pending;
            existing.AcceptedAt = null;
            existing.Message = request.Message;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.AddresseeReadAt = null;
        }
        else
        {
            existing = new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = requesterId,
                AddresseeId = request.AddresseeId,
                Status = FriendshipStatus.Pending,
                Message = request.Message,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _friendshipRepository.AddAsync(existing);
        }

        await _friendshipRepository.SaveChangesAsync();

        // Notify addressee (in-app notification / push) - fire-and-forget
        _ = Task.Run(async () =>
        {
            try
            {
                await _friendNoteService.GetMyNotesAsync(request.AddresseeId); // warm cache
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process side-effect after SendFriendRequest");
            }
        });

        var dto = await MapToResponseDtoAsync(existing, requesterId);
        return dto;
    }

    public async Task<FriendshipResponseDto> AcceptFriendRequestAsync(Guid currentUserId, Guid friendshipId)
    {
        var friendship = await _friendshipRepository.GetByIdAsync(friendshipId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.FriendshipNotFound(friendshipId));

        if (friendship.AddresseeId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.NotFriendRequestRecipient);
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.Friend.FriendRequestNotPending);
        }

        // BR-FRIEND-BUG-01: Check cả 2 user còn active trước khi accept.
        var requester = await _userRepository.GetByIdAsync(friendship.RequesterId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(friendship.RequesterId));
        if (!requester.IsActive || requester.AccountStatus != UserAccountStatus.Active)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.RequesterNotActive);
        }

        var addressee = await _userRepository.GetByIdAsync(currentUserId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(currentUserId));
        if (!addressee.IsActive || addressee.AccountStatus != UserAccountStatus.Active)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.AddresseeNotActive);
        }

        // BR-FRIEND-BUG-02: Check block ngược chiều (đã bị requester block trước khi accept).
        var reverseBlock = await _friendshipRepository.GetByPairAsync(currentUserId, friendship.RequesterId);
        if (reverseBlock != null && reverseBlock.Status == FriendshipStatus.Blocked)
        {
            if (reverseBlock.BlockerIsRequester(reverseBlock, friendship.RequesterId))
            {
                // Requester đã block addressee → không thể accept.
                throw new ForbiddenException(ApiErrorMessages.Friend.BlockedByOtherParty);
            }
            if (reverseBlock.BlockerIsRequester(reverseBlock, currentUserId))
            {
                // Addressee đã block requester → không thể accept.
                throw new ForbiddenException(ApiErrorMessages.Friend.AlreadyBlockedOtherParty);
            }
        }

        // BR-FRIEND-CAP-02: Kiểm tra cả 2 bên có vượt FriendLimit.
        await EnforceFriendLimitAsync(currentUserId);
        await EnforceFriendLimitAsync(friendship.RequesterId);

        var now = DateTime.UtcNow;
        friendship.Status = FriendshipStatus.Accepted;
        friendship.AcceptedAt = now;
        friendship.UpdatedAt = now;
        friendship.AddresseeReadAt = now;

        await _friendshipRepository.SaveChangesAsync();

        return await MapToResponseDtoAsync(friendship, currentUserId);
    }

    public async Task<FriendshipResponseDto> DeclineFriendRequestAsync(Guid currentUserId, Guid friendshipId)
    {
        var friendship = await _friendshipRepository.GetByIdAsync(friendshipId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.FriendshipNotFound(friendshipId));

        if (friendship.AddresseeId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.NotFriendRequestRecipient);
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.Friend.FriendRequestNotPending);
        }

        friendship.Status = FriendshipStatus.Removed;
        friendship.UpdatedAt = DateTime.UtcNow;

        await _friendshipRepository.SaveChangesAsync();

        return await MapToResponseDtoAsync(friendship, currentUserId);
    }

    public async Task CancelFriendRequestAsync(Guid currentUserId, Guid friendshipId)
    {
        var friendship = await _friendshipRepository.GetByIdAsync(friendshipId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.FriendshipNotFound(friendshipId));

        if (friendship.RequesterId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.CannotCancelRequestNotRequester);
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.CannotCancelNonPendingRequest);
        }

        friendship.Status = FriendshipStatus.Removed;
        friendship.UpdatedAt = DateTime.UtcNow;

        await _friendshipRepository.SaveChangesAsync();
    }

    public async Task<FriendshipResponseDto> GetFriendRequestByIdAsync(Guid currentUserId, Guid friendshipId)
    {
        var friendship = await _friendshipRepository.GetByIdAsync(friendshipId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.FriendshipNotFound(friendshipId));

        if (friendship.RequesterId != currentUserId && friendship.AddresseeId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.CannotViewRequestNotMember);
        }

        // Ẩn thông tin nếu status = Blocked và mình không phải người chặn
        if (friendship.Status == FriendshipStatus.Blocked
            && friendship.BlockerUserId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.CannotViewBlockedRequest);
        }

        return await MapToResponseDtoAsync(friendship, currentUserId);
    }

    public async Task<IReadOnlyList<FriendshipResponseDto>> GetBlockedUsersAsync(Guid currentUserId)
    {
        var list = await _friendshipRepository.GetByDirectionAsync(
            currentUserId,
            FriendshipRelationshipDirection.BlockedByMe,
            limit: 100);

        return (await Task.WhenAll(list.Select(f => MapToResponseDtoAsync(f, currentUserId))))
            .ToList();
    }

    public async Task<IReadOnlyList<FriendshipResponseDto>> GetBlockedByUsersAsync(Guid currentUserId)
    {
        var list = await _friendshipRepository.GetByDirectionAsync(
            currentUserId,
            FriendshipRelationshipDirection.BlockedByThem,
            limit: 100);

        return (await Task.WhenAll(list.Select(f => MapToResponseDtoAsync(f, currentUserId))))
            .ToList();
    }

    public async Task RemoveFriendshipAsync(Guid currentUserId, Guid friendshipId)
    {
        var friendship = await _friendshipRepository.GetByIdAsync(friendshipId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.FriendshipNotFound(friendshipId));

        if (friendship.RequesterId != currentUserId && friendship.AddresseeId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.CannotRemoveFriendshipNotMember);
        }

        if (friendship.Status != FriendshipStatus.Accepted)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.CannotRemoveAcceptedByOther);
        }

        friendship.Status = FriendshipStatus.Removed;
        friendship.UpdatedAt = DateTime.UtcNow;

        await _friendshipRepository.SaveChangesAsync();

        // BR-FRIEND-CASCADE-01: Khi unfriend → auto-cancel lobby invite Pending giữa 2 bên.
        try
        {
            var otherUserId = friendship.RequesterId == currentUserId
                ? friendship.AddresseeId
                : friendship.RequesterId;
            var cancelled = await _lobbyInviteRepository.CancelPendingBetweenAsync(currentUserId, otherUserId);
            if (cancelled.Count > 0)
            {
                _logger.LogInformation(
                    "Auto-cancelled {Count} lobby invites between {UserA} and {UserB} after unfriend.",
                    cancelled.Count, currentUserId, otherUserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cascade lobby invite cancellation after unfriend.");
        }
    }

    public async Task BlockUserAsync(Guid currentUserId, Guid targetUserId)
    {
        if (targetUserId == currentUserId)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.CannotBlockSelf);
        }

        var targetUser = await _userRepository.GetByIdAsync(targetUserId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(targetUserId));

        if (targetUser.Role == UserRole.Admin)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.CannotBlockAdmin);
        }

        if (!targetUser.IsActive || targetUser.AccountStatus != UserAccountStatus.Active)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.CannotBlockInactiveAccount);
        }

        var existing = await _friendshipRepository.GetByPairAsync(currentUserId, targetUserId);

        if (existing != null)
        {
            // BR-FRIEND-BLOCK-VIEW: Giữ nguyên Requester/Addressee (không đảo chiều)
            // để phân biệt được chiều block. Chỉ cập nhật BlockerUserId thành currentUser.
            existing.BlockerUserId = currentUserId;
            existing.Status = FriendshipStatus.Blocked;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var blocked = new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = currentUserId,
                AddresseeId = targetUserId,
                BlockerUserId = currentUserId,
                Status = FriendshipStatus.Blocked,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _friendshipRepository.AddAsync(blocked);
        }

        await _friendshipRepository.SaveChangesAsync();
    }

    public async Task UnblockUserAsync(Guid currentUserId, Guid targetUserId)
    {
        var existing = await _friendshipRepository.GetByPairAsync(currentUserId, targetUserId);
        if (existing == null || existing.Status != FriendshipStatus.Blocked)
        {
            throw new NotFoundException(ApiErrorMessages.Friend.UnblockNotFound);
        }

        // BR-FRIEND-BLOCK-02: Chỉ người đã block mới có thể bỏ block.
        // Sau migration: kiểm tra BlockerUserId thay vì RequesterId.
        if (existing.BlockerUserId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.CannotUnblockNotBlocker);
        }

        // Clear block - nếu người kia vẫn còn block mình thì giữ Status nhưng clear BlockerUserId
        // để tránh throw trên lần check unblock tiếp theo.
        // Tuy nhiên Status = Blocked vẫn giữ để chiều kia vẫn bị chặn view profile.
        existing.BlockerUserId = null;
        // Nếu Status chỉ là Blocked do chính currentUser → chuyển sang Removed cho sạch.
        existing.Status = FriendshipStatus.Removed;
        existing.UpdatedAt = DateTime.UtcNow;

        await _friendshipRepository.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<FriendSummaryDto>> GetFriendsAsync(Guid userId)
    {
        var friendships = await _friendshipRepository.GetFriendsAsync(userId);
        var result = new List<FriendSummaryDto>(friendships.Count);
        foreach (var f in friendships)
        {
            var other = f.RequesterId == userId ? f.Addressee : f.Requester;
            result.Add(MapToFriendSummary(other, f));
        }
        return result;
    }

    public async Task<IReadOnlyList<FriendshipResponseDto>> GetPendingReceivedRequestsAsync(Guid userId)
    {
        var list = await _friendshipRepository.GetByUserAsync(userId, FriendshipStatus.Pending);
        return (await Task.WhenAll(list
                .Where(f => f.AddresseeId == userId)
                .Select(f => MapToResponseDtoAsync(f, userId))))
            .ToList();
    }

    public async Task<IReadOnlyList<FriendshipResponseDto>> GetPendingSentRequestsAsync(Guid userId)
    {
        var list = await _friendshipRepository.GetByUserAsync(userId, FriendshipStatus.Pending);
        return (await Task.WhenAll(list
                .Where(f => f.RequesterId == userId)
                .Select(f => MapToResponseDtoAsync(f, userId))))
            .ToList();
    }

    public async Task<IReadOnlyList<FriendshipResponseDto>> GetByDirectionAsync(
        Guid currentUserId,
        FriendshipRelationshipDirection direction,
        int limit = 50)
    {
        if (direction == FriendshipRelationshipDirection.None)
            return Array.Empty<FriendshipResponseDto>();

        var list = await _friendshipRepository.GetByDirectionAsync(currentUserId, direction, limit);
        return (await Task.WhenAll(list.Select(f => MapToResponseDtoAsync(f, currentUserId))))
            .ToList();
    }

    public async Task<IReadOnlyList<UserSearchResultDto>> SearchUsersAsync(Guid currentUserId, string keyword, int limit = 20)
    {
        // BR-FRIEND-SEARCH-BLOCK-FILTER: loại bỏ user bị chặn (cả 2 chiều) khỏi kết quả.
        var blockedUserIds = await _friendshipRepository.GetBlockedUserIdsAsync(currentUserId);
        var users = await _userRepository.SearchByUsernameAsync(
            keyword,
            currentUserId,
            limit,
            blockedUserIds);

        var result = new List<UserSearchResultDto>(users.Count);
        foreach (var user in users)
        {
            var pair = await _friendshipRepository.GetByPairAsync(currentUserId, user.Id);
            var mutualCount = await _friendshipRepository.CountMutualFriendsAsync(currentUserId, user.Id);
            result.Add(new UserSearchResultDto
            {
                UserId = user.Id,
                Username = user.Username,
                AvatarUrl = user.Profile?.AvatarUrl,
                KarmaPoints = user.Profile?.KarmaPoints ?? 100,
                FriendshipStatus = pair?.Status.ToString(),
                RelationshipDirection = ResolveDirection(currentUserId, user.Id, pair),
                MutualFriendsCount = mutualCount
            });
        }
        return result;
    }

    /// <summary>
    /// Tính direction quan hệ từ góc nhìn current user (BR-FRIEND-UI-DIRECTION-01).
    /// </summary>
    private static FriendshipRelationshipDirection ResolveDirection(
        Guid currentUserId,
        Guid otherUserId,
        Friendship? pair)
    {
        if (pair is null) return FriendshipRelationshipDirection.None;

        var isRequester = pair.RequesterId == currentUserId;
        var isAddressee = pair.AddresseeId == currentUserId;
        // pair trả về từ GetByPairAsync luôn có 1 trong 2 == currentUserId;
        // nhưng check cả 2 để chắc chắn (phòng trường hợp 1 user self-search).

        return pair.Status switch
        {
            FriendshipStatus.Pending when isRequester => FriendshipRelationshipDirection.OutgoingRequest,
            FriendshipStatus.Pending when isAddressee => FriendshipRelationshipDirection.IncomingRequest,

            FriendshipStatus.Accepted => FriendshipRelationshipDirection.Accepted,

            FriendshipStatus.Blocked when pair.BlockerUserId == currentUserId
                => FriendshipRelationshipDirection.BlockedByMe,
            FriendshipStatus.Blocked when pair.BlockerUserId == otherUserId
                => FriendshipRelationshipDirection.BlockedByThem,

            FriendshipStatus.Removed => FriendshipRelationshipDirection.None,
            _ => FriendshipRelationshipDirection.None
        };
    }

    public async Task<IReadOnlyList<FriendActivityDto>> GetFriendsActivityAsync(Guid userId)
    {
        var friendships = await _friendshipRepository.GetFriendsAsync(userId);
        var result = new List<FriendActivityDto>(friendships.Count);
        foreach (var f in friendships)
        {
            var other = f.RequesterId == userId ? f.Addressee : f.Requester;
            var lastActive = other.Profile?.LastActiveAt;
            result.Add(new FriendActivityDto
            {
                UserId = other.Id,
                Username = other.Username,
                AvatarUrl = other.Profile?.AvatarUrl,
                KarmaPoints = other.Profile?.KarmaPoints ?? 100,
                GamerTier = other.Profile?.GamerTier.ToString(),
                LastActiveAt = lastActive,
                ActivityStatus = ComputeActivityStatus(lastActive),
                FriendsSince = f.AcceptedAt ?? f.UpdatedAt
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<FriendSuggestionDto>> GetFriendSuggestionsAsync(Guid userId, int limit = 20)
    {
        if (userId == Guid.Empty) throw new BadRequestException(ApiErrorMessages.Friend.CannotSuggestToSelf);

        var currentFriends = (await _friendshipRepository.GetFriendUserIdsAsync(userId)).ToHashSet();
        currentFriends.Add(userId);

        // BR-FRIEND-SUGGEST-BLOCK-FILTER: loại bỏ user bị chặn (cả 2 chiều).
        var blockedUserIds = (await _friendshipRepository.GetBlockedUserIdsAsync(userId)).ToHashSet();

        // 1. Friends-of-friends (BR-FRIEND-SUGGEST-01).
        var candidates = new Dictionary<Guid, int>();
        var friendIds = currentFriends.ToList();
        foreach (var friendId in friendIds)
        {
            var friendsOfFriend = await _friendshipRepository.GetFriendUserIdsAsync(friendId);
            foreach (var fofId in friendsOfFriend)
            {
                if (currentFriends.Contains(fofId)) continue;
                if (blockedUserIds.Contains(fofId)) continue;
                candidates.TryGetValue(fofId, out var count);
                candidates[fofId] = count + 1;
            }
        }

        // 2. Cùng chơi trong lobby gần đây (BR-FRIEND-SUGGEST-02).
        var lobbyMembers = await _lobbyMemberRepository.GetRecentMemberUserIdsAsync(userId, daysBack: 30);
        foreach (var lm in lobbyMembers)
        {
            if (currentFriends.Contains(lm)) continue;
            if (blockedUserIds.Contains(lm)) continue;
            candidates.TryGetValue(lm, out var count);
            candidates[lm] = count + 2; // weight cao hơn FOF
        }

        if (candidates.Count == 0) return Array.Empty<FriendSuggestionDto>();

        var topIds = candidates
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(kv => kv.Key)
            .ToList();

        var users = await _userRepository.GetByIdsAsync(topIds);
        var dict = users.ToDictionary(u => u.Id);

        return topIds
            .Where(id => dict.ContainsKey(id))
            .Select(id =>
            {
                var u = dict[id];
                var reason = candidates[id] >= 2 ? "SameLobbyRecent" : "MutualFriends";
                return new FriendSuggestionDto
                {
                    UserId = u.Id,
                    Username = u.Username,
                    AvatarUrl = u.Profile?.AvatarUrl,
                    KarmaPoints = u.Profile?.KarmaPoints ?? 100,
                    GamerTier = u.Profile?.GamerTier.ToString(),
                    MutualFriendsCount = candidates.TryGetValue(id, out var c) ? c : 0,
                    Reason = reason
                };
            })
            .ToList();
    }

    public async Task<IReadOnlyList<MutualFriendDto>> GetMutualFriendsAsync(Guid currentUserId, Guid otherUserId)
    {
        if (currentUserId == otherUserId)
            throw new BadRequestException(ApiErrorMessages.Friend.CannotViewOwnFriendList);

        var mutualIds = await _friendshipRepository.GetMutualFriendIdsAsync(currentUserId, otherUserId);
        if (mutualIds.Count == 0) return Array.Empty<MutualFriendDto>();

        var users = await _userRepository.GetByIdsAsync(mutualIds);
        var userDict = users.ToDictionary(u => u.Id);

        // Lấy thời điểm kết bạn với currentUser.
        var myFriendships = await _friendshipRepository.GetFriendsAsync(currentUserId);
        var friendsSinceDict = myFriendships.ToDictionary(
            f => f.RequesterId == currentUserId ? f.AddresseeId : f.RequesterId,
            f => f.AcceptedAt ?? f.UpdatedAt);

        return mutualIds
            .Where(id => userDict.ContainsKey(id))
            .Select(id =>
            {
                var u = userDict[id];
                return new MutualFriendDto
                {
                    UserId = u.Id,
                    Username = u.Username,
                    AvatarUrl = u.Profile?.AvatarUrl,
                    FriendsSince = friendsSinceDict.TryGetValue(id, out var dt) ? dt : DateTime.UtcNow
                };
            })
            .ToList();
    }

    public async Task<IReadOnlyList<FriendSummaryDto>> GetOtherUserFriendsAsync(Guid currentUserId, Guid otherUserId)
    {
        if (currentUserId == otherUserId)
            throw new BadRequestException(ApiErrorMessages.Friend.CannotViewOwnFriendList);

        var target = await _userRepository.GetByIdWithProfileAsync(otherUserId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(otherUserId));

        var isPublic = target.Profile?.IsFriendListPublic ?? true;
        if (!isPublic)
        {
            // Chỉ bạn bè mới xem được.
            var pair = await _friendshipRepository.GetByPairAsync(currentUserId, otherUserId);
            if (pair == null || pair.Status != FriendshipStatus.Accepted)
            {
                throw new ForbiddenException(ApiErrorMessages.Friend.FriendListPrivate);
            }
        }

        var friendships = await _friendshipRepository.GetFriendsAsync(otherUserId);
        var result = new List<FriendSummaryDto>(friendships.Count);
        foreach (var f in friendships)
        {
            var other = f.RequesterId == otherUserId ? f.Addressee : f.Requester;
            result.Add(MapToFriendSummary(other, f));
        }
        return result;
    }

    public async Task UpdatePrivacyAsync(Guid userId, UpdateFriendPrivacyDto dto)
    {
        var profile = await _userRepository.GetByIdWithProfileAsync(userId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(userId));

        if (profile.Profile == null)
        {
            throw new NotFoundException(ApiErrorMessages.Friend.ProfileNotYetCreated);
        }

        if (dto.IsFriendListPublic.HasValue) profile.Profile.IsFriendListPublic = dto.IsFriendListPublic.Value;
        if (!string.IsNullOrWhiteSpace(dto.AcceptFriendRequestsFrom))
        {
            var allowed = new[] { "Everyone", "FriendsOfFriends" };
            if (!allowed.Contains(dto.AcceptFriendRequestsFrom))
                throw new BadRequestException($"AcceptFriendRequestsFrom phải là một trong: {string.Join(", ", allowed)}.");
            profile.Profile.AcceptFriendRequestsFrom = dto.AcceptFriendRequestsFrom;
        }
        if (dto.FriendLimit.HasValue) profile.Profile.FriendLimit = dto.FriendLimit.Value;

        profile.Profile.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();
    }

    public async Task MarkRequestAsReadAsync(Guid currentUserId, Guid friendshipId)
    {
        var f = await _friendshipRepository.GetByIdAsync(friendshipId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.FriendshipNotFound(friendshipId));

        if (f.AddresseeId != currentUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.NotFriendRequestRecipient);
        }

        if (f.Status != FriendshipStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.Friend.FriendRequestNotPending);
        }

        f.AddresseeReadAt = DateTime.UtcNow;
        f.UpdatedAt = DateTime.UtcNow;

        await _friendshipRepository.SaveChangesAsync();
    }

    public async Task<int> ExpireOldPendingRequestsAsync(int expiryDays = 30)
    {
        if (expiryDays <= 0) expiryDays = 30;

        var cutoff = DateTime.UtcNow.AddDays(-expiryDays);
        var expired = await _friendshipRepository.GetExpiredPendingAsync(cutoff);

        if (expired.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var f in expired)
        {
            f.Status = FriendshipStatus.Removed;
            f.UpdatedAt = now;
        }

        await _friendshipRepository.SaveChangesAsync();
        _logger.LogInformation("Expired {Count} pending friend requests older than {Days} days.", expired.Count, expiryDays);
        return expired.Count;
    }

    public async Task<PlayerProfileDto> GetPlayerProfileAsync(Guid currentUserId, Guid targetUserId)
    {
        if (currentUserId == targetUserId)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.CannotViewOwnProfile);
        }

        var target = await _userRepository.GetByIdWithProfileAsync(targetUserId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(targetUserId));

        // Ẩn profile khi account không còn active (Banned / Suspended) — trả 404 tránh leak thông tin.
        if (!target.IsActive || target.AccountStatus != UserAccountStatus.Active)
        {
            throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(targetUserId));
        }

        var profile = target.Profile;

        // Quan hệ giữa current user và target.
        var pair = await _friendshipRepository.GetByPairAsync(currentUserId, targetUserId);

        // BR-FRIEND-BLOCK-VIEW: Nếu target đã block current (BlockerUserId == targetUserId)
        // → current không được xem profile target.
        // Case cả 2 block nhau: vẫn áp dụng (BlockerUserId == targetUserId là vì target block current
        // hoặc target là người block sau cùng — kết quả current đều bị chặn xem).
        // Current tự block target (BlockerUserId == currentUserId) → current vẫn xem được profile target.
        if (pair?.Status == FriendshipStatus.Blocked && pair.BlockerUserId == targetUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.BlockedCannotViewProfile);
        }

        var relationship = BuildRelationshipDto(pair, currentUserId);

        // BR-FRIEND-REPORT-01: chỉ report được người đang là bạn (Accepted).
        var canReport = relationship.Status == "Accepted";

        // Privacy: chỉ trả friendsCount nếu list public hoặc 2 bên là bạn.
        var showFriendsCount = profile?.IsFriendListPublic ?? true;
        var friendsCount = showFriendsCount
            ? await _friendshipRepository.CountFriendsAsync(targetUserId)
            : 0;

        var mutualCount = relationship.Status == "Accepted"
            ? await _friendshipRepository.CountMutualFriendsAsync(currentUserId, targetUserId)
            : 0;

        // canSendFriendRequest: không thể gửi nếu đã chặn / bị block / đã là bạn / pending tồn tại.
        var canSendFriendRequest = relationship.Status == "None";

        return new PlayerProfileDto
        {
            UserId = target.Id,
            Username = target.Username,
            AvatarUrl = profile?.AvatarUrl,
            AvatarBorderUrl = profile?.AvatarBorderUrl,
            Bio = profile?.Bio,
            FirstName = profile?.FirstName,
            LastName = profile?.LastName,
            GlobalElo = profile?.GlobalElo ?? 1200,
            KarmaPoints = profile?.KarmaPoints ?? 100,
            GamerTier = profile?.GamerTier.ToString() ?? "Bronze",
            Level = profile?.Level ?? 1,
            FriendsCount = friendsCount,
            MutualFriendsCount = mutualCount,
            ActivityStatus = ComputeActivityStatus(profile?.LastActiveAt),
            LastActiveAt = profile?.LastActiveAt,
            JoinedAt = target.CreatedAt,
            Relationship = relationship,
            CanSendFriendRequest = canSendFriendRequest,
            CanReport = canReport
        };
    }

    private RelationshipDto BuildRelationshipDto(Friendship? pair, Guid currentUserId)
    {
        if (pair == null)
        {
            return new RelationshipDto { Status = "None" };
        }

        return pair.Status switch
        {
            FriendshipStatus.Accepted => new RelationshipDto
            {
                Status = "Accepted",
                FriendshipId = pair.Id,
                IsRequester = pair.RequesterId == currentUserId,
                FriendsSince = pair.AcceptedAt ?? pair.UpdatedAt
            },
            FriendshipStatus.Pending => new RelationshipDto
            {
                // Phân biệt PendingSent vs PendingReceived dựa trên chiều requester.
                Status = pair.RequesterId == currentUserId ? "PendingSent" : "PendingReceived",
                FriendshipId = pair.Id,
                IsRequester = pair.RequesterId == currentUserId,
                Message = pair.Message
            },
            FriendshipStatus.Blocked => new RelationshipDto
            {
                // BR-FRIEND-BLOCK-VIEW: Dùng BlockerUserId để xác định chiều block
                // (không phụ thuộc Requester/Addressee, vì sau migration có thể cả 2 đã block nhau).
                Status = pair.BlockerUserId == currentUserId ? "BlockedByMe" : "BlockedByThem",
                FriendshipId = pair.Id
            },
            _ => new RelationshipDto { Status = "None" }
        };
    }

    // === Helpers ===

    private async Task<int> CountSentRequestsInWindowAsync(Guid requesterId, TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        var sent = await _friendshipRepository.GetByUserAsync(requesterId, FriendshipStatus.Pending);
        return sent.Count(f => f.RequesterId == requesterId && f.CreatedAt >= cutoff);
    }

    private async Task<bool> HasAnyFriendOfFriendAsync(Guid requesterId, Guid targetId, HashSet<Guid> currentFriends)
    {
        foreach (var friendId in currentFriends)
        {
            var fof = await _friendshipRepository.GetFriendUserIdsAsync(friendId);
            if (fof.Contains(targetId)) return true;
        }
        return false;
    }

    private async Task EnforceFriendLimitAsync(Guid userId)
    {
        var profile = await _userRepository.GetByIdWithProfileAsync(userId);
        var limit = profile?.Profile?.FriendLimit ?? 0;
        if (limit <= 0) return;

        var count = await _friendshipRepository.CountFriendsAsync(userId);
        if (count >= limit)
        {
            throw new ConflictException(
                $"Người dùng '{profile?.Username}' đã đạt giới hạn {limit} bạn bè. Không thể accept thêm.");
        }
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

    private async Task<FriendshipResponseDto> MapToResponseDtoAsync(Friendship f, Guid currentUserId)
    {
        var other = f.RequesterId == currentUserId ? f.Addressee : f.Requester;
        var mutual = f.Status == FriendshipStatus.Accepted
            ? await _friendshipRepository.CountMutualFriendsAsync(currentUserId, other.Id)
            : 0;

        return new FriendshipResponseDto
        {
            FriendshipId = f.Id,
            OtherUserId = other.Id,
            OtherUsername = other.Username,
            OtherAvatarUrl = other.Profile?.AvatarUrl,
            Status = f.Status.ToString(),
            IsRequester = f.RequesterId == currentUserId,
            CreatedAt = f.CreatedAt,
            AcceptedAt = f.AcceptedAt,
            Message = f.Message,
            AddresseeReadAt = f.AddresseeReadAt,
            MutualFriendsCount = mutual
        };
    }

    private static FriendSummaryDto MapToFriendSummary(User other, Friendship f)
    {
        return new FriendSummaryDto
        {
            UserId = other.Id,
            Username = other.Username,
            AvatarUrl = other.Profile?.AvatarUrl,
            KarmaPoints = other.Profile?.KarmaPoints ?? 100,
            GamerTier = other.Profile?.GamerTier.ToString(),
            FriendsSince = f.AcceptedAt ?? f.UpdatedAt,
            LastActiveAt = other.Profile?.LastActiveAt,
            ActivityStatus = ComputeActivityStatus(other.Profile?.LastActiveAt)
        };
    }
}

internal static class FriendshipStatusExtensions
{
    /// <summary>
    /// Phân biệt chiều block: trả về true nếu userId là người đã thực hiện block.
    /// BR-FRIEND-BLOCK-VIEW: Sau migration, BlockerUserId là nguồn sự thật duy nhất
    /// để biết ai đã block (không phụ thuộc Requester/Addressee).
    /// Backward-compatible: nếu BlockerUserId null (data cũ) thì fallback theo Requester.
    /// </summary>
    public static bool BlockerIsRequester(this Friendship f, Friendship friendship, Guid userId)
    {
        if (friendship.Status != FriendshipStatus.Blocked) return false;
        if (friendship.BlockerUserId.HasValue)
        {
            return friendship.BlockerUserId.Value == userId;
        }
        return friendship.RequesterId == userId;
    }
}
