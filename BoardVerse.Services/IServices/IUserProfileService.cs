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
    }
}