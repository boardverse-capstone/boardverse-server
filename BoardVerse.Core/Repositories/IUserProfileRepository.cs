using BoardVerse.Core.Entities;

namespace BoardVerse.Core.Repositories
{
    public interface IUserProfileRepository
    {
        Task<User?> GetByIdWithProfileAsync(Guid userId);
        Task<UserProfile?> GetProfileByUserIdAsync(Guid userId);
        Task AddUserProfileAsync(UserProfile profile);
        Task SaveChangesAsync();
    }
}
