using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class FriendNoteRepository : IFriendNoteRepository
{
    private readonly BoardVerseDbContext _db;

    public FriendNoteRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<FriendNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.FriendNotes.Include(n => n.Friend).ThenInclude(u => u.Profile).FirstOrDefaultAsync(n => n.Id == id);

    public Task<FriendNote?> GetByOwnerAndFriendAsync(Guid ownerUserId, Guid friendUserId, CancellationToken cancellationToken = default)
        => _db.FriendNotes.FirstOrDefaultAsync(n => n.OwnerUserId == ownerUserId && n.FriendUserId == friendUserId);

    public async Task<IReadOnlyList<FriendNote>> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
        => await _db.FriendNotes
            .Include(n => n.Friend).ThenInclude(u => u.Profile)
            .Where(n => n.OwnerUserId == ownerUserId)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();

    public Task AddAsync(FriendNote note, CancellationToken cancellationToken = default)
    {
        _db.FriendNotes.Add(note);
        return Task.CompletedTask;
    }

    public void Update(FriendNote note) => _db.FriendNotes.Update(note);

    public void Remove(FriendNote note) => _db.FriendNotes.Remove(note);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync();
}
