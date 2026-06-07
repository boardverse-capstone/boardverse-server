using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class UserProfileService : IUserProfileService
    {
        private readonly IUserProfileRepository _userRepository;

        public UserProfileService(IUserProfileRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<ProfileDto> GetPublicProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException();

            var profile = user.Profile;
            if (profile != null && !profile.IsActive)
            {
                profile = null;
            }

            return new ProfileDto
            {
                UserId = user.Id,
                Username = user.Username,
                GamerTag = profile?.GamerTag,
                AvatarUrl = profile?.AvatarUrl,
                Bio = profile?.Bio,
                KarmaPoints = profile?.KarmaPoints ?? 100,
                GamerTier = profile?.GamerTier.ToString() ?? GamerTier.Bronze.ToString(),
                GlobalElo = profile?.GlobalElo ?? 1200,
                Level = profile?.Level ?? 1,
                UpdatedAt = profile?.UpdatedAt ?? user.UpdatedAt
            };
        }

        public async Task<ProfileDetailDto> GetInternalProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException();

            var p = user.Profile;
            if (p != null && !p.IsActive)
            {
                throw new ProfileDisabledException();
            }

            return new ProfileDetailDto
            {
                UserId = user.Id,
                Username = user.Username,
                GamerTag = p?.GamerTag,
                AvatarUrl = p?.AvatarUrl,
                Bio = p?.Bio,
                KarmaPoints = p?.KarmaPoints ?? 100,
                GamerTier = p?.GamerTier.ToString() ?? GamerTier.Bronze.ToString(),
                GlobalElo = p?.GlobalElo ?? 1200,
                Level = p?.Level ?? 1,
                FirstName = p?.FirstName,
                LastName = p?.LastName,
                DateOfBirth = p?.DateOfBirth,
                HomeAddress = p?.HomeAddress,
                UpdatedAt = p?.UpdatedAt ?? DateTime.UtcNow
            };
        }

        public async Task<ProfileDto> CreateProfileAsync(Guid userId, ProfileCreateDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException();
            if (user.Profile != null && user.Profile.IsActive) throw new ProfileAlreadyExistsException();

            var profile = new UserProfile
            {
                UserId = userId,
                GamerTag = request.GamerTag ?? user.Username,
                Bio = request.Bio,
                FirstName = request.FirstName,
                LastName = request.LastName,
                DateOfBirth = request.DateOfBirth,
                HomeAddress = request.HomeAddress,
                KarmaPoints = 100,
                GamerTier = GamerTier.Bronze,
                UpdatedAt = DateTime.UtcNow
            };

            await _userRepository.AddUserProfileAsync(profile);
            await _userRepository.SaveChangesAsync();

            return await GetPublicProfileAsync(userId);
        }

        public async Task<ProfileDto> UpdateProfileAsync(Guid userId, ProfileUpdateDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException();

            var p = user.Profile ?? new UserProfile { UserId = user.Id, GamerTag = user.Username };
            if (user.Profile != null && !user.Profile.IsActive)
            {
                p.IsActive = true;
            }

            p.KarmaPoints = p.KarmaPoints <= 0 ? 100 : p.KarmaPoints;

            p.GamerTag = request.GamerTag ?? p.GamerTag;
            p.Bio = request.Bio ?? p.Bio;
            p.FirstName = request.FirstName ?? p.FirstName;
            p.LastName = request.LastName ?? p.LastName;
            p.DateOfBirth = request.DateOfBirth ?? p.DateOfBirth;
            p.HomeAddress = request.HomeAddress ?? p.HomeAddress;
            p.UpdatedAt = DateTime.UtcNow;

            if (user.Profile == null) await _userRepository.AddUserProfileAsync(p);

            await _userRepository.SaveChangesAsync();

            return await GetPublicProfileAsync(userId);
        }

        public async Task<ProfileDto> UpdateProgressAsync(Guid userId, ProfileProgressUpdateDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException();

            var p = user.Profile ?? new UserProfile { UserId = user.Id, GamerTag = user.Username };
            if (user.Profile != null && !user.Profile.IsActive)
            {
                p.IsActive = true;
            }

            p.KarmaPoints = p.KarmaPoints <= 0 ? 100 : p.KarmaPoints;

            p.GlobalElo = request.GlobalElo;
            p.Level = request.Level;
            p.UpdatedAt = DateTime.UtcNow;

            if (user.Profile == null) await _userRepository.AddUserProfileAsync(p);

            await _userRepository.SaveChangesAsync();

            return await GetPublicProfileAsync(userId);
        }

        public async Task DeleteProfileAsync(Guid userId)
        {
            var profile = await _userRepository.GetProfileByUserIdAsync(userId);
            if (profile == null) return;

            profile.IsActive = false;
            profile.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();
        }

        public async Task<ProfileDto> UpdateAvatarAsync(Guid userId, UpdateAvatarRequestDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException();

            var profile = user.Profile ?? new UserProfile
            {
                UserId = user.Id,
                GamerTag = user.Username,
                KarmaPoints = 100,
                GamerTier = GamerTier.Bronze,
                GlobalElo = 1200,
                Level = 1,
                CurrentExp = 0
            };
            profile.AvatarUrl = request.AvatarUrl;
            profile.UpdatedAt = DateTime.UtcNow;

            if (user.Profile == null)
            {
                await _userRepository.AddUserProfileAsync(profile);
            }

            await _userRepository.SaveChangesAsync();
            return await GetPublicProfileAsync(userId);
        }

        public async Task<KarmaStateDto> GetKarmaStateAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException();

            var profile = user.Profile;

            return new KarmaStateDto
            {
                UserId = user.Id,
                Username = user.Username,
                KarmaPoints = profile?.KarmaPoints ?? 100,
                GamerTier = profile?.GamerTier.ToString() ?? GamerTier.Bronze.ToString(),
                AvatarUrl = profile?.AvatarUrl,
                UpdatedAt = profile?.UpdatedAt ?? user.UpdatedAt
            };
        }

        public async Task<ProfileDto> CreateOrGetProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException();

            if (user.Profile == null)
            {
                user.Profile = new UserProfile
                {
                    UserId = user.Id,
                    GamerTag = user.Username,
                    KarmaPoints = 100,
                    GamerTier = GamerTier.Bronze,
                    UpdatedAt = DateTime.UtcNow
                };
                await _userRepository.AddUserProfileAsync(user.Profile);
                await _userRepository.SaveChangesAsync();
            }

            return await GetPublicProfileAsync(userId);
        }
    }
}