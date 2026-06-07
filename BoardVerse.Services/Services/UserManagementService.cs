using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
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
                IsBlocked = user.IsBlocked,
                BlockReason = user.BlockReason,
                BlockedAt = user.BlockedAt,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                GamerTag = profile?.GamerTag,
                AvatarUrl = profile?.AvatarUrl,
                Bio = profile?.Bio,
                KarmaPoints = profile?.KarmaPoints ?? 100,
                GamerTier = profile?.GamerTier.ToString() ?? GamerTier.Bronze.ToString(),
                GlobalElo = profile?.GlobalElo ?? 1200,
                Level = profile?.Level ?? 1
            };
        }

        public async Task<List<AdminUserDto>> GetAllAsync(AdminUserQueryDto query)
        {
            if (!string.IsNullOrWhiteSpace(query.Role) && !Enum.TryParse<UserRole>(query.Role, true, out _))
            {
                throw new BadRequestException("Role is invalid.");
            }

            var users = await _userRepository.GetAdminUsersAsync(query);
            return users.Select(MapUser).ToList();
        }

        public async Task<AdminUserDto> GetAsync(Guid id)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(id);
            if (user == null) throw new UserNotFoundException();

            return MapUser(user);
        }

        public async Task<AdminUserDto> CreateAsync(AdminCreateUserDto request)
        {
            if (await _userRepository.UserExistsAsync(request.Email, request.Username))
            {
                throw new UserAlreadyExistsException("User with same email or username exists.");
            }

            if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            {
                throw new BadRequestException("Role is invalid.");
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
            if (user == null) throw new UserNotFoundException();

            if (!string.IsNullOrWhiteSpace(request.Username) && await _userRepository.UsernameExistsAsync(request.Username, id))
            {
                throw new ConflictException("Username already exists.");
            }

            if (!string.IsNullOrWhiteSpace(request.Email) && await _userRepository.EmailExistsAsync(request.Email, id))
            {
                throw new ConflictException("Email already exists.");
            }

            if (!string.IsNullOrWhiteSpace(request.Role) && !Enum.TryParse<UserRole>(request.Role, true, out var role))
            {
                throw new BadRequestException("Role is invalid.");
            }

            user.Username = request.Username ?? user.Username;
            user.Email = request.Email ?? user.Email;

            if (!string.IsNullOrWhiteSpace(request.Role) && Enum.TryParse<UserRole>(request.Role, true, out var parsedRole))
            {
                user.Role = parsedRole;
            }

            if (request.IsActive.HasValue)
            {
                user.IsActive = request.IsActive.Value;
            }

            if (request.IsBlocked.HasValue)
            {
                user.IsBlocked = request.IsBlocked.Value;
                if (!request.IsBlocked.Value)
                {
                    user.BlockReason = null;
                    user.BlockedAt = null;
                }
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
            if (user == null) throw new UserNotFoundException();

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();
        }

        public async Task<AdminUserDto> BlockAsync(Guid id, AdminBlockUserDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(id);
            if (user == null) throw new UserNotFoundException();

            user.IsBlocked = true;
            user.BlockReason = request.Reason;
            user.BlockedAt = DateTime.UtcNow;
            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.SaveChangesAsync();
            return MapUser(user);
        }

        public async Task<AdminUserDto> UnblockAsync(Guid id)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(id);
            if (user == null) throw new UserNotFoundException();

            user.IsBlocked = false;
            user.BlockReason = null;
            user.BlockedAt = null;
            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.SaveChangesAsync();
            return MapUser(user);
        }

        public async Task<AdminUserDto> UpdateRoleAsync(Guid id, AdminUpdateUserRoleDto request)
        {
            var user = await _userRepository.GetByIdWithProfileAsync(id);
            if (user == null) throw new UserNotFoundException();

            if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            {
                throw new BadRequestException("Role is invalid.");
            }

            user.Role = role;
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.SaveChangesAsync();
            return MapUser(user);
        }
    }
}