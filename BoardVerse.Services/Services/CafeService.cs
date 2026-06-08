using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class CafeService : ICafeService
    {
        private readonly ICafeRepository _cafeRepository;

        public CafeService(ICafeRepository cafeRepository)
        {
            _cafeRepository = cafeRepository;
        }

        public async Task<CafeDto> GetCafeAsync(Guid cafeId)
        {
            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException("Cafe not found.");
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
                    throw new BadRequestException("Cannot add an Admin or Manager account as cafe staff.");
                }

                if (existingUser.Role != UserRole.CafeStaff)
                {
                    throw new BadRequestException(
                        $"User '{dto.Email}' has role '{existingUser.Role}' and is not CafeStaff yet. " +
                        "Call POST /api/cafes/{cafeId}/staff/promote first, then POST /api/cafes/{cafeId}/staff to link them.");
                }

                if (await _cafeRepository.IsStaffMemberExistsAsync(cafeId, existingUser.Id))
                {
                    throw new ConflictException("This user is already a staff member of this cafe.");
                }

                staffUser = existingUser;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dto.Username))
                {
                    throw new BadRequestException("Username is required when creating a new staff account.");
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
                throw new NotFoundException("User not found.");
            }

            if (user.Role is UserRole.Admin or UserRole.Manager)
            {
                throw new BadRequestException("Cannot promote an Admin or Manager account to cafe staff.");
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
                throw new ConflictException("This user is already a staff member of this cafe.");
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
                throw new NotFoundException("Staff member not found in this cafe.");
            }

            await _cafeRepository.RemoveCafeStaffAsync(cafeStaff);

            var remainingAssignments = await _cafeRepository.CountActiveStaffAssignmentsAsync(staffId);
            if (remainingAssignments == 0)
            {
                var user = await _cafeRepository.GetUserByIdAsync(staffId);
                if (user != null && user.Role == UserRole.CafeStaff)
                {
                    user.Role = UserRole.User;
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

        private async Task<Cafe> EnsureManagerOwnsCafeAsync(Guid cafeId, Guid currentManagerId)
        {
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException("Cafe not found.");
            }

            if (cafe.ManagerId != currentManagerId)
            {
                throw new ForbiddenException("You are not authorized to manage this cafe.");
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

        private async Task<string> ResolveUsernameAsync(string username, Guid? excludedUserId)
        {
            var normalized = username.Trim();
            if (normalized.Length < 3)
            {
                throw new BadRequestException("Username must be at least 3 characters.");
            }

            if (await _cafeRepository.UsernameExistsAsync(normalized, excludedUserId))
            {
                throw new ConflictException("Username is already taken.");
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
            PhoneNumber = cafe.PhoneNumber,
            Description = cafe.Description,
            CreatedAt = cafe.CreatedAt
        };
    }
}
