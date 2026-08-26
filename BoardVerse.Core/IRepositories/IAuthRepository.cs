using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IAuthRepository
    {
        Task<bool> UserExistsAsync(string email, string username, CancellationToken cancellationToken = default);
        Task<User?> GetByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken cancellationToken = default);
        Task<User?> GetByProviderAsync(string provider, string providerId, CancellationToken cancellationToken = default);
        Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
        Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<User?> GetByEmailVerificationTokenAsync(string token, CancellationToken cancellationToken = default);
        Task<User?> GetByPasswordResetTokenAsync(string token, CancellationToken cancellationToken = default);
        Task<RefreshToken?> GetActiveRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
        Task<bool> HasActiveProfileAsync(Guid userId, CancellationToken cancellationToken = default);
        Task AddUserAsync(User user, CancellationToken cancellationToken = default);
        Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);
        Task DeleteStaleRefreshTokensForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken = default);
        Task<int> DeleteAllStaleRefreshTokensAsync(DateTime utcNow, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
