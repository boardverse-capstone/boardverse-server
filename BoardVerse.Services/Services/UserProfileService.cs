using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.Messages;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Geocoding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services
{
    public class UserProfileService : IUserProfileService
    {
        private readonly IUserProfileRepository _userRepository;
        private readonly ILevelingService _levelingService;
        private readonly IPlayerGeocodingService _geocodingService;
        private readonly ILogger<UserProfileService> _logger;

        public UserProfileService(
            IUserProfileRepository userRepository,
            ILevelingService levelingService,
            IPlayerGeocodingService geocodingService,
            ILogger<UserProfileService>? logger = null)
        {
            _userRepository = userRepository;
            _levelingService = levelingService;
            _geocodingService = geocodingService;
            _logger = logger!;
        }

        public async Task<ProfileDto> GetPublicProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundPublic);

            var profile = user.Profile;
            var hasActiveProfile = profile is { IsActive: true };
            var hasProfile = ProfileCompletionRules.ResolveHasProfile(user.Role, hasActiveProfile);
            if (!hasActiveProfile)
            {
                profile = null;
            }

            return new ProfileDto
            {
                UserId = user.Id,
                Username = user.Username,
                PhoneNumber = user.PhoneNumber,
                AvatarUrl = profile?.AvatarUrl,
                AvatarBorderUrl = profile?.AvatarBorderUrl,
                Bio = profile?.Bio,
                FirstName = profile?.FirstName,
                LastName = profile?.LastName,
                DateOfBirth = profile?.DateOfBirth,
                KarmaPoints = profile?.KarmaPoints ?? 100,
                GamerTier = profile?.GamerTier.ToString() ?? GamerTier.Bronze.ToString(),
                GlobalElo = profile?.GlobalElo ?? 1200,
                Level = profile?.Level ?? 1,
                CurrentExp = profile?.CurrentExp ?? 0,
                LastActiveAt = profile?.LastActiveAt,
                UpdatedAt = profile?.UpdatedAt ?? user.UpdatedAt,
                HasProfile = hasProfile,
                IsFriendListPublic = profile?.IsFriendListPublic ?? true,
                AcceptFriendRequestsFrom = profile?.AcceptFriendRequestsFrom ?? "Everyone",
                FriendLimit = profile?.FriendLimit ?? 0
            };
        }

        public async Task<ProfileDetailDto> GetInternalProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundPrivate);

            var p = user.Profile;
            var hasActiveProfile = p is { IsActive: true };
            var hasProfile = ProfileCompletionRules.ResolveHasProfile(user.Role, hasActiveProfile);
            if (p != null && !p.IsActive)
            {
                throw new ProfileDisabledException(ApiErrorMessages.Profile.ProfileDisabled);
            }

            return new ProfileDetailDto
            {
                UserId = user.Id,
                Username = user.Username,
                PhoneNumber = user.PhoneNumber,
                AvatarUrl = p?.AvatarUrl,
                Bio = p?.Bio,
                KarmaPoints = p?.KarmaPoints ?? 100,
                GamerTier = p?.GamerTier.ToString() ?? GamerTier.Bronze.ToString(),
                GlobalElo = p?.GlobalElo ?? 1200,
                Level = p?.Level ?? 1,
                FirstName = p?.FirstName,
                LastName = p?.LastName,
                DateOfBirth = p?.DateOfBirth,
                UpdatedAt = p?.UpdatedAt ?? DateTime.UtcNow,
                HasProfile = hasProfile
            };
        }

        public async Task<ProfileDto> CreateProfileAsync(Guid userId, ProfileCreateDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundCreate);
            if (user.Profile != null && user.Profile.IsActive)
                throw new ProfileAlreadyExistsException(ApiErrorMessages.Profile.ProfileAlreadyExists);

            // Reactivate existing inactive profile instead of creating a duplicate row
            if (user.Profile != null && !user.Profile.IsActive)
            {
                var p = user.Profile;
                p.Bio = request.Bio ?? p.Bio;
                p.FirstName = request.FirstName ?? p.FirstName;
                p.LastName = request.LastName ?? p.LastName;
                p.DateOfBirth = request.DateOfBirth ?? p.DateOfBirth;
                p.IsActive = true;
                p.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var profile = new UserProfile
                {
                    UserId = userId,
                    Bio = request.Bio,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    DateOfBirth = request.DateOfBirth,
                    KarmaPoints = 100,
                    GamerTier = GamerTier.Bronze,
                    UpdatedAt = DateTime.UtcNow
                };
                await _userRepository.AddUserProfileAsync(profile);
            }

            ApplyPhoneNumber(user, request.PhoneNumber);

            await _userRepository.SaveChangesAsync();
            return await GetPublicProfileAsync(userId);
        }

        public async Task<ProfileDto> UpdateProfileAsync(Guid userId, ProfileUpdateDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundUpdate);

            var p = user.Profile ?? new UserProfile { UserId = user.Id };
            if (user.Profile != null && !user.Profile.IsActive)
            {
                p.IsActive = true;
            }

            p.KarmaPoints = p.KarmaPoints <= 0 ? 100 : p.KarmaPoints;

            p.Bio = request.Bio ?? p.Bio;
            p.FirstName = request.FirstName ?? p.FirstName;
            p.LastName = request.LastName ?? p.LastName;
            p.DateOfBirth = request.DateOfBirth ?? p.DateOfBirth;
            p.UpdatedAt = DateTime.UtcNow;

            ApplyPhoneNumber(user, request.PhoneNumber);

            if (user.Profile == null) await _userRepository.AddUserProfileAsync(p);

            await _userRepository.SaveChangesAsync();
            return await GetPublicProfileAsync(userId);
        }

        public async Task<ProfileDto> UpdateProgressAsync(Guid userId, ProfileProgressUpdateDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundUpdateProgress);

            var p = user.Profile ?? new UserProfile { UserId = user.Id };
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
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundUpdateAvatar);

            var profile = user.Profile ?? new UserProfile
            {
                UserId = user.Id,
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
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundKarma);

            var profile = user.Profile;

            var karmaLogs = await _userRepository.GetKarmaLogsAsync(userId, limit: 50);
            var recentHistory = karmaLogs.Select(log => new KarmaLogEntryDto
            {
                Id = log.Id,
                KarmaChange = (int)log.KarmaPointsChange,
                KarmaBefore = log.KarmaBefore,
                KarmaAfter = log.KarmaAfter,
                ViolationCategory = log.ViolationCategory,
                Note = log.Reason,
                CreatedAt = log.CreatedAt
            }).ToList();

            return new KarmaStateDto
            {
                UserId = user.Id,
                Username = user.Username,
                KarmaPoints = profile?.KarmaPoints ?? 100,
                GamerTier = profile?.GamerTier.ToString() ?? GamerTier.Gold.ToString(),
                AvatarUrl = profile?.AvatarUrl,
                UpdatedAt = profile?.UpdatedAt ?? user.UpdatedAt,
                RecentHistory = recentHistory
            };
        }

        public async Task<ProfileDto> CreateOrGetProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundCreateOrGet);

            if (user.Profile == null)
            {
                user.Profile = new UserProfile
                {
                    UserId = user.Id,
                    KarmaPoints = 100,
                    GamerTier = GamerTier.Bronze,
                    UpdatedAt = DateTime.UtcNow
                };
                await _userRepository.AddUserProfileAsync(user.Profile);
                await _userRepository.SaveChangesAsync();
            }

            return await GetPublicProfileAsync(userId);
        }

        public async Task<PlayerLocationDto> GetCurrentLocationAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null)
            {
                throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundGetLocation);
            }

            return MapToPlayerLocationDto(user.Profile);
        }

        public async Task<PlayerLocationDto> UpdateCurrentLocationAsync(
            Guid userId,
            UpdatePlayerLocationRequestDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null)
            {
                throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundUpdateLocation);
            }

            try
            {
                GeoLocationHelper.ValidateCoordinates(request.Latitude, request.Longitude);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new BadRequestException(ex.ParamName switch
                {
                    "latitude" => ApiErrorMessages.Profile.InvalidLatitudeForLocationUpdate,
                    "longitude" => ApiErrorMessages.Profile.InvalidLongitudeForLocationUpdate,
                    _ => ApiErrorMessages.Profile.InvalidLatitudeForLocationUpdate
                });
            }

            var profile = await EnsureProfileRowAsync(user, userId);
            GeoLocationHelper.ApplyLastKnownLocation(
                profile,
                request.Latitude,
                request.Longitude,
                request.Source);
            profile.UpdatedAt = DateTime.UtcNow;

            // ====== Reverse-geocode (Nominatim) ======
            // Gọi async trước khi insert history để snapshot label tại thời điểm ghi.
            // Fail-soft: nếu Nominatim fail, vẫn lưu lat/lng bình thường,
            // history/label để null. Không throw ra controller.
            ReverseGeocodeResult? resolved = null;
            try
            {
                resolved = await _geocodingService.ReverseGeocodeAsync(
                    request.Latitude,
                    request.Longitude);
            }
            catch (Exception ex)
            {
                // Log nhưng không fail request — UX quan trọng hơn việc hiển thị tên Quận.
                _logger?.LogWarning(
                    ex,
                    "Reverse geocode failed for user {UserId} at ({Lat}, {Lng}); continuing without label.",
                    userId,
                    request.Latitude,
                    request.Longitude);
            }

            if (resolved is not null)
            {
                profile.LastResolvedDistrict = resolved.District;
                profile.LastResolvedCity = resolved.City;
                profile.LastResolvedCountry = resolved.Country;
                profile.LastResolvedDisplayName = resolved.DisplayName;
                profile.LastResolvedAt = DateTime.UtcNow;
            }

            await _userRepository.AddPlayerLocationHistoryAsync(new PlayerLocationHistory
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Source = request.Source,
                RecordedAt = DateTime.UtcNow,
                ResolvedDistrict = resolved?.District,
                ResolvedCity = resolved?.City,
                ResolvedCountry = resolved?.Country,
                ResolvedDisplayName = resolved?.DisplayName
            });

            await _userRepository.SaveChangesAsync();
            return MapToPlayerLocationDto(profile);
        }

        public async Task ClearCurrentLocationAsync(Guid userId)
        {
            var profile = await _userRepository.GetProfileByUserIdAsync(userId);
            if (profile == null)
            {
                throw new ProfileNotFoundException(ApiErrorMessages.Profile.ProfileNotFoundClearLocation);
            }

            if (!GeoLocationHelper.HasLastKnownLocation(profile))
            {
                throw new NotFoundException(ApiErrorMessages.Profile.NoSavedLocationToClear);
            }

            GeoLocationHelper.ClearLastKnownLocation(profile);
            ClearResolvedLocation(profile);
            profile.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();
        }

        private static void ClearResolvedLocation(UserProfile profile)
        {
            profile.LastResolvedDistrict = null;
            profile.LastResolvedCity = null;
            profile.LastResolvedCountry = null;
            profile.LastResolvedDisplayName = null;
            profile.LastResolvedAt = null;
        }

        private async Task<UserProfile> EnsureProfileRowAsync(User user, Guid userId)
        {
            if (user.Profile != null)
            {
                return user.Profile;
            }

            var profile = new UserProfile
            {
                UserId = userId,
                KarmaPoints = 100,
                GamerTier = GamerTier.Bronze,
                GlobalElo = 1200,
                Level = 1,
                CurrentExp = 0,
                UpdatedAt = DateTime.UtcNow
            };
            await _userRepository.AddUserProfileAsync(profile);
            user.Profile = profile;
            return profile;
        }

        private static PlayerLocationDto MapToPlayerLocationDto(UserProfile? profile)
        {
            var hasLocation = profile != null && GeoLocationHelper.HasLastKnownLocation(profile);
            var hasResolvedName = hasLocation
                && !string.IsNullOrWhiteSpace(profile!.LastResolvedDisplayName);

            return new PlayerLocationDto
            {
                Latitude = profile?.LastKnownLatitude,
                Longitude = profile?.LastKnownLongitude,
                UpdatedAt = profile?.LastLocationUpdatedAt,
                Source = profile?.LastLocationSource?.ToString(),
                HasLocation = hasLocation,
                District = profile?.LastResolvedDistrict,
                City = profile?.LastResolvedCity,
                Country = profile?.LastResolvedCountry,
                DisplayName = profile?.LastResolvedDisplayName,
                HasResolvedName = hasResolvedName
            };
        }

        private static void ApplyPhoneNumber(User user, string? phoneNumber)
        {
            if (phoneNumber == null)
            {
                return;
            }

            user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
            user.UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// K-05: Update player profile with cover photo, favorite games.
        /// GamesPlayedCount and WinRate are computed from MatchHistory.
        /// </summary>
        public async Task<PlayerProfileWithStatsDto> UpdatePlayerProfileAsync(
            Guid userId,
            UpdatePlayerProfileDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundUpdate);

            var p = user.Profile ?? new UserProfile { UserId = user.Id };
            if (user.Profile != null && !user.Profile.IsActive)
            {
                p.IsActive = true;
            }

            p.CoverPhotoUrl = request.CoverPhotoUrl;
            p.Bio = request.Bio ?? p.Bio;
            p.FirstName = request.FirstName ?? p.FirstName;
            p.LastName = request.LastName ?? p.LastName;
            if (request.PreferredPlayMode.HasValue)
                p.PreferredPlayMode = request.PreferredPlayMode.Value;
            p.UpdatedAt = DateTime.UtcNow;

            if (request.FavoriteGameIds != null)
            {
                var ids = request.FavoriteGameIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .Take(20)
                    .ToList();
                p.FavoriteGamesJson = ids.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(ids)
                    : null;
            }

            if (user.Profile == null) await _userRepository.AddUserProfileAsync(p);
            await _userRepository.SaveChangesAsync();

            // Compute game stats
            var (gamesPlayed, gamesWon) = await _userRepository.GetMatchHistoryStatsAsync(userId);
            var winRate = gamesPlayed > 0 ? Math.Round((double)gamesWon / gamesPlayed * 100, 1) : 0;

            var favoriteIds = !string.IsNullOrWhiteSpace(p.FavoriteGamesJson)
                ? System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(p.FavoriteGamesJson) ?? []
                : [];

            return new PlayerProfileWithStatsDto
            {
                UserId = user.Id,
                Username = user.Username,
                AvatarUrl = p.AvatarUrl,
                AvatarBorderUrl = p.AvatarBorderUrl,
                CoverPhotoUrl = p.CoverPhotoUrl,
                Bio = p.Bio,
                FirstName = p.FirstName,
                LastName = p.LastName,
                KarmaPoints = p.KarmaPoints,
                GamerTier = p.GamerTier.ToString(),
                GlobalElo = p.GlobalElo,
                Level = p.Level,
                GamesPlayedCount = gamesPlayed,
                WinRate = winRate,
                FavoriteGameIds = favoriteIds,
                PreferredPlayMode = p.PreferredPlayMode,
                UpdatedAt = p.UpdatedAt,
                HasProfile = true
            };
        }

        /// <summary>
        /// K-04: Thêm exp cho user và tự động tính lại level.
        /// Được gọi khi user hoàn thành lobby, tournament, hoặc đạt milestone.
        /// </summary>
        /// <param name="userId">User nhận exp.</param>
        /// <param name="expToAdd">Số exp cần thêm (luôn dương).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Level mới và exp còn lại.</returns>
        public async Task<(int NewLevel, long RemainingExp)> AddExpAndUpdateLevelAsync(
            Guid userId,
            long expToAdd,
            CancellationToken cancellationToken = default)
        {
            if (expToAdd <= 0)
            {
                throw new BadRequestException("Exp phải lớn hơn 0.");
            }

            var user = await _userRepository.GetByIdWithProfileAsync(userId);
            if (user?.Profile == null)
            {
                throw new UserNotFoundException(ApiErrorMessages.Profile.UserNotFoundPublic);
            }

            var profile = user.Profile;
            var newTotalExp = profile.CurrentExp + expToAdd;
            var newLevel = (int)_levelingService.CalculateLevel(newTotalExp);

            profile.CurrentExp = (int)newTotalExp;
            profile.Level = newLevel;
            profile.UpdatedAt = DateTime.UtcNow;

            await _userRepository.SaveChangesAsync();

            var remainingExp = newTotalExp - GetExpStartForLevel(newLevel);

            return (newLevel, remainingExp);
        }

        private long GetExpStartForLevel(int level)
        {
            if (level <= 1) return 0;
            var n = level - 1;
            return 50L * n * (n + 1);
        }
    }
}