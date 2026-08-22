using BoardVerse.Core.DTOs.User;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IUserProfileService
    {
        Task<ProfileDto> GetPublicProfileAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<ProfileDetailDto> GetInternalProfileAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<ProfileDto> CreateProfileAsync(Guid userId, ProfileCreateDto request, CancellationToken cancellationToken = default);
        Task<ProfileDto> UpdateProfileAsync(Guid userId, ProfileUpdateDto request, CancellationToken cancellationToken = default);
        Task<ProfileDto> UpdateProgressAsync(Guid userId, ProfileProgressUpdateDto request, CancellationToken cancellationToken = default);
        Task<ProfileDto> UpdateAvatarAsync(Guid userId, UpdateAvatarRequestDto request, CancellationToken cancellationToken = default);
        Task<KarmaStateDto> GetKarmaStateAsync(Guid userId, CancellationToken cancellationToken = default);
        Task DeleteProfileAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<ProfileDto> CreateOrGetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<PlayerLocationDto> GetCurrentLocationAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<PlayerLocationDto> UpdateCurrentLocationAsync(Guid userId, UpdatePlayerLocationRequestDto request, CancellationToken cancellationToken = default);
        Task ClearCurrentLocationAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// K-05: Update player profile with new fields (cover photo, favorite games).
        /// GamesPlayedCount and WinRate are computed from MatchHistory.
        /// </summary>
        Task<PlayerProfileWithStatsDto> UpdatePlayerProfileAsync(Guid userId, UpdatePlayerProfileDto request, CancellationToken cancellationToken = default);

        /// <summary>
        /// K-04: Add exp for user and auto-update level.
        /// Called when user completes lobby, tournament, or reaches milestone.
        /// </summary>
        Task<(int NewLevel, long RemainingExp)> AddExpAndUpdateLevelAsync(Guid userId, long expToAdd, CancellationToken cancellationToken = default);
    }
}