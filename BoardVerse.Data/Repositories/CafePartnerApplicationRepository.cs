using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class CafePartnerApplicationRepository : ICafePartnerApplicationRepository
    {
        private readonly BoardVerseDbContext _context;

        public CafePartnerApplicationRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public Task AddAsync(CafePartnerApplication application)
        {
            _context.CafePartnerApplications.Add(application);
            return Task.CompletedTask;
        }

        public Task AddCafeAsync(Cafe cafe)
        {
            _context.Cafes.Add(cafe);
            return Task.CompletedTask;
        }

        public async Task<CafePartnerApplication?> GetByIdAsync(Guid id)
        {
            return await _context.CafePartnerApplications
                .Include(a => a.SubmittedByUser)
                .Include(a => a.ReviewedByAdmin)
                .Include(a => a.CreatedManager)
                .Include(a => a.CreatedCafe)
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task<CafePartnerApplication?> GetApprovedByManagerUserIdAsync(Guid managerUserId)
        {
            return await _context.CafePartnerApplications
                .Include(a => a.CreatedCafe)
                .FirstOrDefaultAsync(a =>
                    a.CreatedManagerUserId == managerUserId &&
                    a.Status == CafePartnerApplicationStatus.Approved);
        }

        public async Task<bool> HasOpenApplicationByEmailAsync(string email)
        {
            var normalized = email.Trim().ToLowerInvariant();
            return await _context.CafePartnerApplications.AnyAsync(a =>
                a.RepresentativeEmail.ToLower() == normalized &&
                a.Status == CafePartnerApplicationStatus.PendingApproval);
        }

        public async Task<bool> HasSevereDuplicateAsync(string businessLicense, string normalizedAddress, Guid? excludeApplicationId = null)
        {
            var license = businessLicense.Trim().ToUpperInvariant();
            var address = normalizedAddress.Trim().ToLowerInvariant();

            var cafeAddressMatch = await _context.Cafes.AnyAsync(c =>
                c.Address.Trim().ToLower() == address &&
                c.PartnerOperationalStatus != null);

            if (cafeAddressMatch)
            {
                return true;
            }

            var licenseOnCafe = await _context.CafePartnerApplications.AnyAsync(a =>
                a.BusinessLicense.ToUpper() == license &&
                a.Status == CafePartnerApplicationStatus.Approved &&
                a.CreatedCafeId != null);

            if (licenseOnCafe)
            {
                return true;
            }

            var applicationsQuery = _context.CafePartnerApplications
                .Where(a => a.Status != CafePartnerApplicationStatus.Rejected);

            if (excludeApplicationId.HasValue)
            {
                applicationsQuery = applicationsQuery.Where(a => a.Id != excludeApplicationId.Value);
            }

            return await applicationsQuery.AnyAsync(a =>
                a.BusinessLicense.ToUpper() == license ||
                a.Address.Trim().ToLower() == address);
        }

        public async Task<PaginatedResponse<CafePartnerApplication>> GetPagedAsync(AdminCafePartnerApplicationQueryDto query)
        {
            var applicationsQuery = _context.CafePartnerApplications
                .AsNoTracking()
                .Include(a => a.SubmittedByUser)
                .Include(a => a.ReviewedByAdmin)
                .Include(a => a.CreatedCafe)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                applicationsQuery = applicationsQuery.Where(a =>
                    a.CafeName.Contains(search) ||
                    a.RepresentativeEmail.Contains(search) ||
                    a.BusinessLicense.Contains(search));
            }

            if (query.Status.HasValue)
            {
                applicationsQuery = applicationsQuery.Where(a => a.Status == query.Status.Value);
            }

            var totalItems = await applicationsQuery.CountAsync();
            var pageSize = query.PageSize;
            var pageNumber = query.Page;
            var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

            var data = await applicationsQuery
                .OrderByDescending(a => a.SubmittedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PaginatedResponse<CafePartnerApplication>
            {
                Data = data,
                Meta = new PaginationMeta
                {
                    CurrentPage = pageNumber,
                    PageSize = pageSize,
                    TotalItems = totalItems,
                    TotalPages = totalPages
                }
            };
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
