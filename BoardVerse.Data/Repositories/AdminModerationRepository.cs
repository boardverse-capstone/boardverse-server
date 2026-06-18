using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class AdminModerationRepository : IAdminModerationRepository
    {
        private readonly BoardVerseDbContext _context;

        public AdminModerationRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public async Task<PaginatedResponse<KarmaLogDto>> GetKarmaLogsAsync(
            Guid? userId,
            KarmaViolationCategory? violationCategory,
            DateTime? fromUtc,
            DateTime? toUtc,
            PaginationParams pagination)
        {
            var query = _context.KarmaLogs
                .AsNoTracking()
                .Include(k => k.User)
                .AsQueryable();

            if (userId.HasValue)
            {
                query = query.Where(k => k.UserId == userId.Value);
            }

            if (violationCategory.HasValue)
            {
                query = query.Where(k => k.ViolationCategory == violationCategory.Value);
            }

            if (fromUtc.HasValue)
            {
                query = query.Where(k => k.CreatedAt >= fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                query = query.Where(k => k.CreatedAt <= toUtc.Value);
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(k => k.CreatedAt)
                .Skip((pagination.PageNumber - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .Select(k => new KarmaLogDto
                {
                    Id = k.Id,
                    UserId = k.UserId,
                    Username = k.User.Username,
                    ViolationCategory = k.ViolationCategory,
                    Source = k.Source,
                    DeltaAmount = k.DeltaAmount,
                    KarmaBefore = k.KarmaBefore,
                    KarmaAfter = k.KarmaAfter,
                    Reason = k.Reason,
                    RelatedLobbyId = k.RelatedLobbyId,
                    ActorUserId = k.ActorUserId,
                    IsAdminAdjustment = k.IsAdminAdjustment,
                    CreatedAt = k.CreatedAt
                })
                .ToListAsync();

            return new PaginatedResponse<KarmaLogDto>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = pagination.PageNumber,
                    PageSize = pagination.PageSize,
                    TotalItems = total,
                    TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pagination.PageSize)
                }
            };
        }

        public async Task<IReadOnlyList<UserKarmaAlertDto>> GetKarmaAlertsAsync(int threshold)
        {
            return await _context.UserProfiles
                .AsNoTracking()
                .Where(p => p.IsActive && p.KarmaPoints < threshold)
                .Join(
                    _context.Users.Where(u => u.IsActive),
                    profile => profile.UserId,
                    user => user.Id,
                    (profile, user) => new UserKarmaAlertDto
                    {
                        UserId = user.Id,
                        Username = user.Username,
                        Email = user.Email,
                        KarmaPoints = profile.KarmaPoints,
                        GamerTier = profile.GamerTier.ToString(),
                        ProfileUpdatedAt = profile.UpdatedAt
                    })
                .OrderBy(a => a.KarmaPoints)
                .ThenBy(a => a.Username)
                .ToListAsync();
        }

        public Task<User?> GetUserWithProfileForUpdateAsync(Guid userId) =>
            _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.Id == userId);

        public Task<UserProfile?> GetProfileForUpdateAsync(Guid userId) =>
            _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        public async Task AddKarmaLogAsync(KarmaLog log)
        {
            await _context.KarmaLogs.AddAsync(log);
        }

        public Task SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
