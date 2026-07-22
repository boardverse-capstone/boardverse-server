using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class LobbyRepository : ILobbyRepository
    {
        private readonly BoardVerseDbContext _db;

        public LobbyRepository(BoardVerseDbContext db)
        {
            _db = db;
        }

        public async Task<Lobby?> GetByIdAsync(Guid lobbyId)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .Include(l => l.Cafe)
                .Include(l => l.Booking)
                .FirstOrDefaultAsync(l => l.Id == lobbyId);
        }

        public async Task<Lobby?> GetByActiveSessionIdAsync(Guid activeSessionId)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(l => l.ActiveSessionId == activeSessionId);
        }

        public async Task<Lobby?> GetByIdWithMembersAsync(Guid lobbyId)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                .Include(l => l.GameTemplate)
                .FirstOrDefaultAsync(l => l.Id == lobbyId);
        }

        public async Task<Lobby?> GetByShareCodeAsync(string shareCode)
        {
            if (string.IsNullOrWhiteSpace(shareCode))
                return null;

            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                .Include(l => l.GameTemplate)
                .FirstOrDefaultAsync(l => l.ShareCode == shareCode.ToUpperInvariant());
        }

        public async Task<IReadOnlyList<Lobby>> GetActiveLobbiesForGameAsync(Guid gameTemplateId, Guid? excludeLobbyId)
        {
            var query = _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Where(l => l.GameTemplateId == gameTemplateId && l.Status == LobbyStatus.Open);

            if (excludeLobbyId.HasValue)
            {
                query = query.Where(l => l.Id != excludeLobbyId.Value);
            }

            return await query.ToListAsync();
        }

        /// <summary>
        /// Lấy các lobby public đang mở (IsPrivate=false, Status=Open) để bất kỳ player nào cũng có thể xem/join.
        /// Hỗ trợ filter optional theo game và khu vực địa lý (bounding-box pre-filter).
        /// Service sẽ áp dụng Haversine chính xác + sort theo khoảng cách.
        /// </summary>
        public async Task<IReadOnlyList<Lobby>> GetDiscoverablePublicLobbiesAsync(
            Guid? gameTemplateId,
            double? latitude,
            double? longitude,
            double? radiusKm,
            int limit)
        {
            var query = _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .Include(l => l.HostUser)
                    .ThenInclude(u => u.Profile)
                .Where(l => !l.IsPrivate && l.Status == LobbyStatus.Open);

            if (gameTemplateId.HasValue)
            {
                query = query.Where(l => l.GameTemplateId == gameTemplateId.Value);
            }

            // Bounding-box pre-filter khi filter địa lý (giảm IO trước khi Haversine)
            if (latitude.HasValue && longitude.HasValue && radiusKm.HasValue && radiusKm.Value > 0)
            {
                var latRad = latitude.Value * Math.PI / 180.0;
                var latDelta = radiusKm.Value / 6371.0 * 180.0 / Math.PI;
                var cosLat = Math.Max(0.0001, Math.Abs(Math.Cos(latRad)));
                var lonDelta = radiusKm.Value / (6371.0 * cosLat) * 180.0 / Math.PI;

                var minLat = Math.Max(-90, latitude.Value - latDelta);
                var maxLat = Math.Min(90, latitude.Value + latDelta);
                var minLon = longitude.Value - lonDelta;
                var maxLon = longitude.Value + lonDelta;

                query = query.Where(l => l.Latitude.HasValue && l.Longitude.HasValue
                    && l.Latitude >= minLat && l.Latitude <= maxLat
                    && l.Longitude >= minLon && l.Longitude <= maxLon);
            }
            else
            {
                // Không filter geo: chỉ lấy lobby có toạ độ (nếu có) để không thiếu
                // Khi sort theo ngày tạo
            }

            return await query
                .OrderByDescending(l => l.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// BR-10: Search lobbies by game, geo proximity, and karma filter.
        /// Uses Haversine distance formula for accurate radius filtering.
        /// LOBBY-P0-FIX-9: Clamp at high latitudes to avoid NaN.
        /// </summary>
        public async Task<IReadOnlyList<Lobby>> SearchLobbiesNearbyAsync(
            Guid gameTemplateId,
            double latitude,
            double longitude,
            double radiusKm,
            int? minKarmaScore)
        {
            // LOBBY-P0-FIX-9: Clamp at high latitudes where cos(lat) → 0
            var latRad = latitude * Math.PI / 180.0;
            var lonRad = longitude * Math.PI / 180.0;

            // Bounding box pre-filter: clamp cos(lat) để tránh NaN/inf ở vĩ độ ±90
            var latDelta = radiusKm / 6371.0 * 180.0 / Math.PI;
            var cosLat = Math.Max(0.0001, Math.Abs(Math.Cos(latRad))); // floor at 0.0001 rad ~ 0.006°
            var lonDelta = radiusKm / (6371.0 * cosLat) * 180.0 / Math.PI;

            var minLat = Math.Max(-90, latitude - latDelta);
            var maxLat = Math.Min(90, latitude + latDelta);
            var minLon = longitude - lonDelta;
            var maxLon = longitude + lonDelta;

            var lobbies = await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .Where(l => l.GameTemplateId == gameTemplateId && l.Status == LobbyStatus.Open)
                .Where(l => l.Latitude.HasValue && l.Longitude.HasValue)
                .Where(l => l.Latitude >= minLat && l.Latitude <= maxLat
                    && l.Longitude >= minLon && l.Longitude <= maxLon)
                .ToListAsync();

            // Precise distance filter using Haversine
            var earthRadiusKm = 6371.0;
            lobbies = lobbies
                .Where(l =>
                {
                    var lLat = l.Latitude!.Value;
                    var lLng = l.Longitude!.Value;

                    var dLat = (lLat - latitude) * Math.PI / 180.0;
                    var dLon = (lLng - longitude) * Math.PI / 180.0;
                    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                        + Math.Cos(latRad) * Math.Cos(lLat * Math.PI / 180.0)
                           * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

                    // Clamp để tránh floating point tạo a > 1
                    a = Math.Min(1.0, Math.Max(0.0, a));
                    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
                    var distance = earthRadiusKm * c;
                    return distance <= radiusKm;
                })
                .ToList();

            if (minKarmaScore.HasValue)
            {
                lobbies = lobbies
                    .Where(l => l.Members.All(m => (m.User.Profile?.KarmaPoints ?? 100) >= minKarmaScore.Value))
                    .ToList();
            }

            return lobbies;
        }

        public async Task<IReadOnlyList<Lobby>> GetLobbiesByHostAsync(Guid hostUserId)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                .Include(l => l.GameTemplate)
                .Where(l => l.HostUserId == hostUserId)
                .OrderByDescending(l => l.CreatedAt)
                .Take(50)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Lobby>> GetJoinedLobbiesAsync(Guid userId)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                .Include(l => l.GameTemplate)
                .Where(l => l.Members.Any(m => m.UserId == userId && m.IsActive)
                    && (l.Status == LobbyStatus.Open || l.Status == LobbyStatus.Full
                        || l.Status == LobbyStatus.InProgress || l.Status == LobbyStatus.RatingOpen))
                .OrderByDescending(l => l.ScheduledStartTime ?? l.CreatedAt)
                .Take(50)
                .ToListAsync();
        }

        public async Task<BookingDeposit?> GetBookingByIdAsync(Guid bookingId)
        {
            return await _db.BookingDeposits.FirstOrDefaultAsync(b => b.Id == bookingId);
        }

        public Task AddAsync(Lobby lobby)
        {
            _db.Lobbies.Add(lobby);
            return Task.CompletedTask;
        }

        public Task AddMemberAsync(LobbyMember member)
        {
            _db.LobbyMembers.Add(member);
            return Task.CompletedTask;
        }

        public Task AddReportAsync(LobbyReport report)
        {
            _db.LobbyReports.Add(report);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }
    }
}