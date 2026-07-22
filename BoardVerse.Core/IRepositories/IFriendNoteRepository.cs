using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface IFriendNoteRepository
{
    Task<FriendNote?> GetByIdAsync(Guid id);
    Task<FriendNote?> GetByOwnerAndFriendAsync(Guid ownerUserId, Guid friendUserId);
    Task<IReadOnlyList<FriendNote>> GetByOwnerAsync(Guid ownerUserId);
    Task AddAsync(FriendNote note);
    void Update(FriendNote note);
    void Remove(FriendNote note);
    Task SaveChangesAsync();
}
