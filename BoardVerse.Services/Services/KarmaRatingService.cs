using System.Text.Json;
using BoardVerse.Core.DTOs.Rating;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class KarmaRatingService : IKarmaRatingService
    {
        private static readonly JsonSerializerOptions TagJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly IKarmaRatingRepository _karmaRatingRepository;

        public KarmaRatingService(IKarmaRatingRepository karmaRatingRepository)
        {
            _karmaRatingRepository = karmaRatingRepository;
        }

        public async Task<LobbyKarmaRatingContextDto> GetLobbyRatingContextAsync(Guid raterUserId, Guid lobbyId)
        {
            var lobby = await RequireLobbyMemberContextAsync(raterUserId, lobbyId);
            var ratedTargetIds = await _karmaRatingRepository.GetRatedTargetIdsAsync(lobbyId, raterUserId);
            var ratedSet = ratedTargetIds.ToHashSet();

            return new LobbyKarmaRatingContextDto
            {
                LobbyId = lobby.Id,
                LobbyStatus = lobby.Status.ToString(),
                CanSubmitRatings = KarmaRatingHelper.IsRatingAllowed(lobby.Status),
                AvailableTags = KarmaRatingHelper.AvailableTags
                    .Select(tag => new KarmaRatingTagOptionDto
                    {
                        Tag = tag,
                        KarmaWeight = KarmaRatingHelper.TagWeights[tag]
                    })
                    .ToList(),
                MembersToRate = lobby.Members
                    .Where(m => m.UserId != raterUserId)
                    .Select(m => new LobbyMemberRatingTargetDto
                    {
                        UserId = m.UserId,
                        Username = m.User.Username,
                        AvatarUrl = m.User.Profile?.AvatarUrl,
                        AlreadyRated = ratedSet.Contains(m.UserId)
                    })
                    .OrderBy(m => m.Username)
                    .ToList()
            };
        }

        public async Task<SubmitKarmaRatingsResponseDto> SubmitKarmaRatingsAsync(
            Guid raterUserId,
            SubmitKarmaRatingsRequestDto request)
        {
            var lobby = await RequireLobbyMemberContextAsync(raterUserId, request.LobbyId);

            if (!KarmaRatingHelper.IsRatingAllowed(lobby.Status))
            {
                throw new BadRequestException(
                    ApiErrorMessages.Rating.LobbyNotOpenForRating(request.LobbyId));
            }

            ValidateRequestEntries(raterUserId, request, lobby);

            var applied = new List<KarmaRatingAppliedDto>();

            foreach (var entry in request.Ratings)
            {
                if (await _karmaRatingRepository.HasRatingAsync(request.LobbyId, raterUserId, entry.TargetUserId))
                {
                    throw new ConflictException(
                        ApiErrorMessages.Rating.AlreadyRated(request.LobbyId, entry.TargetUserId));
                }

                var distinctTags = entry.Tags.Distinct().ToList();
                var delta = KarmaRatingHelper.CalculateDelta(distinctTags);

                var profile = await _karmaRatingRepository.GetProfileForUpdateAsync(entry.TargetUserId);
                if (profile == null)
                {
                    throw new NotFoundException(
                        ApiErrorMessages.Rating.TargetProfileMissing(entry.TargetUserId));
                }

                var karmaBefore = profile.KarmaPoints;
                profile.KarmaPoints = KarmaRatingHelper.ApplyDeltaToKarmaPoints(profile.KarmaPoints, delta);
                profile.GamerTier = KarmaRatingHelper.ResolveTier(profile.KarmaPoints);
                profile.UpdatedAt = DateTime.UtcNow;

                await _karmaRatingRepository.AddRatingAsync(new PlayerKarmaRating
                {
                    Id = Guid.NewGuid(),
                    LobbyId = request.LobbyId,
                    RaterUserId = raterUserId,
                    TargetUserId = entry.TargetUserId,
                    TagsJson = JsonSerializer.Serialize(distinctTags, TagJsonOptions),
                    KarmaDeltaApplied = delta,
                    CreatedAt = DateTime.UtcNow
                });

                await _karmaRatingRepository.AddKarmaLogAsync(new KarmaLog
                {
                    Id = Guid.NewGuid(),
                    UserId = entry.TargetUserId,
                    ViolationCategory = ResolveViolationCategory(distinctTags),
                    Source = KarmaLogSource.PlayerCrossRating,
                    KarmaPointsChange = delta,
                    KarmaBefore = karmaBefore,
                    KarmaAfter = profile.KarmaPoints,
                    Reason = $"Lobby cross-rating tags: {string.Join(", ", distinctTags)}",
                    RelatedLobbyId = request.LobbyId,
                    PerformedByUserId = raterUserId,
                    IsAdminAdjustment = false,
                    CreatedAt = DateTime.UtcNow
                });

                applied.Add(new KarmaRatingAppliedDto
                {
                    TargetUserId = entry.TargetUserId,
                    Tags = distinctTags,
                    KarmaDeltaApplied = delta,
                    TargetKarmaPointsAfter = profile.KarmaPoints,
                    TargetGamerTier = profile.GamerTier.ToString()
                });
            }

            await _karmaRatingRepository.SaveChangesAsync();

            return new SubmitKarmaRatingsResponseDto
            {
                LobbyId = request.LobbyId,
                AppliedRatings = applied
            };
        }

        public async Task<LobbyKarmaRatingNotificationDto> OpenLobbyKarmaRatingWindowAsync(Guid lobbyId)
        {
            var lobby = await _karmaRatingRepository.GetLobbyForUpdateAsync(lobbyId);
            if (lobby == null)
            {
                throw new NotFoundException(ApiErrorMessages.Rating.LobbyNotFound(lobbyId));
            }

            if (lobby.Status == LobbyStatus.RatingOpen)
            {
                throw new ConflictException(ApiErrorMessages.Rating.LobbyAlreadyOpenForRating(lobbyId));
            }

            if (lobby.Status is not (LobbyStatus.InProgress or LobbyStatus.Closed))
            {
                throw new BadRequestException(ApiErrorMessages.Rating.LobbyCannotOpenRating(lobbyId));
            }

            var openedAt = DateTime.UtcNow;
            lobby.Status = LobbyStatus.RatingOpen;
            lobby.RatingOpenedAt = openedAt;
            lobby.UpdatedAt = openedAt;

            await _karmaRatingRepository.SaveChangesAsync();

            return new LobbyKarmaRatingNotificationDto
            {
                LobbyId = lobbyId,
                MemberUserIds = lobby.Members
                    .Where(m => m.IsActive)
                    .Select(m => m.UserId)
                    .ToList(),
                RatingOpenedAt = openedAt
            };
        }

        private async Task<Lobby> RequireLobbyMemberContextAsync(Guid raterUserId, Guid lobbyId)
        {
            var lobby = await _karmaRatingRepository.GetLobbyForRatingAsync(lobbyId);
            if (lobby == null)
            {
                throw new NotFoundException(ApiErrorMessages.Rating.LobbyNotFound(lobbyId));
            }

            if (!lobby.Members.Any(m => m.IsActive && m.UserId == raterUserId))
            {
                throw new ForbiddenException(
                    ApiErrorMessages.Rating.NotLobbyMember(lobbyId, raterUserId));
            }

            return lobby;
        }

        private static void ValidateRequestEntries(Guid raterUserId, SubmitKarmaRatingsRequestDto request, Lobby lobby)
        {
            var memberIds = lobby.Members
                .Where(m => m.IsActive)
                .Select(m => m.UserId)
                .ToHashSet();

            var seenTargets = new HashSet<Guid>();

            foreach (var entry in request.Ratings)
            {
                if (entry.LobbyId.HasValue && entry.LobbyId.Value != request.LobbyId)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Rating.LobbyNotFound(entry.LobbyId.Value));
                }

                if (entry.Tags == null || entry.Tags.Count == 0)
                {
                    throw new BadRequestException(ApiErrorMessages.Rating.EmptyTagsForEntry);
                }

                if (entry.Tags.Any(tag => !System.Enum.IsDefined(typeof(KarmaRatingTag), tag)))
                {
                    throw new BadRequestException(ApiErrorMessages.Rating.InvalidTagValue);
                }

                if (!seenTargets.Add(entry.TargetUserId))
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Rating.DuplicateTargetInRequest(entry.TargetUserId));
                }

                if (entry.TargetUserId == raterUserId)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Rating.CannotRateSelf(request.LobbyId));
                }

                if (!memberIds.Contains(entry.TargetUserId))
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Rating.TargetNotLobbyMember(request.LobbyId, entry.TargetUserId));
                }
            }
        }

        private static KarmaViolationCategory ResolveViolationCategory(IReadOnlyList<KarmaRatingTag> tags)
        {
            if (tags.Contains(KarmaRatingTag.NoShow))
            {
                return KarmaViolationCategory.NoShow;
            }

            return KarmaViolationCategory.CrossRating;
        }
    }
}
