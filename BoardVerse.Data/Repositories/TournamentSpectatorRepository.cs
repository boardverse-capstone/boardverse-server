using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class TournamentSpectatorRepository : ITournamentSpectatorRepository
{
    private readonly BoardVerseDbContext _context;

    public TournamentSpectatorRepository(BoardVerseDbContext context)
    {
        _context = context;
    }

    public async Task<TournamentSpectator?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TournamentSpectators
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<TournamentSpectator?> GetByUserAsync(Guid tournamentId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.TournamentSpectators
            .FirstOrDefaultAsync(s =>
                s.TournamentId == tournamentId &&
                s.UserId == userId);
    }

    public async Task<IReadOnlyList<TournamentSpectator>> GetByTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        return await _context.TournamentSpectators
            .Include(s => s.User)
            .Where(s => s.TournamentId == tournamentId)
            .OrderByDescending(s => s.JoinedAt)
            .ToListAsync();
    }

    public async Task AddAsync(TournamentSpectator spectator, CancellationToken cancellationToken = default)
    {
        _context.TournamentSpectators.Add(spectator);
        await Task.CompletedTask;
    }

    public Task UpdateAsync(TournamentSpectator spectator, CancellationToken cancellationToken = default)
    {
        _context.TournamentSpectators.Update(spectator);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var spectator = await _context.TournamentSpectators.FindAsync(id);
        if (spectator != null)
            _context.TournamentSpectators.Remove(spectator);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync();
    }
}
