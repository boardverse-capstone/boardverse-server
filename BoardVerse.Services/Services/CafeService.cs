using BoardVerse.Core.Common;
using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.Messages;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class CafeService : ICafeService
    {
        private readonly ICafeRepository _cafeRepository;
        private readonly IUserProfileRepository _userProfileRepository;
        private readonly ISystemConfigurationProvider _systemConfigurationProvider;

        public CafeService(
            ICafeRepository cafeRepository,
            IUserProfileRepository userProfileRepository,
            ISystemConfigurationProvider systemConfigurationProvider)
        {
            _cafeRepository = cafeRepository;
            _userProfileRepository = userProfileRepository;
            _systemConfigurationProvider = systemConfigurationProvider;
        }

        public async Task<CafeDto> GetCafeAsync(Guid cafeId)
        {
            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            return MapToDto(cafe);
        }

        public async Task<CafeDto> UpdateCafeAsync(Guid cafeId, Guid managerId, UpdateCafeRequestDto dto)
        {
            var cafe = await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                cafe.Name = dto.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(dto.Address))
            {
                cafe.Address = dto.Address.Trim();
            }

            if (dto.PhoneNumber != null)
            {
                cafe.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();
            }

            if (dto.Description != null)
            {
                cafe.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            }

            if (dto.Latitude.HasValue && dto.Longitude.HasValue)
            {
                try
                {
                    GeoLocationHelper.ApplyCoordinates(cafe, dto.Latitude.Value, dto.Longitude.Value);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new BadRequestException(ex.ParamName switch
                    {
                        "latitude" => ApiErrorMessages.Cafe.InvalidLatitudeForCafeUpdate,
                        "longitude" => ApiErrorMessages.Cafe.InvalidLongitudeForCafeUpdate,
                        _ => ApiErrorMessages.Cafe.InvalidLatitudeForCafeUpdate
                    });
                }
            }
            else if (dto.Latitude.HasValue || dto.Longitude.HasValue)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.LocationCoordinatesPairRequired);
            }

            cafe.UpdatedAt = DateTime.UtcNow;
            await _cafeRepository.SaveChangesAsync();

            return MapToDto(cafe);
        }

        public async Task<IEnumerable<CafeDto>> GetManagerCafesAsync(Guid managerId)
        {
            var cafes = await _cafeRepository.GetCafesByManagerIdAsync(managerId);
            return cafes.Select(MapToDto);
        }

        public async Task AddStaffAsync(Guid cafeId, Guid currentManagerId, AddStaffRequestDto dto)
        {
            var cafe = await EnsureManagerOwnsCafeAsync(cafeId, currentManagerId);
            var existingUser = await _cafeRepository.GetUserByEmailAsync(dto.Email);
            User staffUser;

            if (existingUser != null)
            {
                if (existingUser.Role is UserRole.Admin or UserRole.Manager)
                {
                    throw new BadRequestException(ApiErrorMessages.Cafe.StaffAdminOrManagerNotAllowed);
                }

                if (existingUser.Role != UserRole.CafeStaff)
                {
                    throw new BadRequestException(
                        $"User '{dto.Email}' has role '{existingUser.Role}' and is not CafeStaff yet. " +
                        "Call POST /api/cafes/{cafeId}/staff/promote first, then POST /api/cafes/{cafeId}/staff to link them.");
                }

                if (await _cafeRepository.IsStaffMemberExistsAsync(cafeId, existingUser.Id))
                {
                    throw new ConflictException(ApiErrorMessages.Cafe.StaffAlreadyAssigned);
                }

                staffUser = existingUser;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dto.Username))
                {
                    throw new BadRequestException(ApiErrorMessages.Cafe.StaffCreateUsernameRequired);
                }

                var username = await ResolveUsernameAsync(dto.Username, excludedUserId: null);
                staffUser = new User
                {
                    Id = Guid.NewGuid(),
                    Email = dto.Email,
                    Username = username,
                    Role = UserRole.CafeStaff,
                    Provider = "Local",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true,
                    IsEmailVerified = true
                };

                ApplyOptionalPassword(staffUser, dto.Password);
                await _cafeRepository.AddUserAsync(staffUser);
            }

            await LinkStaffToCafeAsync(cafe, staffUser);
        }

        public async Task PromoteUserToStaffAsync(Guid cafeId, Guid currentManagerId, PromoteStaffRequestDto dto)
        {
            var cafe = await EnsureManagerOwnsCafeAsync(cafeId, currentManagerId);
            var user = await _cafeRepository.GetUserByEmailAsync(dto.Email);
            if (user == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.StaffUserNotFound);
            }

            if (user.Role is UserRole.Admin or UserRole.Manager)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.StaffAdminOrManagerNotAllowed);
            }

            if (user.Role == UserRole.CafeStaff)
            {
                throw new BadRequestException(
                    $"User '{dto.Email}' is already CafeStaff. " +
                    "Link them to this cafe via POST /api/cafes/{cafeId}/staff (email only).");
            }

            if (!string.IsNullOrWhiteSpace(dto.Username))
            {
                user.Username = await ResolveUsernameAsync(dto.Username, user.Id);
            }

            user.Role = UserRole.CafeStaff;
            user.UpdatedAt = DateTime.UtcNow;
            ApplyOptionalPassword(user, dto.Password);

            if (await _cafeRepository.IsStaffMemberExistsAsync(cafeId, user.Id))
            {
                throw new ConflictException(ApiErrorMessages.Cafe.StaffAlreadyAssigned);
            }

            await LinkStaffToCafeAsync(cafe, user);
        }

        public async Task<PaginatedResponse<StaffDto>> GetStaffListAsync(
            Guid cafeId,
            Guid currentManagerId,
            PaginationParams paginationParams)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, currentManagerId);
            return await _cafeRepository.GetStaffPagedAsync(cafeId, paginationParams);
        }

        public async Task RemoveStaffAsync(Guid cafeId, Guid currentManagerId, Guid staffId)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, currentManagerId);

            var cafeStaff = await _cafeRepository.GetCafeStaffAsync(cafeId, staffId);
            if (cafeStaff == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.StaffNotFound(cafeId, staffId));
            }

            await _cafeRepository.RemoveCafeStaffAsync(cafeStaff);

            var remainingAssignments = await _cafeRepository.CountActiveStaffAssignmentsAsync(staffId);
            if (remainingAssignments == 0)
            {
                var user = await _cafeRepository.GetUserByIdAsync(staffId);
                if (user != null && user.Role == UserRole.CafeStaff)
                {
                    user.Role = UserRole.Player;
                    user.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _cafeRepository.SaveChangesAsync();
        }

        public async Task<IEnumerable<CafeDto>> GetMyWorkplacesAsync(Guid currentStaffId)
        {
            var cafes = await _cafeRepository.GetCafesByStaffIdAsync(currentStaffId);
            return cafes.Select(MapToDto);
        }

        public async Task<NearbyCafeSearchResultDto> GetNearbyCafesAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid gameTemplateId,
            PaginationParams paginationParams)
        {
            if (gameTemplateId == Guid.Empty)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.GameTemplateIdRequiredForNearbySearch);
            }

            radiusKm = await ResolveMatchmakingRadiusKmAsync(radiusKm);

            try
            {
                GeoLocationHelper.ValidateCoordinates(latitude, longitude);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new BadRequestException(ex.ParamName switch
                {
                    "latitude" => ApiErrorMessages.Cafe.InvalidLatitudeForNearbySearch,
                    "longitude" => ApiErrorMessages.Cafe.InvalidLongitudeForNearbySearch,
                    _ => ApiErrorMessages.Cafe.InvalidLatitudeForNearbySearch
                });
            }

            if (radiusKm is < GeoLocationHelper.MinNearbyRadiusKm or > GeoLocationHelper.MaxNearbyRadiusKm)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.InvalidNearbySearchRadius(
                    GeoLocationHelper.MinNearbyRadiusKm,
                    GeoLocationHelper.MaxNearbyRadiusKm));
            }

            var result = await _cafeRepository.GetNearbyAsync(
                latitude,
                longitude,
                radiusKm,
                gameTemplateId,
                paginationParams);

            var cafes = result.Data.ToList();
            if (cafes.Count > 0)
            {
                await _cafeRepository.EnrichNearbyWithGameWaitAsync(cafes, gameTemplateId);
                result.Data = cafes;

                return new NearbyCafeSearchResultDto
                {
                    Cafes = result,
                    EmptyResultMessage = null,
                    AlternativeSuggestions = []
                };
            }

            if (result.Meta.TotalItems > 0)
            {
                return new NearbyCafeSearchResultDto
                {
                    Cafes = result,
                    EmptyResultMessage = null,
                    AlternativeSuggestions = []
                };
            }

            var alternativeSuggestions = await _cafeRepository.GetAlternativeGameSuggestionsAsync(
                latitude,
                longitude,
                radiusKm,
                gameTemplateId);

            return new NearbyCafeSearchResultDto
            {
                Cafes = result,
                EmptyResultMessage = ApiErrorMessages.Cafe.NoNearbyCafesWithSelectedGameMessage,
                AlternativeSuggestions = alternativeSuggestions
            };
        }

        public async Task<NearbyCafeSearchResultDto> GetNearbyCafesForCurrentUserAsync(
            Guid userId,
            double radiusKm,
            Guid gameTemplateId,
            PaginationParams paginationParams)
        {
            var profile = await _userProfileRepository.GetProfileByUserIdAsync(userId);
            if (profile?.LastKnownLatitude is not { } latitude
                || profile.LastKnownLongitude is not { } longitude)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.SavedLocationRequiredForNearbySearch);
            }

            return await GetNearbyCafesAsync(
                latitude,
                longitude,
                radiusKm,
                gameTemplateId,
                paginationParams);
        }

        private async Task<Cafe> EnsureManagerOwnsCafeAsync(Guid cafeId, Guid currentManagerId)
        {
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            if (cafe.ManagerId != currentManagerId)
            {
                throw new ForbiddenException(ApiErrorMessages.Cafe.ManagerForbidden(cafeId));
            }

            return cafe;
        }

        private async Task LinkStaffToCafeAsync(Cafe cafe, User staffUser)
        {
            var cafeStaff = new CafeStaff
            {
                CafeId = cafe.Id,
                UserId = staffUser.Id,
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _cafeRepository.AddCafeStaffAsync(cafeStaff);
            await _cafeRepository.SaveChangesAsync();
        }

        private async Task<double> ResolveMatchmakingRadiusKmAsync(double radiusKm)
        {
            if (Math.Abs(radiusKm - GeoLocationHelper.DefaultNearbyRadiusKm) > 0.001)
            {
                return radiusKm;
            }

            return await _systemConfigurationProvider.GetDoubleAsync(
                SystemConfigKeys.MatchmakingRadiusKm,
                GeoLocationHelper.DefaultNearbyRadiusKm);
        }

        private async Task<string> ResolveUsernameAsync(string username, Guid? excludedUserId)
        {
            var normalized = username.Trim();
            if (normalized.Length < 3)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.StaffUsernameTooShort);
            }

            if (await _cafeRepository.UsernameExistsAsync(normalized, excludedUserId))
            {
                throw new ConflictException(ApiErrorMessages.Cafe.StaffUsernameTaken);
            }

            return normalized;
        }

        private static void ApplyOptionalPassword(User user, string? password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        }

        private static CafeDto MapToDto(Cafe cafe) => new()
        {
            Id = cafe.Id,
            Name = cafe.Name,
            Address = cafe.Address,
            Latitude = cafe.Latitude,
            Longitude = cafe.Longitude,
            PhoneNumber = cafe.PhoneNumber,
            Description = cafe.Description,
            CreatedAt = cafe.CreatedAt
        };
    }
}
