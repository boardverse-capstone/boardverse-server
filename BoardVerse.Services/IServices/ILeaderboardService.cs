using BoardVerse.Core.DTOs.User;

namespace BoardVerse.Services.IServices
{
    public interface ILeaderboardService
    {
        /// <summary>
        /// K-06: Get global karma leaderboard ordered by KarmaPoints DESC.
        /// </summary>
        Task<KarmaLeaderboardDto> GetKarmaLeaderboardAsync(int limit = 100);
    }
}
