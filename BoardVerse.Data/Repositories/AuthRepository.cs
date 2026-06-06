using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class AuthRepository : IAuthRepository
    {
        private readonly BoardVerseDbContext _context;

        public AuthRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public Task<bool> UserExistsAsync(string email, string username)
        {
            return _context.Users.AnyAsync(u => u.Email == email || u.Username == username);
        }

        public Task<User?> GetByUsernameOrEmailAsync(string usernameOrEmail)
        {
            return _context.Users.FirstOrDefaultAsync(u => u.Username == usernameOrEmail || u.Email == usernameOrEmail);
        }

        public Task<User?> GetByProviderAsync(string provider, string providerId)
        {
            return _context.Users.FirstOrDefaultAsync(u => u.Provider == provider && u.ProviderId == providerId);
        }

        public Task<User?> GetByEmailAsync(string email)
        {
            return _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        }

        public Task<User?> GetByIdAsync(Guid userId)
        {
            return _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        }

        public Task<User?> GetByEmailVerificationTokenAsync(string token)
        {
            return _context.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == token);
        }

        public Task<User?> GetByPasswordResetTokenAsync(string token)
        {
            return _context.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == token);
        }

        public Task<RefreshToken?> GetActiveRefreshTokenAsync(string token)
        {
            return _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == token && !r.IsRevoked);
        }

        public Task<bool> IsTokenBlacklistedAsync(string token)
        {
            return _context.TokenBlacklists.AnyAsync(tb => tb.Token == token);
        }

        public Task AddUserAsync(User user)
        {
            _context.Users.Add(user);
            return Task.CompletedTask;
        }

        public Task AddRefreshTokenAsync(RefreshToken refreshToken)
        {
            _context.RefreshTokens.Add(refreshToken);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            return _context.SaveChangesAsync();
        }
    }
}
