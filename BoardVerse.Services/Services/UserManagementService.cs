using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.Messages;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly IUserManagementRepository _userRepository;

        public UserManagementService(IUserManagementRepository userRepository)
        {
            _userRepository = userRepository;
        }

        private static AdminUserDto MapUser(User user)
        {
            var profile = user.Profile;

            return new AdminUserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role.ToString(),
                AccountStatus = user.AccountStatus.ToString(),
                BlockReason = user.BlockReason,
                BlockedAt = user.BlockedAt,
                LockoutEndDate = user.LockoutEndDate,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                AvatarUrl = profile?.AvatarUrl,
                Bio = profile?.Bio,
                KarmaPoints = profile?.KarmaPoints ?? 100,
                GamerTier = profile?.GamerTier.ToString() ?? GamerTier.Bronze.ToString(),
                GlobalElo = profile?.GlobalElo ?? 1200,
                Level = profile?.Level ?? 1
            };
        }

        public async Task<PaginatedResponse<AdminUserDto>> GetAllAsync(AdminUserQueryDto query)
        {
            if (!string.IsNullOrWhiteSpace(query.Role) && !UserRoleParser.TryParse(query.Role, out _))
            {
                throw new BadRequestException(ApiErrorMessages.AdminUsers.InvalidRoleValue);
            }

            var result = await _userRepository.GetAdminUsersAsync(query);
            var dtoData = result.Data.Select(MapUser).ToList();

            return new PaginatedResponse<AdminUserDto>
            {
                Data = dtoData,
                Meta = result.Meta
            };
        }

        public async Task<AdminUserDto> GetAsync(Guid id)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(id);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.AdminUsers.UserNotFound(id));

            return MapUser(user);
        }

        public async Task<AdminUserDto> CreateAsync(AdminCreateUserDto request)
        {
            if (await _userRepository.UserExistsAsync(request.Email, request.Username))
            {
                throw new UserAlreadyExistsException(ApiErrorMessages.AdminUsers.CreateDuplicate);
            }

            if (!UserRoleParser.TryParse(request.Role, out var role))
            {
                throw new BadRequestException(ApiErrorMessages.AdminUsers.InvalidRoleValue);
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = request.Username,
                Email = request.Email,
                Role = role,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            }

            await _userRepository.AddUserAsync(user);
            await _userRepository.SaveChangesAsync();

            return MapUser(user);
        }

        public async Task<AdminUserDto> UpdateAsync(Guid id, AdminUpdateUserDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(id);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.AdminUsers.UserNotFound(id));

            if (!string.IsNullOrWhiteSpace(request.Username) && await _userRepository.UsernameExistsAsync(request.Username, id))
            {
                throw new ConflictException(ApiErrorMessages.AdminUsers.UsernameConflict(request.Username));
            }

            if (!string.IsNullOrWhiteSpace(request.Email) && await _userRepository.EmailExistsAsync(request.Email, id))
            {
                throw new ConflictException(ApiErrorMessages.AdminUsers.EmailConflict(request.Email));
            }

            if (!string.IsNullOrWhiteSpace(request.Role) && !UserRoleParser.TryParse(request.Role, out _))
            {
                throw new BadRequestException(ApiErrorMessages.AdminUsers.InvalidRoleValue);
            }

            user.Username = request.Username ?? user.Username;
            user.Email = request.Email ?? user.Email;

            if (!string.IsNullOrWhiteSpace(request.Role) && UserRoleParser.TryParse(request.Role, out var parsedRole))
            {
                user.Role = parsedRole;
            }

            if (request.IsActive.HasValue)
            {
                user.IsActive = request.IsActive.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();

            return MapUser(user);
        }

        public async Task DisableAsync(Guid id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.AdminUsers.UserNotFound(id));

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();
        }

        public async Task<AdminUserDto> BlockAsync(Guid id, AdminBlockUserDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(id);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.AdminUsers.UserNotFound(id));

            var utcNow = DateTime.UtcNow;
            user.AccountStatus = UserAccountStatus.Banned;
            user.BlockReason = request.Reason;
            user.BlockedAt = utcNow;
            user.LockoutEndDate = null;
            user.UpdatedAt = utcNow;

            await _userRepository.SaveChangesAsync();
            return MapUser(user);
        }

        public async Task<AdminUserDto> UnblockAsync(Guid id)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(id);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.AdminUsers.UserNotFound(id));

            UserAccessHelper.ClearModerationState(user, DateTime.UtcNow);

            await _userRepository.SaveChangesAsync();
            return MapUser(user);
        }

        public async Task<AdminUserDto> UpdateRoleAsync(Guid id, AdminUpdateUserRoleDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(id);
            if (user == null) throw new UserNotFoundException(ApiErrorMessages.AdminUsers.UserNotFound(id));

            if (!UserRoleParser.TryParse(request.Role, out var role))
            {
                throw new BadRequestException(ApiErrorMessages.AdminUsers.InvalidRoleValue);
            }

            user.Role = role;
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.SaveChangesAsync();
            return MapUser(user);
        }
    }
}