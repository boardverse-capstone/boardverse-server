using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface IAuthRepository
    {
        Task<bool> UserExistsAsync(string email, string username);
        Task<User?> GetByUsernameOrEmailAsync(string usernameOrEmail);
        Task<User?> GetByProviderAsync(string provider, string providerId);
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByIdAsync(Guid userId);
        Task<User?> GetByEmailVerificationTokenAsync(string token);
        Task<User?> GetByPasswordResetTokenAsync(string token);
        Task<RefreshToken?> GetActiveRefreshTokenAsync(string token);
        Task<bool> HasActiveProfileAsync(Guid userId);
        Task AddUserAsync(User user);
        Task AddRefreshTokenAsync(RefreshToken refreshToken);
        Task DeleteStaleRefreshTokensForUserAsync(Guid userId, DateTime utcNow);
        Task<int> DeleteAllStaleRefreshTokensAsync(DateTime utcNow);
        Task SaveChangesAsync();
    }
}
