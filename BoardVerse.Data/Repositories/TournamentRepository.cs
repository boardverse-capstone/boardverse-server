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

    public async Task<Tournament?> GetByIdAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.Matches)
            .Include(t => t.GameTemplate)
            .Include(t => t.Cafe)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);
    }

    public async Task<Tournament?> GetByIdWithDetailsAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
                .ThenInclude(p => p.User)
            .Include(t => t.Matches)
            .Include(t => t.GameTemplate)
            .Include(t => t.Cafe)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);
    }

    public async Task<IReadOnlyList<Tournament>> GetByCafeAsync(Guid cafeId, TournamentStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.GameTemplate)
            .Include(t => t.Cafe)
            .Where(t => t.CafeId == cafeId);

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        return await query
            .OrderByDescending(t => t.StartTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Tournament>> GetAllOpenAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.GameTemplate)
            .Include(t => t.Cafe)
            .Where(t => t.Status == TournamentStatus.RegistrationOpen
                && t.RegistrationDeadline > DateTime.UtcNow)
            .OrderBy(t => t.StartTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Tournament>> GetAllByStatusAsync(TournamentStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.GameTemplate)
            .Include(t => t.Cafe)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        return await query
            .OrderBy(t => t.StartTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Tournament>> GetUpcomingForClosingAsync(DateTime cutoffTime, CancellationToken cancellationToken = default)
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.GameTemplate)
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
            .Include(t => t.GameTemplate)
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
            .Include(t => t.GameTemplate)
            .Where(t => t.Status == TournamentStatus.OnGoing
                && t.CurrentRound == 1
                && t.StartedAt.HasValue
                && t.StartedAt >= windowStart)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Tournament>> GetActiveByCafeAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        return await _db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.GameTemplate)
            .Where(t => t.CafeId == cafeId
                && t.Status == TournamentStatus.OnGoing)
            .OrderByDescending(t => t.CurrentRound)
            .ThenBy(t => t.StartTime)
            .ToListAsync();
    }

    public async Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default)
    {
        await _db.Tournaments.AddAsync(tournament);
    }

    public Task UpdateAsync(Tournament tournament, CancellationToken cancellationToken = default)
    {
        _db.Tournaments.Update(tournament);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync();
    }

    // === Participants ===

    public async Task<TournamentParticipant?> GetParticipantAsync(Guid tournamentId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentParticipants
            .Include(p => p.User)
                .ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(p => p.TournamentId == tournamentId && p.UserId == userId);
    }

    public async Task<TournamentParticipant?> GetParticipantByIdAsync(Guid participantId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentParticipants
            .Include(p => p.User)
                .ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(p => p.Id == participantId);
    }

    public async Task<IReadOnlyList<TournamentParticipant>> GetParticipantsAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentParticipants
            .Include(p => p.User)
                .ThenInclude(u => u.Profile)
            .Where(p => p.TournamentId == tournamentId)
            .OrderBy(p => p.RegisteredAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TournamentParticipant>> GetCheckedInParticipantsAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentParticipants
            .Include(p => p.User)
                .ThenInclude(u => u.Profile)
            .Where(p => p.TournamentId == tournamentId
                && p.Status != TournamentParticipantStatus.Registered)
            .OrderBy(p => p.CheckedInAt)
            .ToListAsync();
    }

    public async Task<int> CountActiveParticipantsAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentParticipants
            .CountAsync(p => p.TournamentId == tournamentId
                && p.Status != TournamentParticipantStatus.Withdrawn
                && p.Status != TournamentParticipantStatus.NoShow);
    }

    public async Task AddParticipantAsync(TournamentParticipant participant, CancellationToken cancellationToken = default)
    {
        await _db.TournamentParticipants.AddAsync(participant);
    }

    public Task UpdateParticipantAsync(TournamentParticipant participant, CancellationToken cancellationToken = default)
    {
        _db.TournamentParticipants.Update(participant);
        return Task.CompletedTask;
    }

    // === Matches ===

    public async Task<TournamentMatchBracket?> GetMatchByIdAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentMatchBrackets
            .Include(m => m.Player1)
            .Include(m => m.Player2)
            .Include(m => m.Player3)
            .Include(m => m.Player4)
            .Include(m => m.WinnerPlayer)
            .FirstOrDefaultAsync(m => m.Id == matchId);
    }

    public async Task<IReadOnlyList<TournamentMatchBracket>> GetMatchesByRoundAsync(Guid tournamentId, int roundNumber, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentMatchBrackets
            .Where(m => m.TournamentId == tournamentId && m.RoundNumber == roundNumber)
            .OrderBy(m => m.MatchNumber)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TournamentMatchBracket>> GetMatchesByTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentMatchBrackets
            .Where(m => m.TournamentId == tournamentId)
            .OrderBy(m => m.RoundNumber).ThenBy(m => m.MatchNumber)
            .ToListAsync();
    }

    public async Task<TournamentMatchBracket?> GetFinalMatchAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentMatchBrackets
            .Include(m => m.Player1)
            .Include(m => m.Player2)
            .Include(m => m.Player3)
            .Include(m => m.Player4)
            .FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.IsFinal);
    }

    public async Task AddMatchAsync(TournamentMatchBracket match, CancellationToken cancellationToken = default)
    {
        await _db.TournamentMatchBrackets.AddAsync(match);
    }

    public async Task AddMatchesAsync(IEnumerable<TournamentMatchBracket> matches, CancellationToken cancellationToken = default)
    {
        await _db.TournamentMatchBrackets.AddRangeAsync(matches);
    }

    public Task UpdateMatchAsync(TournamentMatchBracket match, CancellationToken cancellationToken = default)
    {
        _db.TournamentMatchBrackets.Update(match);
        return Task.CompletedTask;
    }

    public async Task DeleteMatchesByRoundAsync(Guid tournamentId, int roundNumber, CancellationToken cancellationToken = default)
    {
        var matches = await _db.TournamentMatchBrackets
            .Where(m => m.TournamentId == tournamentId && m.RoundNumber == roundNumber)
            .ToListAsync();

        if (matches.Count == 0) return;

        var matchIds = matches.Select(m => m.Id).ToList();

        var contributions = await _db.TournamentMatchEloContributions
            .Where(c => matchIds.Contains(c.MatchId))
            .ToListAsync();
        _db.TournamentMatchEloContributions.RemoveRange(contributions);

        _db.TournamentMatchBrackets.RemoveRange(matches);
    }

    // === Elo Contribution ===
    public async Task AddEloContributionAsync(TournamentMatchEloContribution contribution, CancellationToken cancellationToken = default)
    {
        await _db.TournamentMatchEloContributions.AddAsync(contribution);
    }

    public async Task<IReadOnlyList<TournamentMatchEloContribution>> GetEloContributionsByMatchAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentMatchEloContributions
            .Where(x => x.MatchId == matchId)
            .ToListAsync();
    }

    public async Task DeleteEloContributionsByMatchAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var contributions = await _db.TournamentMatchEloContributions
            .Where(x => x.MatchId == matchId)
            .ToListAsync();
        _db.TournamentMatchEloContributions.RemoveRange(contributions);
    }

    public async Task<IReadOnlyList<TournamentParticipant>> GetParticipantsByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.TournamentParticipants
            .Include(p => p.User)
                .ThenInclude(u => u!.Profile)
            .Include(p => p.Tournament)
                .ThenInclude(t => t!.GameTemplate)
            .Include(p => p.Tournament)
                .ThenInclude(t => t!.Cafe)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<UserProfile>> GetTopEloProfilesAsync(int topCount, Guid? gameTemplateId = null, CancellationToken cancellationToken = default)
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
        IReadOnlyCollection<Guid> userIds, Guid? gameTemplateId = null, CancellationToken cancellationToken = default)
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

    // === Admin: Full CRUD + Reports ===

    public async Task<(IReadOnlyList<Tournament> Items, int TotalCount)> GetAdminListAsync(
        int page, int pageSize, string? searchTerm, TournamentStatus? status, Guid? cafeId, CancellationToken cancellationToken = default)
    {
        var query = _db.Tournaments
            .Include(t => t.GameTemplate)
            .Include(t => t.Cafe)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(t =>
                t.Title.ToLower().Contains(term) ||
                (t.Description != null && t.Description.ToLower().Contains(term)));
        }

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        if (cafeId.HasValue)
        {
            query = query.Where(t => t.CafeId == cafeId.Value);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(t => t.StartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Tournament?> GetAdminDetailAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        return await _db.Tournaments
            .Include(t => t.GameTemplate)
            .Include(t => t.Cafe)
            .Include(t => t.Participants)
                .ThenInclude(p => p.User)
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);
    }

    public async Task<int> CountAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Tournaments.CountAsync();
    }

    public async Task<int> CountByStatusAsync(TournamentStatus status, CancellationToken cancellationToken = default)
    {
        return await _db.Tournaments.CountAsync(t => t.Status == status);
    }
}