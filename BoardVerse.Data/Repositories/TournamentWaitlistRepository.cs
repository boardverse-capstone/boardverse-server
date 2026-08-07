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

    public async Task<TournamentWaitlist?> GetByIdAsync(Guid id)
    {
        return await _context.TournamentWaitlists
            .Include(w => w.User)
            .Include(w => w.Tournament)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<TournamentWaitlist?> GetPendingByUserAsync(Guid tournamentId, Guid userId)
    {
        return await _context.TournamentWaitlists
            .FirstOrDefaultAsync(w =>
                w.TournamentId == tournamentId &&
                w.UserId == userId &&
                w.Status == TournamentWaitlistStatus.Pending);
    }

    public async Task<IReadOnlyList<TournamentWaitlist>> GetByTournamentAsync(
        Guid tournamentId, TournamentWaitlistStatus? status = null)
    {
        var query = _context.TournamentWaitlists
            .Include(w => w.User)
            .Where(w => w.TournamentId == tournamentId);

        if (status.HasValue)
            query = query.Where(w => w.Status == status.Value);

        return await query.OrderBy(w => w.Position).ToListAsync();
    }

    public async Task<int> GetNextPositionAsync(Guid tournamentId)
    {
        var max = await _context.TournamentWaitlists
            .Where(w => w.TournamentId == tournamentId)
            .MaxAsync(w => (int?)w.Position);
        return (max ?? 0) + 1;
    }

    public async Task AddAsync(TournamentWaitlist entry)
    {
        _context.TournamentWaitlists.Add(entry);
    }

    public Task UpdateAsync(TournamentWaitlist entry)
    {
        _context.TournamentWaitlists.Update(entry);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id)
    {
        var entry = await _context.TournamentWaitlists.FindAsync(id);
        if (entry != null)
            _context.TournamentWaitlists.Remove(entry);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
