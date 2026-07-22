using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class TournamentRepository : ITournamentRepository
{
    private readonly BoardVerseDbContext _db;

    public TournamentRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    // === Tournament CRUD ===

    public async Task<Tournament?> GetByIdAsync(Guid tournamentId)
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);
    }

    public async Task<Tournament?> GetByIdWithDetailsAsync(Guid tournamentId)
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
                .ThenInclude(p => p.User)
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);
    }

    public async Task<IReadOnlyList<Tournament>> GetByCafeAsync(Guid cafeId, TournamentStatus? status)
    {
        var query = _db.Tournaments
            .Include(t => t.Participants)
            .Where(t => t.CafeId == cafeId);

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        return await query
            .OrderByDescending(t => t.StartTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Tournament>> GetAllOpenAsync()
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Where(t => t.Status == TournamentStatus.RegistrationOpen
                && t.RegistrationDeadline > DateTime.UtcNow)
            .OrderBy(t => t.StartTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Tournament>> GetUpcomingForClosingAsync(DateTime cutoffTime)
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Where(t => t.Status == TournamentStatus.RegistrationOpen
                && t.RegistrationDeadline <= cutoffTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Tournament>> GetTournamentsStartingSoonAsync(DateTime now, CancellationToken ct = default)
    {
        var windowEnd = now.AddMinutes(30);
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.Cafe)
            .Where(t => (t.Status == TournamentStatus.RegistrationOpen || t.Status == TournamentStatus.RegistrationClosed)
                && t.StartTime > now
                && t.StartTime <= windowEnd)
            .OrderBy(t => t.StartTime)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Tournament>> GetTournamentsJustStartedAsync(CancellationToken ct = default)
    {
        // Lấy tournament OnGoing, Round 1, started trong vòng 5 phút gần đây
        var windowStart = DateTime.UtcNow.AddMinutes(-5);
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Where(t => t.Status == TournamentStatus.OnGoing
                && t.CurrentRound == 1
                && t.StartedAt.HasValue
                && t.StartedAt >= windowStart)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Tournament>> GetActiveByCafeAsync(Guid cafeId)
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Where(t => t.CafeId == cafeId
                && t.Status == TournamentStatus.OnGoing)
            .OrderByDescending(t => t.CurrentRound)
            .ThenBy(t => t.StartTime)
            .ToListAsync();
    }

    public async Task AddAsync(Tournament tournament)
    {
        await _db.Tournaments.AddAsync(tournament);
    }

    public Task UpdateAsync(Tournament tournament)
    {
        _db.Tournaments.Update(tournament);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }

    // === Participants ===

    public async Task<TournamentParticipant?> GetParticipantAsync(Guid tournamentId, Guid userId)
    {
        return await _db.TournamentParticipants
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.TournamentId == tournamentId && p.UserId == userId);
    }

    public async Task<TournamentParticipant?> GetParticipantByIdAsync(Guid participantId)
    {
        return await _db.TournamentParticipants
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == participantId);
    }

    public async Task<IReadOnlyList<TournamentParticipant>> GetParticipantsAsync(Guid tournamentId)
    {
        return await _db.TournamentParticipants
            .Include(p => p.User)
            .Where(p => p.TournamentId == tournamentId)
            .OrderBy(p => p.RegisteredAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TournamentParticipant>> GetCheckedInParticipantsAsync(Guid tournamentId)
    {
        return await _db.TournamentParticipants
            .Include(p => p.User)
            .Where(p => p.TournamentId == tournamentId
                && p.Status != TournamentParticipantStatus.Registered)
            .OrderBy(p => p.CheckedInAt)
            .ToListAsync();
    }

    public async Task<int> CountActiveParticipantsAsync(Guid tournamentId)
    {
        return await _db.TournamentParticipants
            .CountAsync(p => p.TournamentId == tournamentId
                && p.Status != TournamentParticipantStatus.Withdrawn
                && p.Status != TournamentParticipantStatus.NoShow);
    }

    public async Task AddParticipantAsync(TournamentParticipant participant)
    {
        await _db.TournamentParticipants.AddAsync(participant);
    }

    public Task UpdateParticipantAsync(TournamentParticipant participant)
    {
        _db.TournamentParticipants.Update(participant);
        return Task.CompletedTask;
    }

    // === Matches ===

    public async Task<TournamentMatchBracket?> GetMatchByIdAsync(Guid matchId)
    {
        return await _db.TournamentMatchBrackets
            .Include(m => m.Player1)
            .Include(m => m.Player2)
            .Include(m => m.Player3)
            .Include(m => m.Player4)
            .Include(m => m.WinnerPlayer)
            .FirstOrDefaultAsync(m => m.Id == matchId);
    }

    public async Task<IReadOnlyList<TournamentMatchBracket>> GetMatchesByRoundAsync(Guid tournamentId, int roundNumber)
    {
        return await _db.TournamentMatchBrackets
            .Where(m => m.TournamentId == tournamentId && m.RoundNumber == roundNumber)
            .OrderBy(m => m.MatchNumber)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TournamentMatchBracket>> GetMatchesByTournamentAsync(Guid tournamentId)
    {
        return await _db.TournamentMatchBrackets
            .Where(m => m.TournamentId == tournamentId)
            .OrderBy(m => m.RoundNumber).ThenBy(m => m.MatchNumber)
            .ToListAsync();
    }

    public async Task<TournamentMatchBracket?> GetFinalMatchAsync(Guid tournamentId)
    {
        return await _db.TournamentMatchBrackets
            .Include(m => m.Player1)
            .Include(m => m.Player2)
            .Include(m => m.Player3)
            .Include(m => m.Player4)
            .FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.IsFinal);
    }

    public async Task AddMatchAsync(TournamentMatchBracket match)
    {
        await _db.TournamentMatchBrackets.AddAsync(match);
    }

    public async Task AddMatchesAsync(IEnumerable<TournamentMatchBracket> matches)
    {
        await _db.TournamentMatchBrackets.AddRangeAsync(matches);
    }

    public Task UpdateMatchAsync(TournamentMatchBracket match)
    {
        _db.TournamentMatchBrackets.Update(match);
        return Task.CompletedTask;
    }

    // === Elo Contribution ===
    public async Task AddEloContributionAsync(TournamentMatchEloContribution contribution)
    {
        await _db.TournamentMatchEloContributions.AddAsync(contribution);
    }

    public async Task<IReadOnlyList<TournamentMatchEloContribution>> GetEloContributionsByMatchAsync(Guid matchId)
    {
        return await _db.TournamentMatchEloContributions
            .Where(x => x.MatchId == matchId)
            .ToListAsync();
    }

    public async Task DeleteEloContributionsByMatchAsync(Guid matchId)
    {
        var contributions = await _db.TournamentMatchEloContributions
            .Where(x => x.MatchId == matchId)
            .ToListAsync();
        _db.TournamentMatchEloContributions.RemoveRange(contributions);
    }

    public async Task<IReadOnlyList<TournamentParticipant>> GetParticipantsByUserAsync(Guid userId)
    {
        return await _db.TournamentParticipants
            .Include(p => p.Tournament)
                .ThenInclude(t => t!.GameTemplate)
            .Include(p => p.Tournament)
                .ThenInclude(t => t!.Cafe)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<UserProfile>> GetTopEloProfilesAsync(int topCount, Guid? gameTemplateId = null)
    {
        // GlobalElo là tổng quát (BR-10); không filter theo game ở query này.
        // Filter theo game chỉ áp dụng cho AggregatedStats (TournamentsPlayed / Champions count).
        _ = gameTemplateId; // suppress unused warning
        return await _db.UserProfiles
            .Include(pr => pr.User)
            .Where(pr => pr.GlobalElo > 0)
            .OrderByDescending(pr => pr.GlobalElo)
                .ThenBy(pr => pr.UserId) // tiebreaker stable
            .Take(topCount)
            .ToListAsync();
    }

    public async Task<IReadOnlyDictionary<Guid, (int TournamentsPlayed, int Champions)>> GetAggregatedTournamentStatsAsync(
        IReadOnlyCollection<Guid> userIds, Guid? gameTemplateId = null)
    {
        if (userIds == null || userIds.Count == 0)
        {
            return new Dictionary<Guid, (int, int)>();
        }

        // Chỉ đếm participant rows đã tham gia đến cuối tournament
        // (Finished = đã chơi xong; Active = đã check-in vào vòng đấu).
        // Withdrawn / NoShow / Registered chưa thật sự tham gia → không tính.
        var countableStatuses = new[]
        {
            TournamentParticipantStatus.Finished,
            TournamentParticipantStatus.Active
        };

        // Walk-in có UserId=null → filter ra để không match vào userIds collection.
        var query = _db.TournamentParticipants
            .Where(p => p.UserId != null && userIds.Contains(p.UserId.Value) && countableStatuses.Contains(p.Status));

        if (gameTemplateId.HasValue)
        {
            query = query.Where(p => p.Tournament != null && p.Tournament.GameTemplateId == gameTemplateId.Value);
        }

        var grouped = await query
            .GroupBy(p => p.UserId!.Value)
            .Select(g => new
            {
                UserId = g.Key,
                TournamentsPlayed = g.Count(),
                Champions = g.Count(p => p.FinalRank == 1)
            })
            .ToListAsync();

        return grouped.ToDictionary(
            x => x.UserId,
            x => (x.TournamentsPlayed, x.Champions));
    }
}