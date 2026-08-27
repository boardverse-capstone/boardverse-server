using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Caching.Memory;

namespace BoardVerse.Services.Services
{
    public class LeaderboardService : ILeaderboardService
    {
        private readonly IUserProfileRepository _repository;
        private readonly IMemoryCache _cache;

        // K-06: cache 5 phút cho leaderboard (tránh spam DB).
        private static readonly TimeSpan KarmaCacheTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan EloCacheTtl = TimeSpan.FromMinutes(5);

        public LeaderboardService(IUserProfileRepository repository, IMemoryCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        /// <summary>K-06: Backwards-compatible simple karma leaderboard (no paging, no rank lookup).</summary>
        public async Task<KarmaLeaderboardDto> GetKarmaLeaderboardAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            var paged = await GetKarmaLeaderboardPagedAsync(0, NormaliseLimit(limit), viewerUserId: null, cancellationToken);
            return new KarmaLeaderboardDto
            {
                Entries = paged.Entries
                    .Select(e => new KarmaLeaderboardEntryDto
                    {
                        Rank = e.Rank,
                        UserId = e.UserId,
                        Username = e.Username,
                        AvatarUrl = e.AvatarUrl,
                        KarmaPoints = e.KarmaPoints,
                        GamerTier = e.GamerTier
                    })
                    .ToList(),
                GeneratedAt = paged.GeneratedAt
            };
        }

        /// <summary>K-06: Backwards-compatible simple elo leaderboard.</summary>
        public async Task<EloLeaderboardDto> GetEloLeaderboardAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            var paged = await GetEloLeaderboardPagedAsync(0, NormaliseLimit(limit), viewerUserId: null, cancellationToken);
            return new EloLeaderboardDto
            {
                Entries = paged.Entries
                    .Select(e => new EloLeaderboardEntryDto
                    {
                        Rank = e.Rank,
                        UserId = e.UserId,
                        Username = e.Username,
                        AvatarUrl = e.AvatarUrl,
                        GlobalElo = e.GlobalElo,
                        GamerTier = e.GamerTier,
                        Level = e.Level
                    })
                    .ToList(),
                GeneratedAt = paged.GeneratedAt
            };
        }

        /// <summary>K-06: Karma leaderboard with paging (top/offset) and optional viewer rank.</summary>
        public async Task<LeaderboardPagedDto<KarmaLeaderboardEntryDto>> GetKarmaLeaderboardPagedAsync(
            int offset, int limit, Guid? viewerUserId, CancellationToken cancellationToken = default)
        {
            offset = Math.Max(0, offset);
            limit = NormaliseLimit(limit);

            var cacheKey = $"lb:karma:{offset}:{limit}";
            LeaderboardPagedDto<KarmaLeaderboardEntryDto> result;

            if (_cache.TryGetValue(cacheKey, out LeaderboardPagedDto<KarmaLeaderboardEntryDto>? cached) && cached is not null)
            {
                result = cached;
            }
            else
            {
                var rows = await _repository.GetKarmaLeaderboardAsync(offset, limit);
                var total = await _repository.CountActiveKarmaUsersAsync();
                result = BuildKarmaPaged(rows, offset, limit, total);
                _cache.Set(cacheKey, result, KarmaCacheTtl);
            }

            if (viewerUserId.HasValue && result.UserRank is null)
            {
                var viewer = await _repository.GetUserRankAsync(viewerUserId.Value, LeaderboardMetric.Karma);
                result.UserRank = await BuildViewerRankAsync(viewer, LeaderboardMetric.Karma);
            }
            return result;
        }

        /// <summary>K-06: Elo leaderboard with paging (top/offset) and optional viewer rank.</summary>
        public async Task<LeaderboardPagedDto<EloLeaderboardEntryDto>> GetEloLeaderboardPagedAsync(
            int offset, int limit, Guid? viewerUserId, CancellationToken cancellationToken = default)
        {
            offset = Math.Max(0, offset);
            limit = NormaliseLimit(limit);

            var cacheKey = $"lb:elo:{offset}:{limit}";
            LeaderboardPagedDto<EloLeaderboardEntryDto> result;

            if (_cache.TryGetValue(cacheKey, out LeaderboardPagedDto<EloLeaderboardEntryDto>? cached) && cached is not null)
            {
                result = cached;
            }
            else
            {
                var rows = await _repository.GetEloLeaderboardAsync(offset, limit);
                var total = await _repository.CountActiveEloUsersAsync();
                result = BuildEloPaged(rows, offset, limit, total);
                _cache.Set(cacheKey, result, EloCacheTtl);
            }

            if (viewerUserId.HasValue && result.UserRank is null)
            {
                var viewer = await _repository.GetUserRankAsync(viewerUserId.Value, LeaderboardMetric.Elo);
                result.UserRank = await BuildViewerRankAsync(viewer, LeaderboardMetric.Elo);
            }
            return result;
        }

