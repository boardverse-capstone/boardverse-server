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

        public async Task AddStaffAsync(Guid cafeId, Guid currentManagerId, AddStaffRequestDto dto)
        {
            // Step 1: Find Cafe by cafeId
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException("Cafe not found.");
            }

            // Step 2: Auth Check - Verify the current user is the manager of this cafe
            if (cafe.ManagerId != currentManagerId)
            {
                throw new ForbiddenException("You are not authorized to add staff to this cafe.");
            }

            // Step 3: Multiple Workplaces Check - Search User by email
            var existingUser = await _cafeRepository.GetUserByEmailAsync(dto.Email);
            User staffUser;

            if (existingUser != null)
            {
                // User exists - check if already a staff member of this cafe
                var isAlreadyStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafeId, existingUser.Id);
                if (isAlreadyStaff)
                {
                    throw new ConflictException("This user is already a staff member of this cafe.");
                }

                staffUser = existingUser;
            }
            else
            {
                // User does not exist - Create a new User with Role = Staff
                var emailParts = dto.Email.Split('@');
                if (emailParts.Length < 2 || string.IsNullOrWhiteSpace(emailParts[0]))
                {
                    throw new BadRequestException("Invalid email format");
                }

                staffUser = new User
                {
                    Id = Guid.NewGuid(),
                    Email = dto.Email,
                    Username = emailParts[0], // Use email prefix as username
                    Role = UserRole.CafeStaff,
                    Provider = "Local",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true,
                    IsEmailVerified = true // Manager is vouching for this staff member
                };

                await _cafeRepository.AddUserAsync(staffUser);
            }

            // Step 4: Create the CafeStaff link
            var cafeStaff = new CafeStaff
            {
                CafeId = cafeId,
                UserId = staffUser.Id,
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _cafeRepository.AddCafeStaffAsync(cafeStaff);
            await _cafeRepository.SaveChangesAsync();
        }

        public async Task<PaginatedResponse<StaffDto>> GetStaffListAsync(Guid cafeId, Guid currentManagerId, PaginationParams paginationParams)
        {
            // Step 1: Find Cafe by cafeId
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException("Cafe not found.");
            }

            // Step 2: Auth Check - Verify the current user is the manager of this cafe
            if (cafe.ManagerId != currentManagerId)
            {
                throw new ForbiddenException("You are not authorized to view staff for this cafe.");
            }

            // Step 3: Get paginated staff list
            return await _cafeRepository.GetStaffPagedAsync(cafeId, paginationParams);
        }

        public async Task RemoveStaffAsync(Guid cafeId, Guid currentManagerId, Guid staffId)
        {
            // Step 1: Find Cafe by cafeId
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException("Cafe not found.");
            }

            // Step 2: Auth Check - Verify the current user is the manager of this cafe
            if (cafe.ManagerId != currentManagerId)
            {
                throw new ForbiddenException("You are not authorized to remove staff from this cafe.");
            }

            // Step 3: Find the CafeStaff link
            var cafeStaff = await _cafeRepository.GetCafeStaffAsync(cafeId, staffId);
            if (cafeStaff == null)
            {
                throw new NotFoundException("Staff member not found in this cafe.");
            }

            // Step 4: Remove the CafeStaff link (soft delete - set IsActive = false)
            await _cafeRepository.RemoveCafeStaffAsync(cafeStaff);
            await _cafeRepository.SaveChangesAsync();
        }

        public async Task<IEnumerable<CafeDto>> GetMyWorkplacesAsync(Guid currentStaffId)
        {
            // Step 1: Get cafes where this staff member works
            var cafes = await _cafeRepository.GetCafesByStaffIdAsync(currentStaffId);

            // Step 2: Map to DTOs
            return cafes.Select(cafe => new CafeDto
            {
                Id = cafe.Id,
                Name = cafe.Name,
                Address = cafe.Address,
                PhoneNumber = cafe.PhoneNumber,
                Description = cafe.Description,
                CreatedAt = cafe.CreatedAt
            });
        }
    }
}
