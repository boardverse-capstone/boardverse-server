using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class LeaderboardService : ILeaderboardService
    {
        private readonly IUserProfileRepository _repository;

        public LeaderboardService(IUserProfileRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// K-06: Get global karma leaderboard ordered by KarmaPoints DESC.
        /// </summary>
        public async Task<KarmaLeaderboardDto> GetKarmaLeaderboardAsync(int limit = 100)
        {
            var rows = await _repository.GetKarmaLeaderboardAsync(limit);

            var entries = rows.Select((r, index) => new KarmaLeaderboardEntryDto
            {
                Rank = index + 1,
                UserId = r.userId,
                Username = r.username,
                AvatarUrl = r.avatarUrl,
                KarmaPoints = r.karmaPoints,
                GamerTier = r.gamerTier
            }).ToList();

            return new KarmaLeaderboardDto
            {
                Entries = entries,
                GeneratedAt = DateTime.UtcNow
            };
        }
    }
}