        /// <summary>K-06: Level leaderboard with paging (top/offset) and optional viewer rank.
        /// Sắp xếp theo Level DESC, CurrentExp DESC, Username ASC (tie-break).</summary>
        public async Task<LeaderboardPagedDto<LeaderboardEntryDto>> GetLevelLeaderboardPagedAsync(
            int offset, int limit, Guid? viewerUserId, CancellationToken cancellationToken = default)
        {
            offset = Math.Max(0, offset);
            limit = NormaliseLimit(limit);

            var cacheKey = $"lb:level:{offset}:{limit}";
            LeaderboardPagedDto<LeaderboardEntryDto> result;

            if (_cache.TryGetValue(cacheKey, out LeaderboardPagedDto<LeaderboardEntryDto>? cached) && cached is not null)
            {
                result = cached;
            }
            else
            {
                var rows = await _repository.GetLevelLeaderboardAsync(offset, limit);
                var total = await _repository.CountActiveLevelUsersAsync();
                result = BuildLevelPaged(rows, offset, limit, total);
                _cache.Set(cacheKey, result, KarmaCacheTtl);
            }

            if (viewerUserId.HasValue && result.UserRank is null)
            {
                var viewer = await _repository.GetUserRankAsync(viewerUserId.Value, LeaderboardMetric.Level);
                result.UserRank = await BuildViewerRankAsync(viewer, LeaderboardMetric.Level);
            }
            return result;
        }

        private static int NormaliseLimit(int limit)
        {
            if (limit <= 0) return 50;
            return Math.Clamp(limit, 1, 500);
        }

        private static LeaderboardPagedDto<KarmaLeaderboardEntryDto> BuildKarmaPaged(
            IReadOnlyList<KarmaLeaderboardRow> rows, int offset, int limit, long total)
        {
            var entries = rows.Select((r, i) => new KarmaLeaderboardEntryDto
            {
                Rank = offset + i + 1,
                UserId = r.UserId,
                Username = r.Username,
                AvatarUrl = r.AvatarUrl,
                KarmaPoints = r.KarmaPoints,
                GamerTier = r.GamerTier
            }).ToList();

            return new LeaderboardPagedDto<KarmaLeaderboardEntryDto>
            {
                Entries = entries,
                Offset = offset,
                Limit = limit,
                TotalCount = total,
                GeneratedAt = DateTime.UtcNow
            };
        }

        private static LeaderboardPagedDto<EloLeaderboardEntryDto> BuildEloPaged(
            IReadOnlyList<EloLeaderboardRow> rows, int offset, int limit, long total)
        {
            var entries = rows.Select((r, i) => new EloLeaderboardEntryDto
            {
                Rank = offset + i + 1,
                UserId = r.UserId,
                Username = r.Username,
                AvatarUrl = r.AvatarUrl,
                GlobalElo = r.GlobalElo,
                GamerTier = r.GamerTier,
                Level = r.Level
            }).ToList();

            return new LeaderboardPagedDto<EloLeaderboardEntryDto>
            {
                Entries = entries,
                Offset = offset,
                Limit = limit,
                TotalCount = total,
                GeneratedAt = DateTime.UtcNow
            };
        }

        private static LeaderboardPagedDto<LeaderboardEntryDto> BuildLevelPaged(
            IReadOnlyList<LeaderboardRankRow> rows, int offset, int limit, long total)
        {
            var entries = rows.Select((r, i) => new LeaderboardEntryDto
            {
                Rank = offset + i + 1,
                UserId = r.UserId,
                Username = r.Username,
                DisplayName = r.DisplayName,
                AvatarUrl = r.AvatarUrl,
                KarmaPoints = r.KarmaPoints,
                GlobalElo = r.GlobalElo,
                Level = r.Level,
                GamerTier = r.GamerTier
            }).ToList();

            return new LeaderboardPagedDto<LeaderboardEntryDto>
            {
                Entries = entries,
                Offset = offset,
                Limit = limit,
                TotalCount = total,
                GeneratedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Tính rank 1-based cho <paramref name="viewer"/> bằng cách scan top 1000 user
        /// đầu trên cùng metric (descending). Vì repository chưa có dedicated "count-ahead",
        /// em dùng cursor scan — đủ cho MVP. Khi vượt quá (viewer nằm ngoài top 1000) trả về null.
        /// </summary>
        private async Task<LeaderboardEntryDto?> BuildViewerRankAsync(LeaderboardRankRow? viewer, LeaderboardMetric metric)
        {
            if (viewer is null) return null;

            const int CursorLimit = 1000;
            IReadOnlyList<LeaderboardRankRow> rows = metric switch
            {
                LeaderboardMetric.Karma => await _repository.GetKarmaLeaderboardAsync(0, CursorLimit)
                                                ?? (IReadOnlyList<LeaderboardRankRow>)Array.Empty<LeaderboardRankRow>(),
                LeaderboardMetric.Elo => await _repository.GetEloLeaderboardAsync(0, CursorLimit)
                                                ?? (IReadOnlyList<LeaderboardRankRow>)Array.Empty<LeaderboardRankRow>(),
                LeaderboardMetric.Level => await _repository.GetLevelLeaderboardAsync(0, CursorLimit)
                                                ?? (IReadOnlyList<LeaderboardRankRow>)Array.Empty<LeaderboardRankRow>(),
                _ => (IReadOnlyList<LeaderboardRankRow>)Array.Empty<LeaderboardRankRow>()
            };

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].UserId == viewer.UserId)
                {
                    return new LeaderboardEntryDto
                    {
                        Rank = i + 1,
                        UserId = viewer.UserId,
                        Username = viewer.Username,
                        DisplayName = viewer.DisplayName,
                        AvatarUrl = viewer.AvatarUrl,
                        KarmaPoints = viewer.KarmaPoints,
                        GlobalElo = viewer.GlobalElo,
                        Level = viewer.Level,
                        GamerTier = viewer.GamerTier
                    };
                }
            }

            // Viewer nằm ngoài top 1000 — không trả rank (tránh scan toàn bảng mỗi request).
            return null;
        }
    }
}
