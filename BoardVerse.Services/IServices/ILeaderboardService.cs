using BoardVerse.Core.DTOs.User;

namespace BoardVerse.Services.IServices
{
    public interface ILeaderboardService
    {
        /// <summary>
        /// K-06: Backwards-compatible simple karma leaderboard ordered by KarmaPoints DESC.
        /// </summary>
        Task<KarmaLeaderboardDto> GetKarmaLeaderboardAsync(int limit = 100);

        /// <summary>
        /// K-06: Global elo leaderboard ordered by GlobalElo DESC.
        /// </summary>
        Task<EloLeaderboardDto> GetEloLeaderboardAsync(int limit = 100);

        /// <summary>
        /// K-06: Karma leaderboard with paging (offset/limit) and optional viewer rank lookup.
        /// </summary>
        Task<LeaderboardPagedDto<KarmaLeaderboardEntryDto>> GetKarmaLeaderboardPagedAsync(
            int offset, int limit, Guid? viewerUserId);

        /// <summary>
        /// K-06: Elo leaderboard with paging (offset/limit) and optional viewer rank lookup.
        /// </summary>
        Task<LeaderboardPagedDto<EloLeaderboardEntryDto>> GetEloLeaderboardPagedAsync(
            int offset, int limit, Guid? viewerUserId);

        /// <summary>
        /// K-06: Level leaderboard with paging (offset/limit) and optional viewer rank lookup.
        /// Sắp xếp theo Level DESC, CurrentExp DESC, Username ASC.
        /// </summary>
        Task<LeaderboardPagedDto<LeaderboardEntryDto>> GetLevelLeaderboardPagedAsync(
            int offset, int limit, Guid? viewerUserId);
    }
}
