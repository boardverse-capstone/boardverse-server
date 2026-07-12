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
                .FirstOrDefaultAsync(l => l.Id == lobbyId);
        }

        public async Task<Lobby?> GetByActiveSessionIdAsync(Guid activeSessionId)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
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
        /// BR-10: Search lobbies by game, geo proximity, and karma filter.
        /// Uses Haversine distance formula for accurate radius filtering.
        /// </summary>
        public async Task<IReadOnlyList<Lobby>> SearchLobbiesNearbyAsync(
            Guid gameTemplateId,
            double latitude,
            double longitude,
            double radiusKm,
            int? minKarmaScore)
        {
            // Haversine formula for distance calculation
            // distance(km) = 6371 * acos(sin(lat1)*sin(lat2) + cos(lat1)*cos(lat2)*cos(deltaLon))
            var latRad = latitude * Math.PI / 180.0;
            var lonRad = longitude * Math.PI / 180.0;

            // Using a bounding box pre-filter for efficiency, then precise distance check
            var latDelta = radiusKm / 6371.0 * 180.0 / Math.PI;
            var lonDelta = radiusKm / (6371.0 * Math.Cos(latRad)) * 180.0 / Math.PI;

            var minLat = latitude - latDelta;
            var maxLat = latitude + latDelta;
            var minLon = longitude - lonDelta;
            var maxLon = longitude + lonDelta;

            // Use raw SQL for distance calculation since EF Core doesn't support Haversine natively
            var lobbies = await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .Where(l => l.GameTemplateId == gameTemplateId && l.Status == LobbyStatus.Open)
                .Where(l => l.Latitude.HasValue && l.Longitude.HasValue)
                .Where(l => l.Latitude >= minLat && l.Latitude <= maxLat && l.Longitude >= minLon && l.Longitude <= maxLon)
                .ToListAsync();

            // Precise distance filter using Haversine
            var earthRadiusKm = 6371.0;
            lobbies = lobbies
                .Where(l =>
                {
                    var dLat = (l.Latitude!.Value - latitude) * Math.PI / 180.0;
                    var dLon = (l.Longitude!.Value - longitude) * Math.PI / 180.0;
                    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                        + Math.Cos(latRad) * Math.Cos(l.Latitude!.Value * Math.PI / 180.0)
                           * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
                    var distance = earthRadiusKm * c;
                    return distance <= radiusKm;
                })
                .ToList();

            // BR-10: Filter by karma (no Elo filter)
            if (minKarmaScore.HasValue)
            {
                lobbies = lobbies
                    .Where(l => l.Members.All(m => (m.User.Profile?.KarmaPoints ?? 100) >= minKarmaScore.Value))
                    .ToList();
            }

            return lobbies;
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

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }
    }
}
