using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface ITournamentRepository
{
    // === Tournament CRUD ===
    Task<Tournament?> GetByIdAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task<Tournament?> GetByIdWithDetailsAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tournament>> GetByCafeAsync(Guid cafeId, TournamentStatus? status, CancellationToken cancellationToken = default);
    /// <summary>Lấy tất cả tournament đang mở đăng ký (mọi game), status RegistrationOpen + deadline còn hạn.</summary>
    Task<IReadOnlyList<Tournament>> GetAllOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy tất cả tournament với filter status optional.
    /// - status = null → trả tất cả tournament (mọi status, không filter).
    /// - status != null → filter theo TournamentStatus cụ thể.
    /// Mặc định sort theo StartTime asc.
    /// </summary>
    Task<IReadOnlyList<Tournament>> GetAllByStatusAsync(TournamentStatus? status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tournament>> GetUpcomingForClosingAsync(DateTime cutoffTime, CancellationToken cancellationToken = default);

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
    Task<IReadOnlyList<Tournament>> GetActiveByCafeAsync(Guid cafeId, CancellationToken cancellationToken = default);
    Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default);
    Task UpdateAsync(Tournament tournament, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    // === Participants ===
    Task<TournamentParticipant?> GetParticipantAsync(Guid tournamentId, Guid userId, CancellationToken cancellationToken = default);
    Task<TournamentParticipant?> GetParticipantByIdAsync(Guid participantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentParticipant>> GetParticipantsAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentParticipant>> GetCheckedInParticipantsAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task<int> CountActiveParticipantsAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>Lấy tất cả participations của 1 user (kèm Tournament + GameTemplate).</summary>
    Task<IReadOnlyList<TournamentParticipant>> GetParticipantsByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Top N UserProfiles theo GlobalElo desc.
    /// Nếu <paramref name="gameTemplateId"/> != null → chỉ aggregate Elo từ tournament thuộc game đó.
    /// (Hiện GlobalElo là tổng quát, filter theo game sẽ trở thành tổng Elo trừ đi tournament ngoài game.)
    /// </summary>
    Task<IReadOnlyList<UserProfile>> GetTopEloProfilesAsync(int topCount, Guid? gameTemplateId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-aggregate tournament stats cho nhiều user (tournamentsPlayed, championCount).
    /// Tránh N+1 query khi build leaderboard.
    /// Nếu <paramref name="gameTemplateId"/> != null → chỉ aggregate stats từ tournament thuộc game đó.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, (int TournamentsPlayed, int Champions)>> GetAggregatedTournamentStatsAsync(
        IReadOnlyCollection<Guid> userIds, Guid? gameTemplateId = null, CancellationToken cancellationToken = default);

    Task AddParticipantAsync(TournamentParticipant participant, CancellationToken cancellationToken = default);
    Task UpdateParticipantAsync(TournamentParticipant participant, CancellationToken cancellationToken = default);

    // === Matches ===
    Task<TournamentMatchBracket?> GetMatchByIdAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentMatchBracket>> GetMatchesByRoundAsync(Guid tournamentId, int roundNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentMatchBracket>> GetMatchesByTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentMatchBracket?> GetFinalMatchAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task AddMatchAsync(TournamentMatchBracket match, CancellationToken cancellationToken = default);
    Task AddMatchesAsync(IEnumerable<TournamentMatchBracket> matches, CancellationToken cancellationToken = default);
    Task UpdateMatchAsync(TournamentMatchBracket match, CancellationToken cancellationToken = default);
    Task DeleteMatchesByRoundAsync(Guid tournamentId, int roundNumber, CancellationToken cancellationToken = default);

    // === Elo Contribution (for accurate revert) ===
    Task AddEloContributionAsync(TournamentMatchEloContribution contribution, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentMatchEloContribution>> GetEloContributionsByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task DeleteEloContributionsByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);

    // === Admin: Full CRUD + Reports ===
    Task<(IReadOnlyList<Tournament> Items, int TotalCount)> GetAdminListAsync(
        int page, int pageSize, string? searchTerm, TournamentStatus? status, Guid? cafeId, CancellationToken cancellationToken = default);
    Task<Tournament?> GetAdminDetailAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task<int> CountAllAsync(CancellationToken cancellationToken = default);
    Task<int> CountByStatusAsync(TournamentStatus status, CancellationToken cancellationToken = default);
}
