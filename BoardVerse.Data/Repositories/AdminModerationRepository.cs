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
                    KarmaPointsChange = k.KarmaPointsChange,
                    KarmaBefore = k.KarmaBefore,
                    KarmaAfter = k.KarmaAfter,
                    Reason = k.Reason,
                    RelatedLobbyId = k.RelatedLobbyId,
                    PerformedByUserId = k.PerformedByUserId,
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

        public async Task<PaginatedResponse<CoolingOffUserDto>> GetCoolingOffUsersAsync(PaginationParams pagination)
        {
            var query = _context.Wallets
                .AsNoTracking()
                .Include(w => w.User)
                .Where(w => w.IsCoolingOff && w.CoolingOffExpiresAt > DateTime.UtcNow)
                .AsQueryable();

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(w => w.CoolingOffExpiresAt)
                .Skip((pagination.PageNumber - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .Select(w => new CoolingOffUserDto
                {
                    UserId = w.UserId,
                    Username = w.User.Username,
                    Email = w.User.Email,
                    IsCoolingOff = w.IsCoolingOff,
                    CoolingOffExpiresAt = w.CoolingOffExpiresAt,
                    CoolingOffDaysRemaining = w.CoolingOffExpiresAt.HasValue
                        ? (int)Math.Max(0, (w.CoolingOffExpiresAt.Value - DateTime.UtcNow).TotalDays)
                        : 0,
                    FailedLobbiesInWeek = 0,
                    CancelledLobbiesInWeek = 0,
                    TotalForfeitedBvc = 0,
                    CoolingOffStartedAt = w.UpdatedAt
                })
                .ToListAsync();

            return new PaginatedResponse<CoolingOffUserDto>
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

        public Task<Wallet?> GetWalletForUpdateAsync(Guid userId) =>
            _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

        public async Task<PaginatedResponse<PlayerActionHistoryDto>> GetPlayerActionHistoryAsync(PlayerActionHistoryQuery q)
        {
            var query = _context.PlayerActionHistories.AsNoTracking().AsQueryable();

            if (q.UserId.HasValue)
            {
                query = query.Where(h => h.UserId == q.UserId.Value);
            }

            if (q.ActionType.HasValue)
            {
                query = query.Where(h => h.ActionType == q.ActionType.Value);
            }

            if (q.FromUtc.HasValue)
            {
                query = query.Where(h => h.CreatedAt >= q.FromUtc.Value);
            }

            if (q.ToUtc.HasValue)
            {
                query = query.Where(h => h.CreatedAt <= q.ToUtc.Value);
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(h => h.CreatedAt)
                .Skip((q.PageNumber - 1) * q.PageSize)
                .Take(q.PageSize)
                .Select(h => new PlayerActionHistoryDto
                {
                    Id = h.Id,
                    UserId = h.UserId,
                    ActionType = h.ActionType,
                    ActionBy = h.ActionBy,
                    Reason = h.Reason,
                    Metadata = h.Metadata,
                    CreatedAt = h.CreatedAt,
                    ExpiresAt = h.ExpiresAt
                })
                .ToListAsync();

            return new PaginatedResponse<PlayerActionHistoryDto>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = q.PageNumber,
                    PageSize = q.PageSize,
                    TotalItems = total,
                    TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)q.PageSize)
                }
            };
        }
    }
}
