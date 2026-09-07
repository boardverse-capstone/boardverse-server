using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class PlayerBoardGameSaveRepository : IPlayerBoardGameSaveRepository
{
    private readonly BoardVerseDbContext _context;

    public PlayerBoardGameSaveRepository(BoardVerseDbContext context)
    {
        _context = context;
    }

    public async Task<PlayerBoardGameSave?> GetByUserAndGameAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PlayerBoardGameSaves
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.GameTemplateId == gameTemplateId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerBoardGameSave>> GetByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PlayerBoardGameSaves
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.SavedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetSavedGameTemplateIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PlayerBoardGameSaves
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.GameTemplateId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PlayerBoardGameSaves
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == userId && x.GameTemplateId == gameTemplateId,
                cancellationToken);
    }

    public async Task AddAsync(
        PlayerBoardGameSave save,
        CancellationToken cancellationToken = default)
    {
        await _context.PlayerBoardGameSaves.AddAsync(save, cancellationToken);
    }

    public async Task DeleteAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.PlayerBoardGameSaves
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.GameTemplateId == gameTemplateId,
                cancellationToken);

        if (entity != null)
        {
            _context.PlayerBoardGameSaves.Remove(entity);
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
