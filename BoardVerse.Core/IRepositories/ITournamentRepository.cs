using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

public interface ITournamentRepository
{
    // === Tournament CRUD ===
    Task<Tournament?> GetByIdAsync(Guid tournamentId);
    Task<Tournament?> GetByIdWithDetailsAsync(Guid tournamentId);
    Task<IReadOnlyList<Tournament>> GetByCafeAsync(Guid cafeId, TournamentStatus? status);
    Task<IReadOnlyList<Tournament>> GetOpenTournamentsForGameAsync(Guid gameTemplateId);
    Task<IReadOnlyList<Tournament>> GetUpcomingForClosingAsync(DateTime cutoffTime);

    /// <summary>
    /// Lấy tournament sắp bắt đầu trong khoảng 30 phút tới (cho reminder job).
    /// Chỉ lấy tournament ở trạng thái RegistrationOpen/RegistrationClosed.
    /// </summary>
    Task<IReadOnlyList<Tournament>> GetTournamentsStartingSoonAsync(DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Lấy tournament vừa start (OnGoing + CurrentRound = 1 + StartRoundAt gần đây) để detect no-show.
    /// </summary>
    Task<IReadOnlyList<Tournament>> GetTournamentsJustStartedAsync(CancellationToken ct = default);

    /// <summary>Tournament đang OnGoing của 1 cafe (manager dashboard).</summary>
    Task<IReadOnlyList<Tournament>> GetActiveByCafeAsync(Guid cafeId);
    Task AddAsync(Tournament tournament);
    Task UpdateAsync(Tournament tournament);
    Task SaveChangesAsync();

    // === Participants ===
    Task<TournamentParticipant?> GetParticipantAsync(Guid tournamentId, Guid userId);
    Task<TournamentParticipant?> GetParticipantByIdAsync(Guid participantId);
    Task<IReadOnlyList<TournamentParticipant>> GetParticipantsAsync(Guid tournamentId);
    Task<IReadOnlyList<TournamentParticipant>> GetCheckedInParticipantsAsync(Guid tournamentId);
    Task<int> CountActiveParticipantsAsync(Guid tournamentId);

    /// <summary>Lấy tất cả participations của 1 user (kèm Tournament + GameTemplate).</summary>
    Task<IReadOnlyList<TournamentParticipant>> GetParticipantsByUserAsync(Guid userId);

    /// <summary>
    /// Top N UserProfiles theo GlobalElo desc.
    /// Nếu <paramref name="gameTemplateId"/> != null → chỉ aggregate Elo từ tournament thuộc game đó.
    /// (Hiện GlobalElo là tổng quát, filter theo game sẽ trở thành tổng Elo trừ đi tournament ngoài game.)
    /// </summary>
    Task<IReadOnlyList<UserProfile>> GetTopEloProfilesAsync(int topCount, Guid? gameTemplateId = null);

    /// <summary>
    /// Bulk-aggregate tournament stats cho nhiều user (tournamentsPlayed, championCount).
    /// Tránh N+1 query khi build leaderboard.
    /// Nếu <paramref name="gameTemplateId"/> != null → chỉ aggregate stats từ tournament thuộc game đó.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, (int TournamentsPlayed, int Champions)>> GetAggregatedTournamentStatsAsync(
        IReadOnlyCollection<Guid> userIds, Guid? gameTemplateId = null);

    Task AddParticipantAsync(TournamentParticipant participant);
    Task UpdateParticipantAsync(TournamentParticipant participant);

    // === Matches ===
    Task<TournamentMatchBracket?> GetMatchByIdAsync(Guid matchId);
    Task<IReadOnlyList<TournamentMatchBracket>> GetMatchesByRoundAsync(Guid tournamentId, int roundNumber);
    Task<IReadOnlyList<TournamentMatchBracket>> GetMatchesByTournamentAsync(Guid tournamentId);
    Task<TournamentMatchBracket?> GetFinalMatchAsync(Guid tournamentId);
    Task AddMatchAsync(TournamentMatchBracket match);
    Task AddMatchesAsync(IEnumerable<TournamentMatchBracket> matches);
    Task UpdateMatchAsync(TournamentMatchBracket match);

    // === Elo Contribution (for accurate revert) ===
    Task AddEloContributionAsync(TournamentMatchEloContribution contribution);
    Task<IReadOnlyList<TournamentMatchEloContribution>> GetEloContributionsByMatchAsync(Guid matchId);
    Task DeleteEloContributionsByMatchAsync(Guid matchId);
}
