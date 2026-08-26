using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class TournamentWaitlistRepository : ITournamentWaitlistRepository
{
    private readonly BoardVerseDbContext _context;

    public TournamentWaitlistRepository(BoardVerseDbContext context)
    {
        _context = context;
    }

    public async Task<TournamentWaitlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TournamentWaitlists
            .Include(w => w.User)
            .Include(w => w.Tournament)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<TournamentWaitlist?> GetPendingByUserAsync(Guid tournamentId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.TournamentWaitlists
            .FirstOrDefaultAsync(w =>
                w.TournamentId == tournamentId &&
                w.UserId == userId &&
                w.Status == TournamentWaitlistStatus.Pending);
    }

    public async Task<IReadOnlyList<TournamentWaitlist>> GetByTournamentAsync(
        Guid tournamentId, TournamentWaitlistStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.TournamentWaitlists
            .Include(w => w.User)
            .Where(w => w.TournamentId == tournamentId);

        if (status.HasValue)
            query = query.Where(w => w.Status == status.Value);

        return await query.OrderBy(w => w.Position).ToListAsync();
    }

    public async Task<int> GetNextPositionAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var max = await _context.TournamentWaitlists
            .Where(w => w.TournamentId == tournamentId)
            .MaxAsync(w => (int?)w.Position);
        return (max ?? 0) + 1;
    }

    public async Task AddAsync(TournamentWaitlist entry, CancellationToken cancellationToken = default)
    {
        _context.TournamentWaitlists.Add(entry);
    }

    public Task UpdateAsync(TournamentWaitlist entry, CancellationToken cancellationToken = default)
    {
        _context.TournamentWaitlists.Update(entry);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await _context.TournamentWaitlists.FindAsync(id);
        if (entry != null)
            _context.TournamentWaitlists.Remove(entry);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync();
    }
}
