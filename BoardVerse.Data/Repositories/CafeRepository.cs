using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class CafeRepository : ICafeRepository
    {
        private readonly BoardVerseDbContext _context;

        public CafeRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public async Task<Cafe?> GetByIdAsync(Guid id)
        {
            return await _context.Cafes
                .Include(c => c.StaffMembers)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<Cafe?> GetActiveByIdAsync(Guid id)
        {
            return await _context.Cafes
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == id &&
                    c.IsActive &&
                    (c.PartnerOperationalStatus == null ||
                     c.PartnerOperationalStatus == Core.Enum.CafePartnerOperationalStatus.Active));
        }

        public async Task<Cafe?> GetByIdWithInventoriesAsync(Guid id)
        {
            return await _context.Cafes
                .Include(c => c.Inventories)
                    .ThenInclude(i => i.Boxes)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<List<Cafe>> GetNearbyCafesAsync(Guid excludeCafeId, double radiusKm = 10)
        {
            return await _context.Cafes
                .AsNoTracking()
                .Where(c => c.Id != excludeCafeId && c.IsActive && c.Location != null)
                .OrderBy(c => c.Location.Distance(_context.Cafes.First(x => x.Id == excludeCafeId).Location))
                .Take(5)
                .ToListAsync();
        }

        public async Task<User?> GetUserByIdAsync(Guid userId)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null)
        {
            var query = _context.Users.Where(u => u.Username == username);
            if (excludedUserId.HasValue)
            {
                query = query.Where(u => u.Id != excludedUserId.Value);
            }

            return await query.AnyAsync();
        }

        public Task AddCafeStaffAsync(CafeStaff cafeStaff)
        {
            _context.CafeStaffs.Add(cafeStaff);
            return Task.CompletedTask;
        }

        public Task AddUserAsync(User user)
        {
            _context.Users.Add(user);
            return Task.CompletedTask;
        }

        public async Task<bool> IsStaffMemberExistsAsync(Guid cafeId, Guid userId)
        {
            return await _context.CafeStaffs
                .AnyAsync(cs => cs.CafeId == cafeId && cs.UserId == userId);
        }

        public async Task<int> CountActiveStaffAssignmentsAsync(Guid userId)
        {
            return await _context.CafeStaffs
                .CountAsync(cs => cs.UserId == userId && cs.User.IsActive);
        }

        public async Task<PaginatedResponse<StaffDto>> GetStaffPagedAsync(Guid cafeId, PaginationParams paginationParams)
        {
            var query = _context.CafeStaffs
                .Include(cs => cs.User)
                .Where(cs => cs.CafeId == cafeId && cs.User.IsActive)
                .Select(cs => new StaffDto
                {
                    UserId = cs.UserId,
                    Email = cs.User.Email,
                    Username = cs.User.Username,
                    JoinedAt = cs.JoinedAt
                });

            var totalItems = await query.CountAsync();
            var items = await query
                .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
                .Take(paginationParams.PageSize)
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalItems / (double)paginationParams.PageSize);

            return new PaginatedResponse<StaffDto>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = paginationParams.PageNumber,
                    PageSize = paginationParams.PageSize,
                    TotalItems = totalItems,
                    TotalPages = totalPages
                }
            };
        }

        public async Task<CafeStaff?> GetCafeStaffAsync(Guid cafeId, Guid staffId)
        {
            return await _context.CafeStaffs
                .Include(cs => cs.Cafe)
                .FirstOrDefaultAsync(cs => cs.CafeId == cafeId && cs.UserId == staffId);
        }

        public Task RemoveCafeStaffAsync(CafeStaff cafeStaff)
        {
            _context.CafeStaffs.Remove(cafeStaff);
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<Cafe>> GetCafesByManagerIdAsync(Guid managerId)
        {
            return await _context.Cafes
                .AsNoTracking()
                .Where(c => c.ManagerId == managerId && c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<IEnumerable<Cafe>> GetCafesByStaffIdAsync(Guid staffId)
        {
            return await _context.CafeStaffs
                .Include(cs => cs.Cafe)
                .Where(cs => cs.UserId == staffId && cs.User.IsActive)
                .Select(cs => cs.Cafe)
                .Where(c => c.IsActive)
                .ToListAsync();
        }

        public async Task<PaginatedResponse<NearbyCafeDto>> GetNearbyAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid? gameTemplateId,
            PaginationParams paginationParams)
        {
            var origin = GeoLocationHelper.ToPoint(latitude, longitude);
            var radiusMeters = radiusKm * 1000;

            var baseQuery = _context.Cafes
                .AsNoTracking()
                .Where(c => c.IsActive
                    && c.PartnerOperationalStatus == CafePartnerOperationalStatus.Active
                    && c.Location != null
                    && c.Location.IsWithinDistance(origin, radiusMeters));

            if (gameTemplateId.HasValue)
            {
                var gameId = gameTemplateId.Value;
                baseQuery = baseQuery.Where(c =>
                    _context.CafeInventoryBoxes.Any(b =>
                        b.CafeGameInventory.CafeId == c.Id
                        && b.IsActive
                        && b.CafeGameInventory.IsActive
                        && b.CafeGameInventory.GameTemplateId == gameId
                        && (b.Status == CafeGameInventoryStatus.Available
                            || b.Status == CafeGameInventoryStatus.InUse)));
            }

            var projected = baseQuery.Select(c => new NearbyCafeDto
            {
                Id = c.Id,
                Name = c.Name,
                Address = c.Address,
                Latitude = c.Latitude,
                Longitude = c.Longitude,
                PhoneNumber = c.PhoneNumber,
                Description = c.Description,
                CreatedAt = c.CreatedAt,
                DistanceMeters = c.Location!.Distance(origin),
                AvailableGameCount = gameTemplateId.HasValue
                    ? _context.CafeInventoryBoxes.Count(b =>
                        b.CafeGameInventory.CafeId == c.Id
                        && b.IsActive
                        && b.CafeGameInventory.IsActive
                        && b.CafeGameInventory.GameTemplateId == gameTemplateId.Value
                        && b.Status == CafeGameInventoryStatus.Available)
                    : _context.CafeInventoryBoxes.Count(b =>
                        b.CafeGameInventory.CafeId == c.Id
                        && b.IsActive
                        && b.Status == CafeGameInventoryStatus.Available),
                TotalGameBoxCount = gameTemplateId.HasValue
                    ? _context.CafeInventoryBoxes.Count(b =>
                        b.CafeGameInventory.CafeId == c.Id
                        && b.IsActive
                        && b.CafeGameInventory.IsActive
                        && b.CafeGameInventory.GameTemplateId == gameTemplateId.Value
                        && (b.Status == CafeGameInventoryStatus.Available
                            || b.Status == CafeGameInventoryStatus.InUse))
                    : _context.CafeInventoryBoxes.Count(b =>
                        b.CafeGameInventory.CafeId == c.Id
                        && b.IsActive),
                AvailableTableCount = _context.CafeTables.Count(t =>
                    t.CafeId == c.Id
                    && t.IsActive
                    && t.Status == CafeTableStatus.Available),
                TotalTableCount = _context.CafeTables.Count(t =>
                    t.CafeId == c.Id
                    && t.IsActive)
            });

            var totalItems = await projected.CountAsync();
            var items = await projected
                .OrderBy(c => c.DistanceMeters)
                .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
                .Take(paginationParams.PageSize)
                .ToListAsync();

            var totalPages = totalItems == 0
                ? 0
                : (int)Math.Ceiling(totalItems / (double)paginationParams.PageSize);

            return new PaginatedResponse<NearbyCafeDto>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = paginationParams.PageNumber,
                    PageSize = paginationParams.PageSize,
                    TotalItems = totalItems,
                    TotalPages = totalPages
                }
            };
        }

        public async Task EnrichNearbyWithGameWaitAsync(IList<NearbyCafeDto> cafes, Guid gameTemplateId)
        {
            if (cafes.Count == 0)
            {
                return;
            }

            var cafeIds = cafes.Select(c => c.Id).ToList();
            var utcNow = DateTime.UtcNow;

            var playTime = await _context.GameTemplates
                .AsNoTracking()
                .Where(g => g.Id == gameTemplateId && g.IsActive)
                .Select(g => (int?)g.PlayTime)
                .FirstOrDefaultAsync();

            if (!playTime.HasValue)
            {
                return;
            }

            var boxes = await _context.CafeInventoryBoxes
                .AsNoTracking()
                .Where(b =>
                    cafeIds.Contains(b.CafeGameInventory.CafeId)
                    && b.IsActive
                    && b.CafeGameInventory.IsActive
                    && b.CafeGameInventory.GameTemplateId == gameTemplateId
                    && (b.Status == CafeGameInventoryStatus.Available
                        || b.Status == CafeGameInventoryStatus.InUse))
                .Select(b => new
                {
                    b.Id,
                    b.CafeGameInventory.CafeId,
                    b.Status
                })
                .ToListAsync();

            var inUseBoxIds = boxes
                .Where(b => b.Status == CafeGameInventoryStatus.InUse)
                .Select(b => b.Id)
                .ToList();

            var sessionStarts = inUseBoxIds.Count == 0
                ? []
                : await _context.ActiveSessions
                    .AsNoTracking()
                    .Where(s =>
                        s.Status != GroupSessionStatus.Paid
                        && s.CafeInventoryBoxId.HasValue
                        && inUseBoxIds.Contains(s.CafeInventoryBoxId.Value))
                    .Select(s => new { BoxId = s.CafeInventoryBoxId!.Value, s.StartedAt })
                    .ToListAsync();

            var sessionStartByBoxId = sessionStarts.ToDictionary(s => s.BoxId, s => s.StartedAt);

            foreach (var cafe in cafes)
            {
                var cafeBoxes = boxes.Where(b => b.CafeId == cafe.Id).ToList();
                var availableCount = cafeBoxes.Count(b => b.Status == CafeGameInventoryStatus.Available);

                cafe.AvailableGameCount = availableCount;
                cafe.TotalGameBoxCount = cafeBoxes.Count;

                if (availableCount > 0)
                {
                    cafe.SelectedGameAvailabilityStatus = NearbyCafeGameAvailabilityStatus.GameAvailable;
                    cafe.EstimatedWaitMinutes = null;
                    continue;
                }

                cafe.SelectedGameAvailabilityStatus = NearbyCafeGameAvailabilityStatus.WaitingForGame;

                var waitCandidates = cafeBoxes
                    .Where(b => b.Status == CafeGameInventoryStatus.InUse)
                    .Select(b =>
                    {
                        if (sessionStartByBoxId.TryGetValue(b.Id, out var startedAt))
                        {
                            var elapsedMinutes = (utcNow - startedAt).TotalMinutes;
                            return (int)Math.Max(0, Math.Ceiling(playTime.Value - elapsedMinutes));
                        }

                        return playTime.Value;
                    })
                    .ToList();

                cafe.EstimatedWaitMinutes = waitCandidates.Count == 0
                    ? playTime.Value
                    : waitCandidates.Min();
            }
        }

        public async Task<IReadOnlyList<NearbyAlternativeGameSuggestionDto>> GetAlternativeGameSuggestionsAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid gameTemplateId,
            int limit = 10)
        {
            var origin = GeoLocationHelper.ToPoint(latitude, longitude);
            var radiusMeters = radiusKm * 1000;

            var categoryIds = await _context.GameTemplateCategories
                .AsNoTracking()
                .Where(gtc => gtc.GameTemplateId == gameTemplateId)
                .Select(gtc => gtc.CategoryId)
                .ToListAsync();

            if (categoryIds.Count == 0)
            {
                return [];
            }

            var availabilityRows = await _context.CafeInventoryBoxes
                .AsNoTracking()
                .Where(b =>
                    b.IsActive
                    && b.Status == CafeGameInventoryStatus.Available
                    && b.CafeGameInventory.IsActive
                    && b.CafeGameInventory.GameTemplateId != gameTemplateId
                    && b.CafeGameInventory.GameTemplate.IsActive
                    && b.CafeGameInventory.GameTemplate.Categories.Any(c => categoryIds.Contains(c.CategoryId))
                    && b.CafeGameInventory.Cafe.IsActive
                    && b.CafeGameInventory.Cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Active
                    && b.CafeGameInventory.Cafe.Location != null
                    && b.CafeGameInventory.Cafe.Location.IsWithinDistance(origin, radiusMeters))
                .Select(b => new
                {
                    GameTemplateId = b.CafeGameInventory.GameTemplateId,
                    GameName = b.CafeGameInventory.GameTemplate.Name,
                    ThumbnailUrl = b.CafeGameInventory.GameTemplate.ThumbnailUrl,
                    MinPlayers = b.CafeGameInventory.GameTemplate.MinPlayers,
                    MaxPlayers = b.CafeGameInventory.GameTemplate.MaxPlayers,
                    CafeId = b.CafeGameInventory.CafeId,
                    DistanceMeters = b.CafeGameInventory.Cafe.Location!.Distance(origin)
                })
                .ToListAsync();

            if (availabilityRows.Count == 0)
            {
                return [];
            }

            var gameIds = availabilityRows.Select(r => r.GameTemplateId).Distinct().ToList();

            var sharedCategoriesByGame = await _context.GameTemplateCategories
                .AsNoTracking()
                .Where(gtc =>
                    gameIds.Contains(gtc.GameTemplateId)
                    && categoryIds.Contains(gtc.CategoryId))
                .Select(gtc => new
                {
                    gtc.GameTemplateId,
                    Category = new CategoryDto
                    {
                        Id = gtc.Category.Id,
                        Name = gtc.Category.Name,
                        Slug = gtc.Category.Slug,
                        Description = gtc.Category.Description,
                        SortOrder = gtc.Category.SortOrder
                    }
                })
                .ToListAsync();

            var categoriesLookup = sharedCategoriesByGame
                .GroupBy(x => x.GameTemplateId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Category)
                        .OrderBy(c => c.SortOrder)
                        .ToList());

            return availabilityRows
                .GroupBy(r => r.GameTemplateId)
                .Select(g =>
                {
                    var first = g.First();
                    return new NearbyAlternativeGameSuggestionDto
                    {
                        GameTemplateId = g.Key,
                        GameName = first.GameName,
                        ThumbnailUrl = first.ThumbnailUrl,
                        MinPlayers = first.MinPlayers,
                        MaxPlayers = first.MaxPlayers,
                        NearbyCafeCount = g.Select(x => x.CafeId).Distinct().Count(),
                        NearestCafeDistanceMeters = g.Min(x => x.DistanceMeters),
                        AvailableBoxCount = g.Count(),
                        SharedCategories = categoriesLookup.GetValueOrDefault(g.Key, [])
                    };
                })
                .OrderBy(s => s.NearestCafeDistanceMeters)
                .ThenByDescending(s => s.AvailableBoxCount)
                .Take(limit)
                .ToList();
        }

        public async Task<Cafe?> GetPartnerCafeByManagerIdAsync(Guid managerUserId)
        {
            return await _context.Cafes
                .Include(c => c.PartnerApplication)
                .Include(c => c.Tables.Where(t => t.IsActive))
                .FirstOrDefaultAsync(c =>
                    c.ManagerId == managerUserId &&
                    c.PartnerOperationalStatus != null);
        }

        public async Task SyncCafeTablesAsync(Guid cafeId, IReadOnlyList<string> tableNames)
        {
            var existingTables = await _context.CafeTables
                .Where(t => t.CafeId == cafeId)
                .ToListAsync();

            var loadedIds = existingTables.Select(t => t.Id).ToHashSet();
            CafeTableSyncHelper.ApplySync(cafeId, tableNames, existingTables);

            foreach (var table in existingTables.Where(t => !loadedIds.Contains(t.Id)))
            {
                _context.CafeTables.Add(table);
            }

            await _context.SaveChangesAsync();
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
