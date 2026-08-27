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

        public async Task<Lobby?> GetByIdAsync(Guid lobbyId, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .Include(l => l.Cafe)
                .Include(l => l.Booking)
                .Include(l => l.Reservation)
                .FirstOrDefaultAsync(l => l.Id == lobbyId);
        }

        /// <summary>
        /// H4: Lấy lobby + khóa row (SELECT ... FOR UPDATE) — dùng trong transaction JoinLobby
        /// để chống race condition khi nhiều request join đồng thời vượt MaxMembers (BR-07).
        /// Caller phải đang trong một transaction (BeginTransactionAsync đã được gọi).
        /// </summary>
        public async Task<Lobby?> GetByIdForUpdateAsync(Guid lobbyId, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .FromSqlRaw("SELECT * FROM \"Lobbies\" WHERE \"Id\" = {0} FOR UPDATE", lobbyId)
                .Include(l => l.Members)
                .Include(l => l.Reservation)
                .AsSplitQuery()
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// H4: Bắt đầu transaction cho JoinLobby atomic guard.
        /// Pattern copy từ IActiveSessionRepository / ActiveSessionRepository.
        /// </summary>
        public async Task<IDatabaseTransactionContext> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            return new EfTransactionContextAdapter(tx);
        }

        public async Task<Lobby?> GetByActiveSessionIdAsync(Guid activeSessionId, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .FirstOrDefaultAsync(l => l.ActiveSessionId == activeSessionId);
        }

        public async Task<Lobby?> GetByIdWithMembersAsync(Guid lobbyId, CancellationToken cancellationToken = default)
        {
            // AsNoTracking: tránh EF cache trả entity cũ với Members rỗng khi caller
            // (vd CafePosService) vừa load lobby ở transaction khác cùng DbContext.
            return await _db.Lobbies
                .AsNoTracking()
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .FirstOrDefaultAsync(l => l.Id == lobbyId);
        }

        public async Task<Lobby?> GetByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(shareCode))
                return null;

            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .Include(l => l.Cafe)
                .FirstOrDefaultAsync(l => l.ShareCode == shareCode.ToUpperInvariant());
        }

        /// <summary>
        /// Tìm lobby theo ReservationId — dùng để self-heal orphan reservation (R-Bug-029).
        /// </summary>
        public async Task<Lobby?> GetByReservationIdAsync(Guid reservationId, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .FirstOrDefaultAsync(l => l.ReservationId == reservationId);
        }

        public async Task<IReadOnlyList<Lobby>> GetActiveLobbiesForGameAsync(Guid gameTemplateId, Guid? excludeLobbyId, CancellationToken cancellationToken = default)
        {
            var query = _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.Cafe)
                .Where(l => l.GameTemplateId == gameTemplateId && l.Status == LobbyStatus.Open);

            if (excludeLobbyId.HasValue)
            {
                query = query.Where(l => l.Id != excludeLobbyId.Value);
            }

            return await query.ToListAsync();
        }

        /// <summary>
        /// Lấy các lobby public CHƯA BẮT ĐẦU CHƠI để player khám phá và có thể join (BR-10 + BR-LOBBY-READY-02).
        /// Phạm vi status: <c>Open</c>, <c>Viable</c>, <c>Full</c>, <c>WaitingCheckIn</c>.
        /// Lobby private (<c>IsPrivate = true</c>) bị ẩn hoàn toàn.
        /// Lobby đã <c>InProgress</c> trở lên (Closed / RatingOpen / TimeoutFailed / HostCancelled / Dissolved / ...) bị loại vì đang chơi hoặc kết thúc.
        /// Hỗ trợ filter optional theo game và khu vực địa lý (bounding-box pre-filter).
        /// Service sẽ áp dụng Haversine chính xác + sort theo khoảng cách.
        /// </summary>
        public async Task<IReadOnlyList<Lobby>> GetDiscoverablePublicLobbiesAsync(
            Guid? gameTemplateId,
            double? latitude,
            double? longitude,
            double? radiusKm,
            int limit, CancellationToken cancellationToken = default)
        {
            // BR-10 + BR-LOBBY-READY-02 + yêu cầu UX (2026-08-27):
            // Hiển thị lobby public CHƯA VÀO PHIÊN CHƠI — bao gồm Open/Viable/Full/WaitingCheckIn.
            // Trước đây chỉ Open → lobby vừa đủ người biến mất khỏi discoverable, gây UX kém.
            // Khi lobby vào quán (InProgress) mới ẩn khỏi kết quả discovery.
            var prePlayStatuses = new[]
            {
                LobbyStatus.Open,
                LobbyStatus.Viable,
                LobbyStatus.Full,
                LobbyStatus.WaitingCheckIn
            };

            var query = _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .Include(l => l.HostUser)
                    .ThenInclude(u => u.Profile)
                .Include(l => l.Cafe)
                .Where(l => !l.IsPrivate && prePlayStatuses.Contains(l.Status));

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
            int? minKarmaScore, CancellationToken cancellationToken = default)
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
                .Include(l => l.Cafe)
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

        public async Task<IReadOnlyList<Lobby>> GetLobbiesByHostAsync(Guid hostUserId, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .Include(l => l.Cafe)
                .Where(l => l.HostUserId == hostUserId)
                .OrderByDescending(l => l.CreatedAt)
                .Take(50)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Lobby>> GetMyLobbiesAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .Include(l => l.Cafe)
                .Where(l => ActiveLobbyStatuses.Contains(l.Status)
                    && (l.HostUserId == userId
                        || l.Members.Any(m => m.UserId == userId && m.IsActive)))
                .OrderByDescending(l => l.ScheduledStartTime ?? l.CreatedAt)
                .Take(50)
                .ToListAsync();
        }

        // ===== BR-NEW-* mở rộng cho Reservation flow =====

        private static readonly HashSet<LobbyStatus> ActiveLobbyStatuses = new()
        {
            LobbyStatus.PendingActivation,
            LobbyStatus.PendingCafeApproval,
            LobbyStatus.Open,
            LobbyStatus.Viable,
            LobbyStatus.Full,
            LobbyStatus.InProgress
        };

        public async Task<IReadOnlyList<Lobby>> GetActiveLobbiesByHostAsync(Guid hostUserId, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                .Where(l => l.HostUserId == hostUserId && ActiveLobbyStatuses.Contains(l.Status))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Lobby>> GetActiveLobbiesByMemberAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                .Where(l => l.Members.Any(m => m.UserId == userId && m.IsActive)
                    && ActiveLobbyStatuses.Contains(l.Status))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Lobby>> GetActiveLobbiesByCafeDateSlotAsync(
            Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default)
        {
            // BR-NEW-15: Query by TimeOnly range instead of TimeSlot enum.
            return await _db.Lobbies
                .Where(l => l.CafeId == cafeId
                    && l.PlayDate == playDate
                    && l.PreferredStartTime == scheduledStartTime
                    && l.PreferredEndTime == scheduledEndTime
                    && ActiveLobbyStatuses.Contains(l.Status))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Lobby>> GetActiveLobbiesByHostAsync(Guid hostUserId, DateOnly playDate, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .Include(l => l.Members)
                .Where(l => l.HostUserId == hostUserId
                    && l.PlayDate == playDate
                    && ActiveLobbyStatuses.Contains(l.Status))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Lobby>> GetActiveLobbiesByCafeDateSlotAsync(
            Guid userId, Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default)
        {
            // BR-NEW-15: Query by TimeOnly range instead of TimeSlot enum.
            return await _db.Lobbies
                .Include(l => l.Members)
                .Where(l => l.HostUserId == userId
                    && l.CafeId == cafeId
                    && l.PlayDate == playDate
                    && l.PreferredStartTime == scheduledStartTime
                    && l.PreferredEndTime == scheduledEndTime
                    && ActiveLobbyStatuses.Contains(l.Status))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<LobbyMember>> GetMembersAsync(Guid lobbyId, CancellationToken cancellationToken = default)
        {
            return await _db.LobbyMembers
                .Where(m => m.LobbyId == lobbyId)
                .ToListAsync();
        }

        public async Task<bool> IsUserLobbyMemberAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _db.LobbyMembers
                .AsNoTracking()
                .AnyAsync(m => m.LobbyId == lobbyId && m.UserId == userId);
        }

        public async Task<bool> IsUserBookingParticipantAsync(Guid bookingId, Guid userId, CancellationToken cancellationToken = default)
        {
            // Booking has no direct HostUserId — host is determined via the linked Lobby.
            // Participant = BookingDeposits.UserId (BR-22 per-member deposit) for this booking.
            return await _db.BookingDeposits
                .AsNoTracking()
                .AnyAsync(d => d.BookingId == bookingId && d.UserId == userId);
        }

        public async Task<IReadOnlyList<Lobby>> GetOverlappingLobbiesAsync(
            Guid userId,
            DateOnly playDate,
            TimeOnly newScheduledStartTime,
            TimeOnly newScheduledEndTime,
            DateTime newRecruitmentDeadline, CancellationToken cancellationToken = default)
        {
            // BR-USER-LIMIT-02: 2 lobby/booking overlap nếu có intersection (cộng 30 phút đệm).
            // BR-NEW-15: Dùng TimeOnly PreferredStartTime/PreferredEndTime thay vì TimeSlot enum.
            var buffer = TimeSpan.FromMinutes(30);

            var query = _db.Lobbies
                .Where(l =>
                    l.Status != LobbyStatus.Closed
                    && l.Status != LobbyStatus.TimeoutFailed
                    && l.Status != LobbyStatus.HostCancelled
                    && l.Status != LobbyStatus.RejectedByCafe
                    && l.Status != LobbyStatus.ExpiredByCafe
                    && l.Status != LobbyStatus.RatingOpen
                    && (
                        l.HostUserId == userId
                        || l.Members.Any(m => m.UserId == userId && m.IsActive)
                    )
                    && l.PlayDate == playDate
                    && l.PreferredStartTime == newScheduledStartTime
                    && l.PreferredEndTime == newScheduledEndTime
                    && l.RecruitmentDeadline.HasValue);

            var lower = newRecruitmentDeadline - buffer;
            var upper = playDate.ToDateTime(newScheduledEndTime) + buffer;

            query = query.Where(l =>
                (l.RecruitmentDeadline >= lower && l.RecruitmentDeadline <= upper)
                || (l.ScheduledStartTime >= lower && l.ScheduledStartTime <= upper)
                || (l.RecruitmentDeadline <= lower && l.ScheduledStartTime >= upper));

            return await query.ToListAsync();
        }

        public async Task<int> CountActiveOrTerminalByHostPlayDateAsync(Guid hostUserId, DateOnly playDate, CancellationToken cancellationToken = default)
        {
            return await _db.Lobbies
                .Where(l => l.HostUserId == hostUserId && l.PlayDate == playDate)
                .CountAsync();
        }

        public async Task<BookingDeposit?> GetBookingByIdAsync(Guid bookingId, CancellationToken cancellationToken = default)
        {
            return await _db.BookingDeposits.FirstOrDefaultAsync(b => b.Id == bookingId);
        }

        public Task AddAsync(Lobby lobby, CancellationToken cancellationToken = default)
        {
            _db.Lobbies.Add(lobby);
            return Task.CompletedTask;
        }

        public Task AddMemberAsync(LobbyMember member, CancellationToken cancellationToken = default)
        {
            _db.LobbyMembers.Add(member);
            return Task.CompletedTask;
        }

        public Task AddReportAsync(LobbyReport report, CancellationToken cancellationToken = default)
        {
            _db.LobbyReports.Add(report);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Lobby lobby, CancellationToken cancellationToken = default)
        {
            lobby.UpdatedAt = DateTime.UtcNow;
            _db.Lobbies.Update(lobby);
            return Task.CompletedTask;
        }

        public async Task RemoveAsync(Lobby lobby, CancellationToken cancellationToken = default)
        {
            _db.LobbyMembers.RemoveRange(lobby.Members);
            _db.LobbyMessages.Where(m => m.LobbyId == lobby.Id);
            var messages = await _db.LobbyMessages.Where(m => m.LobbyId == lobby.Id).ToListAsync();
            _db.LobbyMessages.RemoveRange(messages);
            var invites = await _db.LobbyInvites.Where(i => i.LobbyId == lobby.Id).ToListAsync();
            _db.LobbyInvites.RemoveRange(invites);
            var reports = await _db.LobbyReports.Where(r => r.LobbyId == lobby.Id).ToListAsync();
            _db.LobbyReports.RemoveRange(reports);
            _db.Lobbies.Remove(lobby);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _db.SaveChangesAsync();
        }

        // === Admin: Reports ===

        public async Task<int> CountFailuresByTypeAsync(
            DateTime? fromUtc, DateTime? toUtc,
            LobbyStatus? failureType, CancellationToken cancellationToken = default)
        {
            var query = _db.Lobbies.AsQueryable();

            if (fromUtc.HasValue)
            {
                query = query.Where(l => l.UpdatedAt >= fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                query = query.Where(l => l.UpdatedAt <= toUtc.Value);
            }

            if (failureType.HasValue)
            {
                query = query.Where(l => l.Status == failureType.Value);
            }
            else
            {
                // Count all failure types
                var failureTypes = new[]
                {
                    LobbyStatus.TimeoutFailed,
                    LobbyStatus.HostCancelled,
                    LobbyStatus.RejectedByCafe,
                    LobbyStatus.ExpiredByCafe
                };
                query = query.Where(l => failureTypes.Contains(l.Status));
            }

            return await query.CountAsync();
        }

        /// <summary>
        /// BR-NEW-10 §XI.1 — Per-host failure count for cooling-off signal detection.
        /// </summary>
        public async Task<int> CountFailuresByTypeForHostAsync(
            Guid hostUserId,
            DateTime? fromUtc, DateTime? toUtc,
            LobbyStatus? failureType, CancellationToken cancellationToken = default)
        {
            var query = _db.Lobbies
                .Where(l => l.HostUserId == hostUserId);

            if (fromUtc.HasValue)
            {
                query = query.Where(l => l.UpdatedAt >= fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                query = query.Where(l => l.UpdatedAt <= toUtc.Value);
            }

            if (failureType.HasValue)
            {
                query = query.Where(l => l.Status == failureType.Value);
            }

            return await query.CountAsync();
        }

        public async Task<int> CountQuickCreateCancelAsync(
            Guid hostUserId,
            DateTime fromUtc,
            TimeSpan maxGap, CancellationToken cancellationToken = default)
        {
            // BR-RISK-01 (SIG-08): Host cancel trong khoảng (UpdatedAt - CreatedAt) < maxGap.
            // Lobby.Status is stored as varchar (string), not int — use string literals.
            // HostCancelled=3, RejectedByCafe=12, ExpiredByCafe=13
            var statusList = string.Join(",", new[] { "HostCancelled", "RejectedByCafe", "ExpiredByCafe" }
                .Select(s => $"'{s}'"));
            var intervalMinutes = maxGap.TotalMinutes;

            var sql = $@"
                SELECT count(*)::int
                FROM ""Lobbies"" AS l
                WHERE l.""HostUserId"" = '{hostUserId}'
                  AND l.""CreatedAt"" >= '{fromUtc:O}'
                  AND l.""Status"" IN ({statusList})
                  AND l.""UpdatedAt"" < l.""CreatedAt"" + interval '{intervalMinutes} minutes'";

            var result = await _db.Database
                .SqlQueryRaw<int>(sql)
                .ToListAsync();
            return result.FirstOrDefault();
        }

        public async Task<(IReadOnlyList<Lobby> Items, int TotalCount)> GetAdminLobbyFailuresAsync(
            int page, int pageSize,
            DateTime? fromUtc, DateTime? toUtc,
            LobbyStatus? failureType, CancellationToken cancellationToken = default)
        {
            var query = _db.Lobbies
                .Include(l => l.GameTemplate)
                .Include(l => l.HostUser)
                    .ThenInclude(u => u.Profile)
                .Include(l => l.Members)
                .AsQueryable();

            if (fromUtc.HasValue)
            {
                query = query.Where(l => l.UpdatedAt >= fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                query = query.Where(l => l.UpdatedAt <= toUtc.Value);
            }

            if (failureType.HasValue)
            {
                query = query.Where(l => l.Status == failureType.Value);
            }
            else
            {
                var failureTypes = new[]
                {
                    LobbyStatus.TimeoutFailed,
                    LobbyStatus.HostCancelled,
                    LobbyStatus.RejectedByCafe,
                    LobbyStatus.ExpiredByCafe
                };
                query = query.Where(l => failureTypes.Contains(l.Status));
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(l => l.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        /// <summary>
        /// Lấy danh sách lobby của 1 cafe cho Manager dashboard.
        /// Filter theo status và playDate, có phân trang.
        /// </summary>
        public async Task<(IReadOnlyList<Lobby> Items, int TotalCount)> GetByCafeAsync(
            Guid cafeId,
            DateOnly? playDate,
            List<LobbyStatus>? statuses,
            int page,
            int pageSize, CancellationToken cancellationToken = default)
        {
            var query = _db.Lobbies
                .Include(l => l.GameTemplate)
                .Include(l => l.HostUser)
                    .ThenInclude(u => u.Profile)
                .Include(l => l.Members)
                .Include(l => l.Reservation)
                .Where(l => l.CafeId == cafeId);

            if (playDate.HasValue)
            {
                query = query.Where(l => l.PlayDate == playDate.Value);
            }

            if (statuses != null && statuses.Count > 0)
            {
                query = query.Where(l => statuses.Contains(l.Status));
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }
    }
}