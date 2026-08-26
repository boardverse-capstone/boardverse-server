using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.DTOs.Pos;
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

        public async Task<Cafe?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .Include(c => c.StaffMembers)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }

        /// <summary>
        /// Lấy Cafe kèm Manager navigation (FK User) — dùng cho AdminCafeController.GET /api/admin/cafes/{id}
        /// cần render ManagerName/ManagerEmail. Include Manager tránh NullRef khi map DTO.
        /// </summary>
        public async Task<Cafe?> GetByIdWithManagerAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .Include(c => c.Manager)
                .Include(c => c.StaffMembers)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }

        public async Task<Cafe?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == id &&
                    c.IsActive &&
                    (c.PartnerOperationalStatus == null ||
                     c.PartnerOperationalStatus == Core.Enum.CafePartnerOperationalStatus.Active), cancellationToken);
        }

        public async Task<Cafe?> GetByIdWithInventoriesAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .Include(c => c.Inventories)
                    .ThenInclude(i => i.Boxes)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }

        public async Task<List<Cafe>> GetNearbyCafesAsync(Guid excludeCafeId, double radiusKm = 10, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .AsNoTracking()
                .Where(c => c.Id != excludeCafeId && c.IsActive && c.Location != null)
                .OrderBy(c => c.Location.Distance(_context.Cafes.First(x => x.Id == excludeCafeId).Location))
                .Take(5)
                .ToListAsync(cancellationToken);
        }

        public async Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        }

        public async Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        }

        public async Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null, CancellationToken cancellationToken = default)
        {
            var query = _context.Users.Where(u => u.Username == username);
            if (excludedUserId.HasValue)
            {
                query = query.Where(u => u.Id != excludedUserId.Value);
            }

            return await query.AnyAsync(cancellationToken);
        }

        public Task AddCafeStaffAsync(CafeStaff cafeStaff, CancellationToken cancellationToken = default)
        {
            _context.CafeStaffs.Add(cafeStaff);
            return Task.CompletedTask;
        }

        public Task AddUserAsync(User user, CancellationToken cancellationToken = default)
        {
            _context.Users.Add(user);
            return Task.CompletedTask;
        }

        public async Task<bool> IsStaffMemberExistsAsync(Guid cafeId, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.CafeStaffs
                .AnyAsync(cs => cs.CafeId == cafeId && cs.UserId == userId, cancellationToken);
        }

        /// <summary>
        /// GAP-C1: Returns true when the user is the cafe's manager OR a staff member.
        /// Used by IDOR guards on booking/receipt endpoints to lock cross-tenant access.
        /// </summary>
        public async Task<bool> IsManagerOrStaffAsync(Guid cafeId, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .AnyAsync(c => c.Id == cafeId && (c.ManagerId == userId
                    || _context.CafeStaffs.Any(cs => cs.CafeId == cafeId && cs.UserId == userId)), cancellationToken);
        }

        public async Task<int> CountActiveStaffAssignmentsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.CafeStaffs
                .CountAsync(cs => cs.UserId == userId && cs.User.IsActive, cancellationToken);
        }

        public async Task<PaginatedResponse<StaffDto>> GetStaffPagedAsync(Guid cafeId, PaginationParams paginationParams, CancellationToken cancellationToken = default)
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

            var totalItems = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
                .Take(paginationParams.PageSize)
                .ToListAsync(cancellationToken);

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

        public async Task<CafeStaff?> GetCafeStaffAsync(Guid cafeId, Guid staffId, CancellationToken cancellationToken = default)
        {
            return await _context.CafeStaffs
                .Include(cs => cs.Cafe)
                .FirstOrDefaultAsync(cs => cs.CafeId == cafeId && cs.UserId == staffId, cancellationToken);
        }

        public Task RemoveCafeStaffAsync(CafeStaff cafeStaff, CancellationToken cancellationToken = default)
        {
            _context.CafeStaffs.Remove(cafeStaff);
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<Cafe>> GetCafesByManagerIdAsync(Guid managerId, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .AsNoTracking()
                .Where(c => c.ManagerId == managerId && c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<IEnumerable<Cafe>> GetCafesByStaffIdAsync(Guid staffId, CancellationToken cancellationToken = default)
        {
            return await _context.CafeStaffs
                .Include(cs => cs.Cafe)
                .Where(cs => cs.UserId == staffId && cs.User.IsActive)
                .Select(cs => cs.Cafe)
                .Where(c => c.IsActive)
                .ToListAsync(cancellationToken);
        }

        public async Task<PaginatedResponse<NearbyCafeDto>> GetNearbyAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid? gameTemplateId,
            string? name,
            PaginationParams paginationParams,
            CancellationToken cancellationToken = default)
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

            if (!string.IsNullOrWhiteSpace(name))
            {
                var term = name.Trim().ToLower();
                baseQuery = baseQuery.Where(c =>
                    c.Name.ToLower().Contains(term));
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
                TotalSeats = c.TotalSeats,
                BillingModel = CafePartnerStatusMapper.ToApiBillingModel(c.BillingModel),
                BasePrice = c.BasePrice,
                TieredBlockRate = c.TieredBlockRate,
                TieredBlockMinutes = c.TieredBlockMinutes,
                DepositPercentage = c.DepositPercentage,
                IsPricingLocked = c.IsPricingLocked,
                HasSePayConfigured = c.SePayMerchantId != null && c.SePayMerchantId != ""
                                     && c.SePayApiKey != null && c.SePayApiKey != ""
                                     && c.SePaySecretKey != null && c.SePaySecretKey != "",
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

            var totalItems = await projected.CountAsync(cancellationToken);
            var items = await projected
                .OrderBy(c => c.DistanceMeters)
                .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
                .Take(paginationParams.PageSize)
                .ToListAsync(cancellationToken);

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

        public async Task<PaginatedResponse<NearbyCafeDto>> GetAllActiveCafesAsync(
            PaginationParams paginationParams,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Cafes
                .AsNoTracking()
                .Where(c => c.IsActive
                    && c.PartnerOperationalStatus == CafePartnerOperationalStatus.Active);

            var projected = query.Select(c => new NearbyCafeDto
            {
                Id = c.Id,
                Name = c.Name,
                Address = c.Address,
                Latitude = c.Latitude,
                Longitude = c.Longitude,
                PhoneNumber = c.PhoneNumber,
                Description = c.Description,
                CreatedAt = c.CreatedAt,
                DistanceMeters = 0,
                TotalSeats = c.TotalSeats,
                BillingModel = CafePartnerStatusMapper.ToApiBillingModel(c.BillingModel),
                BasePrice = c.BasePrice,
                TieredBlockRate = c.TieredBlockRate,
                TieredBlockMinutes = c.TieredBlockMinutes,
                DepositPercentage = c.DepositPercentage,
                IsPricingLocked = c.IsPricingLocked,
                HasSePayConfigured = c.SePayMerchantId != null && c.SePayMerchantId != ""
                                     && c.SePayApiKey != null && c.SePayApiKey != ""
                                     && c.SePaySecretKey != null && c.SePaySecretKey != "",
                AvailableGameCount = _context.CafeInventoryBoxes.Count(b =>
                    b.CafeGameInventory.CafeId == c.Id
                    && b.IsActive
                    && b.Status == CafeGameInventoryStatus.Available),
                TotalGameBoxCount = _context.CafeInventoryBoxes.Count(b =>
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

            var totalItems = await projected.CountAsync(cancellationToken);
            var items = await projected
                .OrderBy(c => c.Name)
                .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
                .Take(paginationParams.PageSize)
                .ToListAsync(cancellationToken);

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

        public async Task<PaginatedResponse<NearbyCafeDto>> SearchCafesAsync(
            string name,
            double? latitude,
            double? longitude,
            double? radiusKm,
            PaginationParams paginationParams,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Cafes
                .AsNoTracking()
                .Where(c => c.IsActive
                    && c.PartnerOperationalStatus == CafePartnerOperationalStatus.Active);

            var term = name.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term));

            if (latitude.HasValue && longitude.HasValue && radiusKm.HasValue)
            {
                var origin = GeoLocationHelper.ToPoint(latitude.Value, longitude.Value);
                var radiusMeters = radiusKm.Value * 1000;
                query = query.Where(c => c.Location != null && c.Location.IsWithinDistance(origin, radiusMeters));
            }

            var projected = query.Select(c => new NearbyCafeDto
            {
                Id = c.Id,
                Name = c.Name,
                Address = c.Address,
                Latitude = c.Latitude,
                Longitude = c.Longitude,
                PhoneNumber = c.PhoneNumber,
                Description = c.Description,
                CreatedAt = c.CreatedAt,
                DistanceMeters = latitude.HasValue && longitude.HasValue && c.Location != null
                    ? c.Location.Distance(GeoLocationHelper.ToPoint(latitude.Value, longitude.Value))
                    : 0,
                TotalSeats = c.TotalSeats,
                BillingModel = CafePartnerStatusMapper.ToApiBillingModel(c.BillingModel),
                BasePrice = c.BasePrice,
                TieredBlockRate = c.TieredBlockRate,
                TieredBlockMinutes = c.TieredBlockMinutes,
                DepositPercentage = c.DepositPercentage,
                IsPricingLocked = c.IsPricingLocked,
                HasSePayConfigured = c.SePayMerchantId != null && c.SePayMerchantId != ""
                                     && c.SePayApiKey != null && c.SePayApiKey != ""
                                     && c.SePaySecretKey != null && c.SePaySecretKey != "",
                AvailableGameCount = _context.CafeInventoryBoxes.Count(b =>
                    b.CafeGameInventory.CafeId == c.Id
                    && b.IsActive
                    && b.Status == CafeGameInventoryStatus.Available),
                TotalGameBoxCount = _context.CafeInventoryBoxes.Count(b =>
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

            if (latitude.HasValue && longitude.HasValue)
            {
                projected = projected.OrderBy(c => c.DistanceMeters);
            }
            else
            {
                projected = projected.OrderBy(c => c.Name);
            }

            var totalItems = await projected.CountAsync(cancellationToken);
            var items = await projected
                .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
                .Take(paginationParams.PageSize)
                .ToListAsync(cancellationToken);

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

        public async Task EnrichNearbyWithGameWaitAsync(IList<NearbyCafeDto> cafes, Guid gameTemplateId, CancellationToken cancellationToken = default)
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
                .FirstOrDefaultAsync(cancellationToken);

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
                .ToListAsync(cancellationToken);

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
                    .ToListAsync(cancellationToken);

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
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            var origin = GeoLocationHelper.ToPoint(latitude, longitude);
            var radiusMeters = radiusKm * 1000;

            var categoryIds = await _context.GameTemplateCategories
                .AsNoTracking()
                .Where(gtc => gtc.GameTemplateId == gameTemplateId)
                .Select(gtc => gtc.CategoryId)
                .ToListAsync(cancellationToken);

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
                .ToListAsync(cancellationToken);

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
                .ToListAsync(cancellationToken);

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

        public async Task<Cafe?> GetPartnerCafeByManagerIdAsync(Guid managerUserId, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .Include(c => c.PartnerApplication)
                .Include(c => c.Tables.Where(t => t.IsActive))
                .Include(c => c.Inventories.Where(i => i.IsActive))
                .FirstOrDefaultAsync(c =>
                    c.ManagerId == managerUserId &&
                    c.PartnerOperationalStatus != null, cancellationToken);
        }

        public async Task SyncCafeTablesAsync(Guid cafeId, IReadOnlyList<string> tableNames, CancellationToken cancellationToken = default)
        {
            var existingTables = await _context.CafeTables
                .Where(t => t.CafeId == cafeId)
                .ToListAsync(cancellationToken);

            var loadedIds = existingTables.Select(t => t.Id).ToHashSet();
            CafeTableSyncHelper.ApplySync(cafeId, tableNames, existingTables);

            foreach (var table in existingTables.Where(t => !loadedIds.Contains(t.Id)))
            {
                _context.CafeTables.Add(table);
            }

            await RefreshTableLayoutJsonAsync(cafeId, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task SyncCafeTablesAsync(Guid cafeId, IReadOnlyList<CafeTableSyncItem> tables, CancellationToken cancellationToken = default)
        {
            var existingTables = await _context.CafeTables
                .Where(t => t.CafeId == cafeId)
                .ToListAsync(cancellationToken);

            var loadedIds = existingTables.Select(t => t.Id).ToHashSet();
            CafeTableSyncHelper.ApplySync(cafeId, tables, existingTables);

            foreach (var table in existingTables.Where(t => !loadedIds.Contains(t.Id)))
            {
                _context.CafeTables.Add(table);
            }

            await RefreshTableLayoutJsonAsync(cafeId, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task RefreshTableLayoutJsonAsync(Guid cafeId, CancellationToken cancellationToken = default)
        {
            var cafe = await _context.Cafes.FirstOrDefaultAsync(c => c.Id == cafeId, cancellationToken);
            if (cafe == null)
            {
                return;
            }

            var activeNames = await _context.CafeTables
                .Where(t => t.CafeId == cafeId && t.IsActive)
                .OrderBy(t => t.SortOrder)
                .Select(t => t.Name)
                .ToListAsync(cancellationToken);

            cafe.TableLayoutJson = System.Text.Json.JsonSerializer.Serialize(activeNames);
            cafe.UpdatedAt = DateTime.UtcNow;
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        // === Admin: Full CRUD ===

        public async Task AddCafeAsync(Cafe cafe, CancellationToken cancellationToken = default)
        {
            _context.Cafes.Add(cafe);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<(IReadOnlyList<Cafe> Items, int TotalCount)> GetAdminListAsync(
            int page, int pageSize, string? searchTerm, bool? isActive, Guid? managerId,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Cafes.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(c =>
                    c.Name.ToLower().Contains(term) ||
                    (c.Address != null && c.Address.ToLower().Contains(term)));
            }

            if (isActive.HasValue)
            {
                query = query.Where(c => c.IsActive == isActive.Value);
            }

            if (managerId.HasValue)
            {
                query = query.Where(c => c.ManagerId == managerId.Value);
            }

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Include(c => c.Manager)
                .Include(c => c.StaffMembers)
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<Cafe?> GetAdminDetailAsync(Guid cafeId, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .Include(c => c.StaffMembers)
                    .ThenInclude(s => s.User)
                .Include(c => c.Inventories)
                .FirstOrDefaultAsync(c => c.Id == cafeId, cancellationToken);
        }

        public async Task<int> CountAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Cafes.CountAsync(cancellationToken);
        }

        public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Cafes.CountAsync(c => c.IsActive, cancellationToken);
        }

        // === Cafe Detail (extended public info) ===

        public async Task<Cafe?> GetCafeDetailAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Cafes
                .AsNoTracking()
                .Include(c => c.StaffMembers)
                .Include(c => c.Inventories)
                .Include(c => c.Tables)
                .FirstOrDefaultAsync(c =>
                    c.Id == id &&
                    c.IsActive &&
                    (c.PartnerOperationalStatus == null ||
                     c.PartnerOperationalStatus == CafePartnerOperationalStatus.Active), cancellationToken);
        }

        public async Task<Dictionary<TimeSlot, int>> GetAvailableSeatsByTimeSlotAsync(Guid cafeId, DateOnly playDate, CancellationToken cancellationToken = default)
        {
            // BR-NEW-15 (2026-08-18): GetAvailableSeatsByTimeSlotAsync đang trong quá trình refactor.
            // SeatInventory.TimeSlot đã được thay bằng ScheduledStartTime/ScheduledEndTime.
            // Tạm thời trả về total seats - held - inUse (không phân biệt slot).
            // TODO Phase 2: Cập nhật trả về Dictionary<TimeOnly, int> dựa trên ScheduledStartTime ranges.
            var result = new Dictionary<TimeSlot, int>();
            var totalSeats = await _context.Cafes
                .AsNoTracking()
                .Where(c => c.Id == cafeId)
                .Select(c => c.TotalSeats)
                .FirstOrDefaultAsync(cancellationToken);

            foreach (TimeSlot slot in Enum.GetValues<TimeSlot>())
            {
                var inventory = await _context.SeatInventories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s =>
                        s.CafeId == cafeId &&
                        s.PlayDate == playDate &&
                        s.ScheduledStartTime == slot.GetStartTime() &&
                        s.ScheduledEndTime == slot.GetEndTime(), cancellationToken);

                if (inventory != null)
                {
                    result[slot] = inventory.AvailableSeats;
                }
                else
                {
                    var heldSeats = await CountHeldSeatsForSlotAsync(cafeId, playDate, slot, cancellationToken);
                    var inUseSeats = await CountInUseSeatsForSlotAsync(cafeId, playDate, slot, cancellationToken);
                    result[slot] = Math.Max(0, totalSeats - heldSeats - inUseSeats);
                }
            }

            return result;
        }

        public async Task<List<CafeScheduleOverride>> GetScheduleOverridesAsync(Guid cafeId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
        {
            var query = _context.CafeScheduleOverrides
                .AsNoTracking()
                .Where(o => o.CafeId == cafeId);

            if (fromDate.HasValue)
            {
                query = query.Where(o => o.ApplyDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(o => o.ApplyDate <= toDate.Value);
            }

            return await query
                .OrderBy(o => o.ApplyDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> CountHeldSeatsAsync(Guid cafeId, DateOnly playDate, CancellationToken cancellationToken = default)
        {
            // Đếm tổng MaxPlayers của các reservation đang ở trạng thái holding/confirmed
            // (chưa check-in, chưa expired/cancelled)
            return await _context.Reservations
                .AsNoTracking()
                .Where(r =>
                    r.CafeId == cafeId &&
                    r.PlayDate == playDate &&
                    (r.Status == ReservationStatus.Holding ||
                     r.Status == ReservationStatus.Confirmed ||
                     r.Status == ReservationStatus.AwaitingDeposit))
                .SumAsync(r => (int?)r.MaxPlayers, cancellationToken) ?? 0;
        }

        public async Task<int> CountInUseSeatsAsync(Guid cafeId, DateOnly playDate, CancellationToken cancellationToken = default)
        {
            // Đếm tổng số members đang active trong các session
            // ActiveSession: đã check-in, chưa paid
            var startOfDay = playDate.ToDateTime(TimeOnly.MinValue);
            var endOfDay = playDate.ToDateTime(TimeOnly.MaxValue);

            return await _context.ActiveSessions
                .AsNoTracking()
                .Where(s =>
                    s.CafeId == cafeId &&
                    s.Status != GroupSessionStatus.Paid &&
                    s.StartedAt >= startOfDay &&
                    s.StartedAt <= endOfDay)
                .SelectMany(s => s.Members)
                .CountAsync(m => m.Status == IndividualSessionStatus.Playing, cancellationToken);
        }

        /// <summary>
        /// Đếm tổng MaxPlayers của các reservation đang giữ ghế cho (cafe, playDate, timeSlot).
        /// Dùng cho fallback khi SeatInventory row chưa được tạo.
        /// BR-NEW-15: Dùng OVERLAP check thay vì exact match để đếm đúng reservation giao nhau với slot.
        ///
        /// Reservation overlap với slot khi:
        ///   r.PreferredStartTime &lt; slotEnd &amp;&amp; r.PreferredEndTime &gt; slotStart
        /// Ví dụ: reservation 14:00-17:00 overlap với Afternoon (12:00-18:00)
        /// </summary>
        private async Task<int> CountHeldSeatsForSlotAsync(Guid cafeId, DateOnly playDate, TimeSlot timeSlot, CancellationToken cancellationToken = default)
        {
            var slotStart = timeSlot.GetStartTime();
            var slotEnd = timeSlot.GetEndTime();

            return await _context.Reservations
                .AsNoTracking()
                .Where(r =>
                    r.CafeId == cafeId &&
                    r.PlayDate == playDate &&
                    // GAP-06 fix: OVERLAP check thay vì exact match
                    r.PreferredStartTime < slotEnd &&
                    r.PreferredEndTime > slotStart &&
                    (r.Status == ReservationStatus.Holding ||
                     r.Status == ReservationStatus.Confirmed ||
                     r.Status == ReservationStatus.AwaitingDeposit))
                .SumAsync(r => (int?)r.MaxPlayers, cancellationToken) ?? 0;
        }

        /// <summary>
        /// Đếm tổng số members đang active trong session cho (cafe, playDate).
        /// ActiveSession không có TimeSlot field → chỉ filter theo ngày.
        /// Dùng cho fallback khi SeatInventory row chưa được tạo.
        /// </summary>
        private async Task<int> CountInUseSeatsForSlotAsync(Guid cafeId, DateOnly playDate, TimeSlot timeSlot, CancellationToken cancellationToken = default)
        {
            var startOfDay = playDate.ToDateTime(TimeOnly.MinValue);
            var endOfDay = playDate.ToDateTime(TimeOnly.MaxValue);

            return await _context.ActiveSessions
                .AsNoTracking()
                .Where(s =>
                    s.CafeId == cafeId &&
                    s.Status != GroupSessionStatus.Paid &&
                    s.StartedAt >= startOfDay &&
                    s.StartedAt <= endOfDay)
                .SelectMany(s => s.Members)
                .CountAsync(m => m.Status == IndividualSessionStatus.Playing, cancellationToken);
        }
    }
}