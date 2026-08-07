using BoardVerse.Core.DTOs.User;

namespace BoardVerse.Services.IServices
{
    public interface IUserProfileService
    {
        Task<ProfileDto> GetPublicProfileAsync(Guid userId);
        Task<ProfileDetailDto> GetInternalProfileAsync(Guid userId);
        Task<ProfileDto> CreateProfileAsync(Guid userId, ProfileCreateDto request);
        Task<ProfileDto> UpdateProfileAsync(Guid userId, ProfileUpdateDto request);
        Task<ProfileDto> UpdateProgressAsync(Guid userId, ProfileProgressUpdateDto request);
        Task<ProfileDto> UpdateAvatarAsync(Guid userId, UpdateAvatarRequestDto request);
        Task<KarmaStateDto> GetKarmaStateAsync(Guid userId);
        Task DeleteProfileAsync(Guid userId);
        Task<ProfileDto> CreateOrGetProfileAsync(Guid userId);
        Task<PlayerLocationDto> GetCurrentLocationAsync(Guid userId);
        Task<PlayerLocationDto> UpdateCurrentLocationAsync(Guid userId, UpdatePlayerLocationRequestDto request);
        Task ClearCurrentLocationAsync(Guid userId);

        /// <summary>
        /// K-05: Update player profile with new fields (cover photo, favorite games).
        /// GamesPlayedCount and WinRate are computed from MatchHistory.
        /// </summary>
        Task<PlayerProfileWithStatsDto> UpdatePlayerProfileAsync(Guid userId, UpdatePlayerProfileDto request);

        /// <summary>
        /// K-04: Add exp for user and auto-update level.
        /// Called when user completes lobby, tournament, or reaches milestone.
        /// </summary>
        Task<(int NewLevel, long RemainingExp)> AddExpAndUpdateLevelAsync(Guid userId, long expToAdd, CancellationToken cancellationToken = default);
    }
}